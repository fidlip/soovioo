using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>Accounts of one person at several schools (<see cref="AccountLink"/>):
/// linking them with the other account's credentials and switching between
/// them with a one-time code. A school sees neither — its log gets only the
/// login at <see cref="RedeemCode"/>.</summary>
[ApiController]
public class LinkedAccountsController(
    AppDbContext db, AuthSessionService sessions, RefreshTokenService refreshTokens, TimeProvider clock) : ControllerBase
{
    public const string BadCredentials = "Neplatné přihlašovací údaje.";
    public const string BadCode = "Přepnutí na druhý účet se nepovedlo. Přihlaste se prosím.";
    public static readonly TimeSpan CodeLength = TimeSpan.FromMinutes(1);

    private static readonly PasswordHasher<User> Hasher = new();

    [HttpPost("api/linked-accounts")]
    [Authorize]
    [CrossSchoolAudit]
    public async Task<IActionResult> Link(LinkAccountRequest request)
    {
        var me = await db.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (me?.School is null || me.IsSuperAdmin || me.School.IsDemo)
        {
            return BadRequest("Tento účet propojit nejde.");
        }
        if (me.MustChangePassword)
        {
            return BadRequest("Nejdřív si nastavte vlastní heslo.");
        }

        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == request.SchoolSlug.Trim());
        var other = school is null
            ? null
            : await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Username == request.Username && u.SchoolId == school.Id);
        if (other is null || other.ErasedAt is not null
            || Hasher.VerifyHashedPassword(other, other.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return BadRequest(BadCredentials);
        }

        // After the password, so it doesn't tell strangers the account exists.
        if (other.DeactivatedAt is not null)
        {
            return BadRequest(AuthController.DeactivatedMessage);
        }
        if (other.SchoolId == me.SchoolId)
        {
            return BadRequest("Propojit jde jen účty z různých škol.");
        }
        if (school!.IsDemo)
        {
            return BadRequest("Účet v ukázkové škole propojit nejde.");
        }
        if (other.MustChangePassword)
        {
            return BadRequest("U druhého účtu si nejdřív nastavte vlastní heslo.");
        }

        var links = await db.AccountLinks.IgnoreQueryFilters()
            .Where(l => l.UserId == me.Id || l.UserId == other.Id)
            .ToListAsync();
        var myGroup = links.FirstOrDefault(l => l.UserId == me.Id)?.GroupId;
        var otherGroup = links.FirstOrDefault(l => l.UserId == other.Id)?.GroupId;
        var joined = await db.AccountLinks.IgnoreQueryFilters()
            .Where(l => l.GroupId == myGroup || l.GroupId == otherGroup)
            .ToListAsync();
        var accounts = joined.Select(l => l.UserId).Union([me.Id, other.Id]).ToList();
        var schools = await db.Users.IgnoreQueryFilters()
            .Where(u => accounts.Contains(u.Id))
            .Select(u => u.SchoolId)
            .ToListAsync();
        if (schools.Count != schools.Distinct().Count())
        {
            return BadRequest("V propojených účtech může být z každé školy jen jeden.");
        }

        var group = myGroup ?? otherGroup ?? Guid.NewGuid();
        foreach (var link in joined)
        {
            link.GroupId = group;
        }
        var now = clock.GetUtcNow();
        foreach (var id in new[] { me.Id, other.Id })
        {
            if (joined.All(l => l.UserId != id))
            {
                db.AccountLinks.Add(new AccountLink { UserId = id, GroupId = group, LinkedAt = now });
            }
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Takes a linked account out of the caller's accounts.</summary>
    [HttpDelete("api/linked-accounts/{userId:int}")]
    [Authorize]
    [CrossSchoolAudit]
    public async Task<IActionResult> Unlink(int userId)
    {
        if (!await IsLinkedToMeAsync(userId))
        {
            return NotFound();
        }
        await db.UnlinkAccountAsync(userId);
        return NoContent();
    }

    /// <summary>A one-time code that logs into a linked account — the web
    /// page carries it to that school's own address, redeemed at
    /// <see cref="RedeemCode"/>. The caller's session stays.</summary>
    [HttpPost("api/linked-accounts/{userId:int}/switch")]
    [Authorize]
    [CrossSchoolAudit]
    public async Task<ActionResult<AccountSwitchDto>> Switch(int userId)
    {
        if (!await IsLinkedToMeAsync(userId))
        {
            return NotFound();
        }
        var link = await db.AccountLinks.IgnoreQueryFilters()
            .Include(l => l.User).ThenInclude(u => u!.School)
            .FirstAsync(l => l.UserId == userId);

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        link.SwitchCodeHash = Hash(code);
        link.SwitchCodeExpiresAt = clock.GetUtcNow() + CodeLength;
        link.SwitchPersistent = await IsPersistentSessionAsync();
        await db.SaveChangesAsync();
        return Ok(new AccountSwitchDto(code, link.User!.School!.Slug));
    }

    /// <summary>Anonymous: on the other school's address there's no session
    /// yet. Its school sees an ordinary login.</summary>
    [HttpPost("api/auth/login-code")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> RedeemCode(RedeemLoginCodeRequest request)
    {
        var hash = Hash(request.Code);
        var link = await db.AccountLinks.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.SwitchCodeHash == hash);
        if (link is null)
        {
            return BadRequest(BadCode);
        }
        var expired = link.SwitchCodeExpiresAt < clock.GetUtcNow();
        link.SwitchCodeHash = null;
        link.SwitchCodeExpiresAt = null;
        await db.SaveChangesAsync();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == link.UserId);
        if (expired || user is null || user.DeactivatedAt is not null || user.ErasedAt is not null)
        {
            return BadRequest(BadCode);
        }

        var audit = AuditContext.Of(HttpContext);
        audit.UserId = user.Id;
        audit.SchoolId = user.SchoolId;
        audit.Username = user.Username;
        audit.Action = "Přihlášení";
        audit.Description = "Přihlášení";

        var persistent = !request.UseCookie || link.SwitchPersistent;
        return Ok(await sessions.StartAsync(HttpContext, user, persistent, request.UseCookie));
    }

    private async Task<bool> IsLinkedToMeAsync(int userId)
    {
        var me = CurrentUserId();
        return userId != me && (await db.LinkedWithAsync([me]))[me].Contains(userId);
    }

    /// <summary>A web session continues the way it was logged in; the mobile
    /// app has no cookie and stays signed in anyway.</summary>
    private async Task<bool> IsPersistentSessionAsync()
    {
        var schoolId = int.TryParse(User.FindFirstValue("schoolId"), out var id) ? id : (int?)null;
        var (token, _) = AuthSessionService.ReadRefreshToken(Request, null, schoolId);
        return token is not null && await refreshTokens.FindPersistentAsync(token) == true;
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public record LinkAccountRequest(string SchoolSlug, string Username, string Password);

public record AccountSwitchDto(string Code, string SchoolSlug);

/// <param name="UseCookie">Web app: the refresh token goes into the cookie
/// of this school's page, see <see cref="LoginRequest"/>.</param>
public record RedeemLoginCodeRequest(string Code, bool UseCookie = false);
