using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
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

[Authorize]
[RequirePermission(Permission.Administration)]
[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    ICurrentSchoolContext currentSchool,
    IEmailSender emailSender,
    IOptions<AppLinkOptions> appLinkOptions,
    RefreshTokenService refreshTokens,
    ILogger<UsersController> logger) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers([FromServices] PhotoAccess? photoAccess = null)
    {
        var users = await db.Users
            .Where(u => u.ErasedAt == null)
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                User = u,
                u.Person,
                u.Person!.TeacherProfile,
                StudentId = db.Students.Where(s => s.Person!.UserId == u.Id).Select(s => (int?)s.Id).FirstOrDefault(),
            })
            .ToListAsync();

        var photos = photoAccess is null ? [] : await photoAccess.VisibleUserPhotosAsync(User, users.Select(u => u.User));
        return Ok(users.Select(u => ToDto(u.User, u.StudentId) with { Photo = photos.GetValueOrDefault(u.User.Id) }));
    }

    /// <summary>With an e-mail the invitation (see <see cref="SendInvite"/>)
    /// goes out right away and the password is optional — an account without
    /// one can't log in until its owner accepts the invite. The outcome is in
    /// <see cref="UserDto.Invite"/>; a failed invite doesn't undo the account.</summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> AddUser(CreateUserRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return this.FieldError("username", "Uživatelské jméno už existuje.");
        }

        var (roles, rolesError) = await ParseRolesAsync(request.RoleIds);
        if (rolesError is not null)
        {
            return rolesError;
        }

        var (person, personError) = await ResolvePersonAsync(roles, request.PersonId, currentUserId: null);
        if (personError is not null)
        {
            return personError;
        }

        var hasEmail = !string.IsNullOrWhiteSpace(person!.Email);
        var hasPassword = !string.IsNullOrWhiteSpace(request.Password);
        if (!hasEmail && !hasPassword)
        {
            return this.FieldError("password", "Osoba nemá e-mail na pozvánku — heslo je povinné.");
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = string.Empty,
            Roles = roles,
            ActiveRole = roles[0],
            // Admin only ever manages their own school — never trust a
            // client-supplied SchoolId here.
            SchoolId = currentSchool.SchoolId,
            // Admin picked this password, not the account's owner — force a
            // change on first login (see User.MustChangePassword).
            MustChangePassword = hasPassword,
        };
        // No password: the empty hash never verifies, so the invite is the
        // only way in.
        if (hasPassword)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password!);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        person.UserId = user.Id;
        await db.SaveChangesAsync();

        var invite = hasEmail ? await TrySendInviteAsync(user) : (InviteOutcome?)null;
        var dto = ToDto(user, person.Student?.Id) with { Invite = invite?.ToString() };
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

        var (roles, rolesError) = await ParseRolesAsync(request.RoleIds);
        if (rolesError is not null)
        {
            return rolesError;
        }

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var losesAdmin = user.HasBuiltIn(UserRole.Admin) && roles.All(r => r.BuiltIn != UserRole.Admin);
        if (id == currentUserId && losesAdmin)
        {
            return BadRequest("Nelze si odebrat roli Admin — vyřízli byste se z administrace.");
        }

        if (losesAdmin && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        var (person, personError) = await ResolvePersonAsync(roles, request.PersonId, currentUserId: id);
        if (personError is not null)
        {
            return personError;
        }
        // The Person the account was on stays as a record of its own.
        if (user.Person is { } previous && previous.Id != person!.Id)
        {
            previous.UserId = null;
        }
        person!.UserId = id;

        user.Username = request.Username;
        // Keep the active role when it survived the edit, otherwise fall
        // back to the first remaining one (ParseRolesAsync sorts them).
        var activeRole = roles.FirstOrDefault(r => r.Id == user.ActiveRoleId) ?? roles[0];
        user.Roles.Clear();
        user.Roles.AddRange(roles);
        user.ActiveRole = activeRole;
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
        return Ok(ToDto(user, person.Student?.Id));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == currentUserId)
        {
            return BadRequest("Nelze smazat vlastní účet.");
        }

        if (user.HasBuiltIn(UserRole.Admin) && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        // The Person stays, as a record of its own (Osoby).
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Someone who left (a member of staff, typically): the account
    /// stops working, the data stays until the school's retention period
    /// for staff runs out (Administrace → Archiv a výmaz údajů).</summary>
    [HttpPost("{id:int}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(int id, [FromServices] Retention.AccountEraser eraser)
    {
        var user = await db.Users.Include(u => u.Person).ThenInclude(p => p!.TeacherProfile)
            .FirstOrDefaultAsync(u => u.Id == id && u.ErasedAt == null);
        if (user is null)
        {
            return NotFound();
        }
        if (id == int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!))
        {
            return BadRequest("Nelze deaktivovat vlastní účet.");
        }
        if (user.HasBuiltIn(UserRole.Admin) && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }
        await eraser.DeactivateAsync(user);
        await db.SaveChangesAsync();
        return Ok(ToDto(user, await db.Students.Where(s => s.Person!.UserId == id).Select(s => (int?)s.Id).FirstOrDefaultAsync()));
    }

    [HttpPost("{id:int}/activate")]
    public async Task<ActionResult<UserDto>> Activate(int id, [FromServices] Retention.AccountEraser eraser)
    {
        var user = await db.Users.Include(u => u.Person).ThenInclude(p => p!.TeacherProfile)
            .FirstOrDefaultAsync(u => u.Id == id && u.ErasedAt == null);
        if (user is null)
        {
            return NotFound();
        }
        eraser.Reactivate(user);
        await db.SaveChangesAsync();
        return Ok(ToDto(user, await db.Students.Where(s => s.Person!.UserId == id).Select(s => (int?)s.Id).FirstOrDefaultAsync()));
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

        if (string.IsNullOrWhiteSpace(user.Person?.Email))
        {
            return BadRequest("Uživatel nemá vyplněný e-mail.");
        }

        if (!InvitesConfigured(appLinkOptions.Value))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "E-mailové pozvánky nejsou v tomhle prostředí nakonfigurované.");
        }

        await SendInviteEmailAsync(user);
        return NoContent();
    }

    /// <summary>The automatic invite of <see cref="AddUser"/> — like
    /// <see cref="SendInvite"/>, but reports a missing configuration or a
    /// failed send instead of failing the request.</summary>
    private async Task<InviteOutcome> TrySendInviteAsync(User user)
    {
        if (!InvitesConfigured(appLinkOptions.Value))
        {
            return InviteOutcome.NotConfigured;
        }

        try
        {
            await SendInviteEmailAsync(user);
            return InviteOutcome.Sent;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Automatic invite for new user {UserId} wasn't sent.", user.Id);
            return InviteOutcome.Failed;
        }
    }

    private static bool InvitesConfigured(AppLinkOptions options) =>
        options.PathBasedTenant
            ? !string.IsNullOrWhiteSpace(options.FrontendBaseUrl)
            : !string.IsNullOrWhiteSpace(options.MainDomain);

    /// <summary>New 7-day <see cref="User.InvitationToken"/> + the e-mail
    /// with its link. Caller checks the e-mail and configuration first.</summary>
    private async Task SendInviteEmailAsync(User user)
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId);

        user.InvitationToken = GenerateToken();
        user.InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await db.SaveChangesAsync();

        var link = BuildInviteLink(appLinkOptions.Value, school.Slug, user.InvitationToken);
        var subject = $"Pozvánka do Soovioo — {school.Name}";
        var body = $"""
            <p>Dobrý den,</p>
            <p>byl(a) jste přihlášen(a) do školního systému Soovioo pro školu <strong>{school.Name}</strong>.
            Pro nastavení vlastního hesla a přihlášení klikněte na odkaz níže:</p>
            <p><a href="{link}">{link}</a></p>
            <p>Odkaz je platný 7 dní.</p>
            """;
        await emailSender.SendAsync(user.Person!.Email!, subject, body);
    }

    private static string BuildInviteLink(AppLinkOptions options, string schoolSlug, string token)
    {
        return $"{options.SchoolOrigin(schoolSlug)}/accept-invite?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>The school's roles, built-in ones first (Admin, Teacher,
    /// Parent, Student) so the first one is a sensible default active role.
    /// A pupil's role can't be combined — a pupil's account is never also
    /// staff or a parent.</summary>
    private async Task<(List<Role> Roles, ActionResult<UserDto>? Error)> ParseRolesAsync(IReadOnlyList<int>? ids)
    {
        var wanted = (ids ?? []).Distinct().ToList();
        var roles = await db.Roles.Where(r => wanted.Contains(r.Id)).ToListAsync();
        if (roles.Count != wanted.Count)
        {
            return ([], BadRequest("Neznámá role."));
        }
        roles = roles.OrderBy(r => r.BuiltIn is null).ThenBy(r => r.BuiltIn).ThenBy(r => r.Name).ToList();
        if (roles.Count == 0)
        {
            return ([], BadRequest("Vyberte aspoň jednu roli."));
        }
        if (roles.Any(r => r.Kind == RoleKind.Student) && roles.Count > 1)
        {
            return ([], BadRequest(StudentTerm.Localize(
                "Roli žáka nelze kombinovat s jinou.", await SchoolTypeAsync())));
        }
        return (roles, null);
    }

    private async Task<SchoolType> SchoolTypeAsync() =>
        await db.Schools.Where(s => s.Id == currentSchool.SchoolId).Select(s => s.Type).FirstOrDefaultAsync();

    private static UserDto ToDto(User user, int? studentId) =>
        new(user.Id, user.Username, user.Person?.FirstName ?? "", user.Person?.LastName ?? "",
            PersonNames.Display(user), user.ActiveRoleId, RoleRefDto.ListOf(user.Roles),
            user.Person?.Phone, user.Person?.Email, studentId, user.Person?.TeacherProfile?.Qualification,
            DeactivatedAt: user.DeactivatedAt, PersonId: user.Person?.Id, LeftAt: user.LeftAt);

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>True if <paramref name="userId"/> is the only Admin left for
    /// the current school — deleting/demoting them would leave the school
    /// with no one able to manage it.</summary>
    private async Task<bool> IsLastAdminAsync(int userId) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Roles.Any(r => r.BuiltIn == UserRole.Admin) && u.DeactivatedAt == null);

    /// <summary>The Person the account is to be: in the school, without
    /// another account, with the profile each role needs — the built-in
    /// Teacher a teacher's, a parent's role a parent's, a pupil's role a
    /// pupil's (other staff roles none). Doesn't
    /// write anything. <paramref name="currentUserId"/> is the account being
    /// edited (null when creating).</summary>
    private async Task<(Person? Person, ActionResult<UserDto>? Error)> ResolvePersonAsync(
        List<Role> roles, int? personId, int? currentUserId)
    {
        if (personId is not { } id)
        {
            return (null, this.FieldError("personId", "Vyberte osobu, jejíž je účet."));
        }
        var person = await db.Persons
            .Include(p => p.TeacherProfile)
            .Include(p => p.ParentProfile)
            .Include(p => p.Student)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
        {
            return (null, this.FieldError("personId", "Neznámá osoba."));
        }
        if (person.UserId is not null && person.UserId != currentUserId)
        {
            return (null, this.FieldError("personId", "Tato osoba už má uživatelský účet."));
        }
        var missing = new List<string>();
        if (roles.Any(r => r.BuiltIn == UserRole.Teacher) && person.TeacherProfile is null)
        {
            missing.Add("učitele");
        }
        if (roles.Any(r => r.Kind == RoleKind.Parent) && person.ParentProfile is null)
        {
            missing.Add("rodiče");
        }
        if (roles.Any(r => r.Kind == RoleKind.Student) && person.Student is null)
        {
            missing.Add("žáka");
        }
        if (missing.Count > 0)
        {
            return (null, this.FieldError("personId", $"Osoba nemá profil {string.Join(", ", missing)} — doplňte ho."));
        }
        return (person, null);
    }
}

