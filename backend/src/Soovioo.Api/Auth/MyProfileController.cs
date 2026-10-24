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

    /// <summary>Who sees the caller's own data ("Viditelnost údajů") —
    /// as staff and/or parent. A pupil gets their own settings read-only,
    /// those are their parents' call (Mé děti).</summary>
    [HttpGet("visibility")]
    public async Task<ActionResult<IReadOnlyList<VisibilitySettingDto>>> GetVisibility()
    {
        var user = await CurrentUserAsync();
        return Ok(await VisibilityAsync(user));
    }

    [HttpPut("visibility")]
    public async Task<ActionResult<IReadOnlyList<VisibilitySettingDto>>> SetVisibility(SetVisibilityRequest request)
    {
        var user = await CurrentUserAsync();
        if (user.SchoolId is not { } schoolId)
        {
            return BadRequest("Účet nepatří žádné škole.");
        }

        var person = await db.EnsurePersonAsync(user);
        var choices = person.Id == 0 ? [] : await db.VisibilityChoices.Where(c => c.PersonId == person.Id).ToListAsync();
        var error = VisibilitySettings.Choose(
            db, await Visibility.LoadAsync(db, schoolId), Visibility.OwnOwners(user.Roles), choices, request,
            () => new VisibilityChoice { SchoolId = schoolId, Person = person });
        if (error is not null)
        {
            return BadRequest(error);
        }
        await db.SaveChangesAsync();

        return Ok(await VisibilityAsync(user));
    }

    private async Task<IReadOnlyList<VisibilitySettingDto>> VisibilityAsync(User user)
    {
        if (user.SchoolId is not { } schoolId)
        {
            return [];
        }
        var visibility = await Visibility.LoadAsync(db, schoolId);
        var settings = new List<VisibilitySettingDto>();

        var owners = Visibility.OwnOwners(user.Roles);
        if (owners.Count > 0)
        {
            var choices = user.Person is null
                ? []
                : await db.VisibilityChoices.Where(c => c.PersonId == user.Person.Id).ToListAsync();
            settings.AddRange(VisibilitySettings.Of(visibility, owners, choices, editable: true));
        }
        if (user.Roles.Contains(UserRole.Student)
            && await db.Students.FirstOrDefaultAsync(s => s.UserId == user.Id) is { } student)
        {
            var choices = await db.VisibilityChoices.Where(c => c.StudentId == student.Id).ToListAsync();
            settings.AddRange(VisibilitySettings.Of(visibility, [VisibilityOwner.Student], choices, editable: false));
        }
        return settings;
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

public record SwitchRoleResponse(string Token);

public record UpdateMyProfileRequest(string? Phone, string? Email);

/// <param name="RefreshToken">Mobile clients only — the web app's refresh
/// token travels in the cookie.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <param name="RefreshToken">Null for cookie (web) callers.</param>
public record ChangePasswordResponse(string Token, string? RefreshToken = null);
