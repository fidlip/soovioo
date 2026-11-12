using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Common;
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
[AuditArea("Nastavení")]
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
        return Ok(await ToDtoAsync(user));
    }

    [HttpPut]
    [AuditAction("Upraven vlastní profil")]
    public async Task<ActionResult<MyProfileDto>> UpdateProfile(UpdateMyProfileRequest request)
    {
        var user = await CurrentUserAsync();
        var person = await db.EnsurePersonAsync(user);
        person.Phone = request.Phone;
        person.Email = request.Email;
        await db.SaveChangesAsync();
        return Ok(await ToDtoAsync(user));
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
    [AuditAction("Změněno vlastní heslo")]
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

        var (presented, cookie) = AuthSessionService.ReadRefreshToken(Request, request.RefreshToken, user.SchoolId);
        var fromCookie = cookie is not null;
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
    [AuditAction("Přepnuta aktivní role")]
    public async Task<ActionResult<SwitchRoleResponse>> SwitchRole(SwitchRoleRequest request)
    {
        var user = await CurrentUserAsync();
        if (user.Roles.FirstOrDefault(r => r.Id == request.RoleId) is not { } role)
        {
            return BadRequest("Tuto roli účet nemá.");
        }

        user.ActiveRole = role;
        await db.SaveChangesAsync();
        return Ok(new SwitchRoleResponse(await sessions.IssueAccessTokenAsync(user)));
    }

    /// <summary>A parent's own nickname applies right away, a pupil's waits
    /// for approval (<see cref="Nicknames"/>).</summary>
    [HttpPut("nickname")]
    [AuditAction("Změněna vlastní přezdívka")]
    public async Task<ActionResult<MyProfileDto>> SetNickname(SetNicknameRequest request, [FromServices] Nicknames nicknames)
    {
        var user = await CurrentUserAsync();
        var (value, error) = Nicknames.Normalize(request.Nickname);
        if (error is not null)
        {
            return this.FieldError("nickname", error);
        }
        if (user.ActiveRole?.Kind == RoleKind.Student && user.Person is not null
            && await db.Students.FirstOrDefaultAsync(s => s.PersonId == user.Person.Id) is { } student)
        {
            await nicknames.ProposeAsync(student, value);
        }
        else if (user.ActiveRole?.Kind == RoleKind.Parent)
        {
            var person = await db.EnsurePersonAsync(user);
            person.Nickname = value;
        }
        else
        {
            return BadRequest("Přezdívku si nastavují jen rodiče a žáci.");
        }
        await db.SaveChangesAsync();
        return Ok(await ToDtoAsync(user));
    }

    /// <summary>Stored only where it differs from the default, so the
    /// school's default keeps applying to everyone who didn't choose.</summary>
    [HttpPut("show-nicknames")]
    [AuditAction("Změněno zobrazování přezdívek žáků")]
    public async Task<ActionResult<MyProfileDto>> SetShowNicknames(SetShowNicknamesRequest request)
    {
        var user = await CurrentUserAsync();
        if (!ChoosesNicknames(user))
        {
            return BadRequest("Tato volba se vás netýká.");
        }
        user.ShowStudentNicknames = request.Show == await DefaultShowNicknamesAsync(user) ? null : request.Show;
        await db.SaveChangesAsync();
        return Ok(await ToDtoAsync(user));
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
    [AuditAction("Změněna viditelnost vlastních údajů")]
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
        if (user.HasKind(RoleKind.Student) && user.Person is not null)
        {
            var choices = await db.VisibilityChoices.Where(c => c.PersonId == user.Person.Id).ToListAsync();
            settings.AddRange(VisibilitySettings.Of(visibility, [VisibilityOwner.Student], choices, editable: false));
        }
        return settings;
    }

    private async Task<User> CurrentUserAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == userId);
    }

    /// <summary>Staff may always have a photo, parents where the school
    /// has parents' photos on (pupils' photos are per <see cref="Evidence.StudentProfile"/>,
    /// see <see cref="PhotosController"/>).</summary>
    private async Task<MyProfileDto> ToDtoAsync(User user)
    {
        var canUploadPhoto = PhotoAccess.IsStaff(user);
        if (!canUploadPhoto && user.HasKind(RoleKind.Parent) && user.SchoolId is { } schoolId)
        {
            canUploadPhoto = await db.Schools.AnyAsync(s =>
                s.Id == schoolId && s.ParentPhotos);
        }
        var kind = user.ActiveRole?.Kind;
        return new(user.Id, user.Username, PersonNames.Display(user), user.ActiveRole?.Name ?? nameof(UserRole.SuperAdmin), user.Person?.Phone, user.Person?.Email,
            PhotoStorage.Token(user.Person?.PhotoKey), canUploadPhoto,
            CanSetNickname: kind is RoleKind.Parent or RoleKind.Student,
            Nickname: user.Person?.Nickname,
            NicknameProposal: kind == RoleKind.Student ? user.Person?.NicknameProposal : null,
            ShowNicknames: ChoosesNicknames(user) ? user.ShowStudentNicknames ?? await DefaultShowNicknamesAsync(user) : null);
    }

    /// <summary>Teachers (not the school's management, who never see
    /// nicknames) and parents choose whether pupils' nicknames show.</summary>
    private static bool ChoosesNicknames(User user) =>
        user.ActiveRole is { } role
        && (role.Kind == RoleKind.Parent || (role.Kind == RoleKind.Staff && role.BuiltIn != UserRole.Admin));

    private async Task<bool> DefaultShowNicknamesAsync(User user) =>
        user.ActiveRole?.Kind != RoleKind.Staff
        || await db.Schools.Where(s => s.Id == user.SchoolId).Select(s => s.StaffSeeStudentNicknames).FirstOrDefaultAsync();
}

/// <param name="Photo">Token for the own photo's URL, null = no photo.</param>
/// <param name="CanUploadPhoto">Whether the account may have a photo at all.</param>
/// <param name="NicknameProposal">A pupil's own, waiting for approval.</param>
/// <param name="ShowNicknames">Whether pupils' nicknames show to the caller;
/// null = not their choice (pupils, the school's management).</param>
public record MyProfileDto(
    int Id, string Username, string Name, string Role, string? Phone, string? Email, string? Photo, bool CanUploadPhoto,
    bool CanSetNickname = false, string? Nickname = null, string? NicknameProposal = null, bool? ShowNicknames = null);

public record SetShowNicknamesRequest(bool Show);

public record SwitchRoleRequest(int RoleId);

public record SwitchRoleResponse(string Token);

public record UpdateMyProfileRequest(string? Phone, string? Email);

/// <param name="RefreshToken">Mobile clients only — the web app's refresh
/// token travels in the cookie.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <param name="RefreshToken">Null for cookie (web) callers.</param>
public record ChangePasswordResponse(string Token, string? RefreshToken = null);