/// <param name="PersonId">Whose account it is (Administrace → Osoby).</param>
/// <param name="Password">Optional when the person has an e-mail — the
/// account is then usable only through the invite.</param>
public record CreateUserRequest(string Username, string? Password, IReadOnlyList<int> RoleIds, int? PersonId);

public record UpdateUserRequest(string Username, IReadOnlyList<int> RoleIds, int? PersonId, string? Password = null);

/// <summary><see cref="Role"/> is the active one, <see cref="Roles"/> all
/// the account may switch between; name and contacts are its Person's.</summary>
/// <param name="Name">Display name (first + last, or the username).</param>
/// <param name="StudentId">Only for role Student — the pupil this account logs in as.</param>
/// <param name="Qualification">Teachers' aprobace (TeacherProfile).</param>
/// <param name="Invite">Only in <see cref="UsersController.AddUser"/>'s
/// response, when the new account has an e-mail: an <see cref="InviteOutcome"/>.</param>
public record UserDto(
    int Id, string Username, string FirstName, string LastName, string Name, int? ActiveRoleId, IReadOnlyList<RoleRefDto> Roles,
    string? Phone, string? Email, int? StudentId = null, string? Qualification = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Invite = null,
    string? Photo = null,
    DateTimeOffset? DeactivatedAt = null,
    int? PersonId = null,
    DateOnly? LeftAt = null);

public enum InviteOutcome
{
    Sent,
    /// <summary>No App link configuration (the 503 of SendInvite).</summary>
    NotConfigured,
    /// <summary>The e-mail couldn't be sent (SMTP missing or down).</summary>
    Failed,
}
