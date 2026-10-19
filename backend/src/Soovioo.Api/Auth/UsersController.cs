using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    ICurrentSchoolContext currentSchool,
    IEmailSender emailSender,
    IOptions<AppLinkOptions> appLinkOptions,
    RefreshTokenService refreshTokens) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await db.Users
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                User = u,
                u.Person,
                u.Person!.TeacherProfile,
                StudentId = db.Students.Where(s => s.UserId == u.Id).Select(s => (int?)s.Id).FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(users.Select(u => ToDto(u.User, u.StudentId)));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> AddUser(CreateUserRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return this.FieldError("username", "Uživatelské jméno už existuje.");
        }

        var (roles, rolesError) = ParseRoles(request.Roles);
        if (rolesError is not null)
        {
            return rolesError;
        }

        var (linkedStudent, linkError) = await ResolveStudentLinkAsync(roles, request.StudentId, currentUserId: null);
        if (linkError is not null)
        {
            return linkError;
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = string.Empty,
            Roles = roles,
            Role = roles[0],
            // Admin only ever manages their own school — never trust a
            // client-supplied SchoolId here.
            SchoolId = currentSchool.SchoolId,
            // Admin picked this password, not the account's owner — force a
            // change on first login (see User.MustChangePassword).
            MustChangePassword = true,
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await ApplyPersonAsync(user, roles, request.FirstName, request.LastName, request.Phone, request.Email);
        await db.SaveChangesAsync();

        if (linkedStudent is not null)
        {
            linkedStudent.UserId = user.Id;
            await db.SaveChangesAsync();
        }

        var dto = ToDto(user, linkedStudent?.Id);
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, dto);
    }

    /// <summary>Password is optional — omit it to change only username/role.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, UpdateUserRequest request)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        if (await db.Users.AnyAsync(u => u.Id != id && u.Username == request.Username))
        {
            return this.FieldError("username", "Uživatelské jméno už existuje.");
        }

        var (roles, rolesError) = ParseRoles(request.Roles);
        if (rolesError is not null)
        {
            return rolesError;
        }

        if (user.Roles.Contains(UserRole.Admin) && !roles.Contains(UserRole.Admin) && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        var (linkedStudent, linkError) = await ResolveStudentLinkAsync(roles, request.StudentId, currentUserId: id);
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
        // Keep the active role when it survived the edit, otherwise fall
        // back to the first remaining one (ParseRoles sorts them).
        var activeRole = roles.Contains(user.Role) ? user.Role : roles[0];
        user.Roles = roles;
        user.Role = activeRole;
        await ApplyPersonAsync(user, roles, request.FirstName, request.LastName, request.Phone, request.Email);
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            // Admin reset this password on the user's behalf — same forced
            // first-login change as a brand new account, see AddUser above.
            user.MustChangePassword = true;
        }

        await db.SaveChangesAsync();
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            // Whoever knew the old password shouldn't stay signed in on it.
            await refreshTokens.RevokeAllForUserAsync(user.Id);
        }
        return Ok(ToDto(user, linkedStudent?.Id));
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

        if (user.Roles.Contains(UserRole.Admin) && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        // The Person (with its profiles and parent↔child links) goes with
        // the account — see Person.UserId.
        db.ParentStudents.RemoveRange(db.ParentStudents.Where(ps => ps.ParentProfile!.Person!.UserId == id));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Teacher-only data (TeacherProfile) — currently the aprobace.</summary>
    [HttpPut("{id:int}/teacher-profile")]
    public async Task<ActionResult<UserDto>> SetTeacherProfile(int id, SetTeacherProfileRequest request)
    {
        var user = await db.Users.Include(u => u.Person).ThenInclude(p => p!.TeacherProfile)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null || !user.Roles.Contains(UserRole.Teacher))
        {
            return NotFound();
        }

        var profile = await db.EnsureTeacherProfileAsync(user);
        profile.Qualification = string.IsNullOrWhiteSpace(request.Qualification) ? null : request.Qualification.Trim();
        await db.SaveChangesAsync();
        return Ok(ToDto(user, await db.Students.Where(s => s.UserId == id).Select(s => (int?)s.Id).FirstOrDefaultAsync()));
    }

    /// <summary>E-mails <paramref name="id"/> a one-time link to set their own
    /// password (<see cref="User.InvitationToken"/>), valid 7 days. Coexists
    /// with the admin-sets-a-password flow in <see cref="AddUser"/>/
    /// <see cref="UpdateUser"/> — this is an alternative the Admin can trigger
    /// any time the account has an e-mail on file, not a replacement for it.</summary>
    [HttpPost("{id:int}/invite")]
    public async Task<IActionResult> SendInvite(int id)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        var email = user.Person?.Email;
        if (string.IsNullOrWhiteSpace(email))
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
        var subject = $"Pozvánka do Soovioo — {school.Name}";
        var body = $"""
            <p>Dobrý den,</p>
            <p>byl(a) jste přihlášen(a) do školního systému Soovioo pro školu <strong>{school.Name}</strong>.
            Pro nastavení vlastního hesla a přihlášení klikněte na odkaz níže:</p>
            <p><a href="{link}">{link}</a></p>
            <p>Odkaz je platný 7 dní.</p>
            """;
        await emailSender.SendAsync(email, subject, body);

        return NoContent();
    }

    private static string BuildInviteLink(AppLinkOptions options, string schoolSlug, string token)
    {
        var origin = options.PathBasedTenant
            ? $"{options.FrontendBaseUrl!.TrimEnd('/')}/{schoolSlug}"
            : $"https://{schoolSlug}.{options.MainDomain}";
        return $"{origin}/accept-invite?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>Distinct, in enum order (Admin, Teacher, Parent, Student) so
    /// the first one is a sensible default active role. Student can't be
    /// combined — a pupil's account is never also staff or a parent.</summary>
    private (List<UserRole> Roles, ActionResult<UserDto>? Error) ParseRoles(IReadOnlyList<string>? names)
    {
        var roles = new List<UserRole>();
        foreach (var name in names ?? [])
        {
            if (!Enum.TryParse<UserRole>(name, out var parsed) || parsed == UserRole.SuperAdmin)
            {
                return ([], BadRequest("Neznámá role."));
            }
            roles.Add(parsed);
        }
        roles = roles.Distinct().Order().ToList();
        if (roles.Count == 0)
        {
            return ([], BadRequest("Vyberte aspoň jednu roli."));
        }
        if (roles.Contains(UserRole.Student) && roles.Count > 1)
        {
            return ([], BadRequest("Roli Student nelze kombinovat s jinou."));
        }
        return (roles, null);
    }

    private static IReadOnlyList<string> RoleNames(IEnumerable<UserRole> roles) =>
        roles.Order().Select(r => r.ToString()).ToList();

    private static UserDto ToDto(User user, int? studentId) =>
        new(user.Id, user.Username, user.Person?.FirstName ?? "", user.Person?.LastName ?? "",
            PersonNames.Display(user), user.Role.ToString(), RoleNames(user.Roles),
            user.Person?.Phone, user.Person?.Email, studentId, user.Person?.TeacherProfile?.Qualification);

    /// <summary>Writes name/contacts to the account's Person and creates the
    /// profile of every role it now has (profiles of removed roles stay, so
    /// re-adding a role restores e.g. a parent's children).</summary>
    private async Task ApplyPersonAsync(
        User user, List<UserRole> roles, string? firstName, string? lastName, string? phone, string? email)
    {
        var person = await db.EnsurePersonAsync(user);
        person.FirstName = firstName?.Trim() ?? "";
        person.LastName = string.IsNullOrWhiteSpace(lastName) ? person.LastName : lastName.Trim();
        person.Phone = phone;
        person.Email = email;
        if (roles.Contains(UserRole.Teacher))
        {
            await db.EnsureTeacherProfileAsync(user);
        }
        if (roles.Contains(UserRole.Parent))
        {
            await db.EnsureParentProfileAsync(user);
        }
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>True if <paramref name="userId"/> is the only Admin left for
    /// the current school — deleting/demoting them would leave the school
    /// with no one able to manage it.</summary>
    private async Task<bool> IsLastAdminAsync(int userId) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Roles.Contains(UserRole.Admin));

    /// <summary>Validates and resolves the <see cref="Student"/> a Student-role
    /// account must link to (null/error otherwise). Doesn't write anything —
    /// callers apply <c>Student.UserId</c> themselves once the rest of the
    /// request has also validated, so a half-applied link never survives
    /// an otherwise-rejected request. <paramref name="currentUserId"/> is the
    /// user being updated (null when creating), so re-saving a Student
    /// account with the same already-linked student isn't rejected as
    /// "already taken".</summary>
    private async Task<(Student? Student, ActionResult<UserDto>? Error)> ResolveStudentLinkAsync(
        List<UserRole> roles, int? requestedStudentId, int? currentUserId)
    {
        if (!roles.Contains(UserRole.Student))
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
            return (null, BadRequest("Neznámý žák."));
        }

        if (student.UserId is not null && student.UserId != currentUserId)
        {
            return (null, BadRequest("Tento žák už má uživatelský účet."));
        }

        return (student, null);
    }
}

/// <param name="LastName">Empty = keep the current one (a new account
/// falls back to the username).</param>
public record CreateUserRequest(
    string Username, string Password, IReadOnlyList<string> Roles, string? Phone = null, string? Email = null,
    int? StudentId = null, string? FirstName = null, string? LastName = null);

public record UpdateUserRequest(
    string Username, IReadOnlyList<string> Roles, string? Password = null, string? Phone = null, string? Email = null,
    int? StudentId = null, string? FirstName = null, string? LastName = null);

/// <summary><see cref="Role"/> is the active one, <see cref="Roles"/> all
/// the account may switch between. <see cref="StudentId"/> is set only for
/// role Student — the one <see cref="Evidence.Student"/> this account logs
/// in as.</summary>
public record SetTeacherProfileRequest(string? Qualification);

/// <param name="Name">Display name (first + last, or the username).</param>
/// <param name="Qualification">Teachers' aprobace (TeacherProfile).</param>
public record UserDto(
    int Id, string Username, string FirstName, string LastName, string Name, string Role, IReadOnlyList<string> Roles,
    string? Phone, string? Email, int? StudentId = null, string? Qualification = null);
