using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Self-service profile for the caller's own <see cref="User"/> row —
/// unlike <see cref="UsersController"/> (Admin-only, manages anyone in the
/// school), every logged-in role (including SuperAdmin) can read/update their
/// own contact info and password here.</summary>
[Authorize]
[AllowSuperAdmin] // the SuperAdmin's own profile and password
[ApiController]
[Route("api/users/me")]
public class MyProfileController(
    AppDbContext db,
    AuthSessionService sessions,
    RefreshTokenService refreshTokens) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<MyProfileDto>> GetProfile()
    {
        var user = await CurrentUserAsync();
        return Ok(ToDto(user));
    }

    [HttpPut]
    public async Task<ActionResult<MyProfileDto>> UpdateProfile(UpdateMyProfileRequest request)
    {
        var user = await CurrentUserAsync();
        var person = await db.EnsurePersonAsync(user);
        person.Phone = request.Phone;
        person.Email = request.Email;
        await db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    /// <summary>Returns a freshly signed token, not 204 — the caller may be
    /// clearing a <see cref="User.MustChangePassword"/> forced redirect, and
    /// the token they're still holding has the old (true) claim baked in.
    /// Re-login would need the new password repeated right after they just
    /// typed it once; reissuing here avoids that.
    ///
    /// A new password also signs the account out everywhere else: every
    /// refresh token is revoked and this caller alone gets a new session,
    /// keeping its "Zůstat přihlášen" choice (mobile: always persistent).</summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest("Nové heslo nesmí být prázdné.");
        }

        var user = await CurrentUserAsync();
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return BadRequest("Současné heslo nesouhlasí.");
        }

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();

        var (presented, fromCookie) = AuthSessionService.ReadRefreshToken(Request, request.RefreshToken);
        var persistent = presented is null || !fromCookie
            || (await refreshTokens.FindPersistentAsync(presented) ?? false);
        // Only a web caller already holding the cookie gets a new one; a
        // Bearer-only caller with nothing to present gets it in the body.
        await refreshTokens.RevokeAllForUserAsync(user.Id);
        var session = await sessions.StartAsync(HttpContext, user, persistent, useCookie: fromCookie);
        return Ok(new ChangePasswordResponse(session.Token, session.RefreshToken));
    }

    /// <summary>Makes another of the caller's <see cref="User.Roles"/> the
    /// active one and returns an access token carrying it. Persisted on the
    /// user, so it's also what the next login/refresh starts in.</summary>
    [HttpPost("active-role")]
    public async Task<ActionResult<SwitchRoleResponse>> SwitchRole(SwitchRoleRequest request)
    {
        var user = await CurrentUserAsync();
        if (!Enum.TryParse<UserRole>(request.Role, out var role) || !user.Roles.Contains(role))
        {
            return BadRequest("Tuto roli účet nemá.");
        }

        user.Role = role;
        await db.SaveChangesAsync();
        return Ok(new SwitchRoleResponse(await sessions.IssueAccessTokenAsync(user)));
    }

    /// <summary>What the caller may hide of their own data (the school's
    /// "Viditelnost údajů" shares it and allows opting out), and whether
    /// they did — for the account itself and for the pupils whose entry in
    /// the class list they decide (own record, or children).</summary>
    [HttpGet("privacy")]
    public async Task<ActionResult<PrivacyDto>> GetPrivacy()
    {
        var user = await CurrentUserAsync();
        return Ok(await PrivacyAsync(user));
    }

    /// <summary>Only what <see cref="GetPrivacy"/> offers can be changed —
    /// hiding is the individual's call, sharing beyond the school's settings
    /// isn't.</summary>
    [HttpPut("privacy")]
    public async Task<ActionResult<PrivacyDto>> UpdatePrivacy(PrivacyDto request)
    {
        var user = await CurrentUserAsync();
        var school = user.SchoolId is { } schoolId ? await db.Schools.FindAsync(schoolId) : null;
        if (school is null)
        {
            return BadRequest("Účet nepatří žádné škole.");
        }

        var decidable = DecidableOptions(user, school);
        var hidden = Visibility.FromNames(request.Options.Where(o => o.Hidden).Select(o => o.Option));
        var touched = Visibility.FromNames(request.Options.Select(o => o.Option));
        if (hidden is null || touched is null || (touched.Value & ~decidable) != VisibilityOption.None)
        {
            return BadRequest("Tento údaj si skrýt nemůžete.");
        }

        var students = await DecidableStudentsAsync(user, school);
        if (request.Students.Any(s => !students.Any(own => own.Id == s.StudentId)))
        {
            return BadRequest("Tento údaj si skrýt nemůžete.");
        }

        if (touched.Value != VisibilityOption.None)
        {
            var person = await db.EnsurePersonAsync(user);
            person.HiddenData = (person.HiddenData & ~touched.Value) | hidden.Value;
        }
        foreach (var update in request.Students)
        {
            students.First(s => s.Id == update.StudentId).HiddenFromClassmates = update.Hidden;
        }
        await db.SaveChangesAsync();

        return Ok(await PrivacyAsync(user));
    }

    private async Task<PrivacyDto> PrivacyAsync(User user)
    {
        var school = user.SchoolId is { } schoolId ? await db.Schools.FindAsync(schoolId) : null;
        if (school is null)
        {
            return new PrivacyDto([], []);
        }

        var hidden = user.Person?.HiddenData ?? VisibilityOption.None;
        var options = Visibility.ToNames(DecidableOptions(user, school))
            .Select(name => new PrivacyOptionDto(name, hidden.HasFlag(Enum.Parse<VisibilityOption>(name))))
            .ToList();
        var students = (await DecidableStudentsAsync(user, school))
            .Select(s => new StudentPrivacyDto(s.Id, $"{s.FirstName} {s.LastName}", s.HiddenFromClassmates))
            .ToList();
        return new PrivacyDto(options, students);
    }

    private static VisibilityOption DecidableOptions(User user, Evidence.School school) =>
        Visibility.OwnDataOptions(user.Roles) & school.SharedData & school.OptOutAllowed;

    /// <summary>The pupil's own record, and a parent's children — tracked,
    /// so <see cref="UpdatePrivacy"/> can flip them.</summary>
    private async Task<List<Evidence.Student>> DecidableStudentsAsync(User user, Evidence.School school)
    {
        if (!Visibility.AllowsClassmatesOptOut(school))
        {
            return [];
        }
        var childIds = user.Roles.Contains(UserRole.Parent) ? await db.ChildIdsOf(user.Id).ToListAsync() : [];
        var isStudent = user.Roles.Contains(UserRole.Student);
        return await db.Students
            .Where(s => childIds.Contains(s.Id) || (isStudent && s.UserId == user.Id))
            .OrderBy(s => s.FirstName)
            .ToListAsync();
    }

    private async Task<User> CurrentUserAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == userId);
    }

    private static MyProfileDto ToDto(User user) =>
        new(user.Username, PersonNames.Display(user), user.Role.ToString(), user.Person?.Phone, user.Person?.Email);
}

public record MyProfileDto(string Username, string Name, string Role, string? Phone, string? Email);

public record SwitchRoleRequest(string Role);

/// <param name="Options">The caller's own data they may hide (<see cref="VisibilityOption"/> names).</param>
/// <param name="Students">Pupils whose entry in the class list the caller decides.</param>
public record PrivacyDto(IReadOnlyList<PrivacyOptionDto> Options, IReadOnlyList<StudentPrivacyDto> Students);

public record PrivacyOptionDto(string Option, bool Hidden);

/// <param name="Name">Ignored on update.</param>
public record StudentPrivacyDto(int StudentId, string Name, bool Hidden);

public record SwitchRoleResponse(string Token);

public record UpdateMyProfileRequest(string? Phone, string? Email);

/// <param name="RefreshToken">Mobile clients only — the web app's refresh
/// token travels in the cookie.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <param name="RefreshToken">Null for cookie (web) callers.</param>
public record ChangePasswordResponse(string Token, string? RefreshToken = null);
