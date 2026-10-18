using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    ICurrentSchoolContext currentSchool,
    IEmailSender emailSender,
    IOptions<AppLinkOptions> appLinkOptions) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await db.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto(
                u.Id, u.Username, u.Role.ToString(), u.Phone, u.Email,
                db.Students.Where(s => s.UserId == u.Id).Select(s => (int?)s.Id).FirstOrDefault()))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> AddUser(CreateUserRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return BadRequest("Username already exists.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role) || role == UserRole.SuperAdmin)
        {
            return BadRequest("Unknown role.");
        }

        var (linkedStudent, linkError) = await ResolveStudentLinkAsync(role, request.StudentId, currentUserId: null);
        if (linkError is not null)
        {
            return linkError;
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = string.Empty,
            Role = role,
            // Admin only ever manages their own school — never trust a
            // client-supplied SchoolId here.
            SchoolId = currentSchool.SchoolId,
            Phone = request.Phone,
            Email = request.Email,
            // Admin picked this password, not the account's owner — force a
            // change on first login (see User.MustChangePassword).
            MustChangePassword = true,
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (linkedStudent is not null)
        {
            linkedStudent.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        var dto = new UserDto(user.Id, user.Username, user.Role.ToString(), user.Phone, user.Email, linkedStudent?.Id);
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, dto);
    }

    /// <summary>Password is optional — omit it to change only username/role.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, UpdateUserRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await db.Users.AnyAsync(u => u.Id != id && u.Username == request.Username))
        {
            return BadRequest("Username already exists.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role) || role == UserRole.SuperAdmin)
        {
            return BadRequest("Unknown role.");
        }

        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        var (linkedStudent, linkError) = await ResolveStudentLinkAsync(role, request.StudentId, currentUserId: id);
        if (linkError is not null)
        {
            return linkError;
        }

        // Clear a stale link from before this update — role changed away
        // from Student, or the linked student changed — before applying the
        // (possibly different) one resolved above.
        var previousLink = await db.Students.FirstOrDefaultAsync(s => s.UserId == id);
        if (previousLink is not null && previousLink.Id != linkedStudent?.Id)
        {
            previousLink.UserId = null;
        }
        if (linkedStudent is not null)
        {
            linkedStudent.UserId = id;
        }

        user.Username = request.Username;
        user.Role = role;
        user.Phone = request.Phone;
        user.Email = request.Email;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            // Admin reset this password on the user's behalf — same forced
            // first-login change as a brand new account, see AddUser above.
            user.MustChangePassword = true;
        }

        await db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Username, user.Role.ToString(), user.Phone, user.Email, linkedStudent?.Id));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == currentUserId)
        {
            return BadRequest("Nelze smazat vlastní účet.");
        }

        if (user.Role == UserRole.Admin && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        db.ParentStudents.RemoveRange(db.ParentStudents.Where(ps => ps.UserId == id));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>E-mails <paramref name="id"/> a one-time link to set their own
    /// password (<see cref="User.InvitationToken"/>), valid 7 days. Coexists
    /// with the admin-sets-a-password flow in <see cref="AddUser"/>/
    /// <see cref="UpdateUser"/> — this is an alternative the Admin can trigger
    /// any time the account has an e-mail on file, not a replacement for it.</summary>
    [HttpPost("{id:int}/invite")]
    public async Task<IActionResult> SendInvite(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return BadRequest("Uživatel nemá vyplněný e-mail.");
        }

        var linkOptions = appLinkOptions.Value;
        if (linkOptions.PathBasedTenant && string.IsNullOrWhiteSpace(linkOptions.FrontendBaseUrl)
            || !linkOptions.PathBasedTenant && string.IsNullOrWhiteSpace(linkOptions.MainDomain))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "E-mailové pozvánky nejsou v tomhle prostředí nakonfigurované.");
        }

        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId);

        user.InvitationToken = GenerateToken();
        user.InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await db.SaveChangesAsync();

        var link = BuildInviteLink(linkOptions, school.Slug, user.InvitationToken);
        var subject = $"Pozvánka do Sovy — {school.Name}";
        var body = $"""
            <p>Dobrý den,</p>
            <p>byl(a) jste přihlášen(a) do školního systému Soovioo pro školu <strong>{school.Name}</strong>.
            Pro nastavení vlastního hesla a přihlášení klikněte na odkaz níže:</p>
            <p><a href="{link}">{link}</a></p>
            <p>Odkaz je platný 7 dní.</p>
            """;
        await emailSender.SendAsync(user.Email, subject, body);

        return NoContent();
    }

    private static string BuildInviteLink(AppLinkOptions options, string schoolSlug, string token)
    {
        var origin = options.PathBasedTenant
            ? $"{options.FrontendBaseUrl!.TrimEnd('/')}/{schoolSlug}"
            : $"https://{schoolSlug}.{options.MainDomain}";
        return $"{origin}/accept-invite?token={Uri.EscapeDataString(token)}";
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>True if <paramref name="userId"/> is the only Admin left for
    /// the current school — deleting/demoting them would leave the school
    /// with no one able to manage it.</summary>
    private async Task<bool> IsLastAdminAsync(int userId) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin);

    /// <summary>Validates and resolves the <see cref="Student"/> a Student-role
    /// account must link to (null/error otherwise). Doesn't write anything —
    /// callers apply <c>Student.UserId</c> themselves once the rest of the
    /// request has also validated, so a half-applied link never survives
    /// an otherwise-rejected request. <paramref name="currentUserId"/> is the
    /// user being updated (null when creating), so re-saving a Student
    /// account with the same already-linked student isn't rejected as
    /// "already taken".</summary>
    private async Task<(Student? Student, ActionResult<UserDto>? Error)> ResolveStudentLinkAsync(
        UserRole role, int? requestedStudentId, int? currentUserId)
    {
        if (role != UserRole.Student)
        {
            return (null, null);
        }

        if (requestedStudentId is not { } studentId)
        {
            return (null, BadRequest("Role Student vyžaduje vybraného žáka."));
        }

        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return (null, BadRequest("Unknown studentId."));
        }

        if (student.UserId is not null && student.UserId != currentUserId)
        {
            return (null, BadRequest("Tento žák už má uživatelský účet."));
        }

        return (student, null);
    }
}

public record CreateUserRequest(
    string Username, string Password, string Role, string? Phone = null, string? Email = null, int? StudentId = null);

public record UpdateUserRequest(
    string Username, string Role, string? Password = null, string? Phone = null, string? Email = null, int? StudentId = null);

/// <summary><see cref="StudentId"/> is set only for role Student — the one
/// <see cref="Evidence.Student"/> this account logs in as.</summary>
public record UserDto(int Id, string Username, string Role, string? Phone, string? Email, int? StudentId = null);
