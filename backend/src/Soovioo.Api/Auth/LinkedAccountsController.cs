using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

/// <summary>Accounts of one person at several schools (<see cref="AccountLink"/>):
/// linking them by logging into the other one and switching between them with
/// a one-time code. A school sees neither — its log gets only the
/// login at <see cref="RedeemCode"/>.</summary>
[ApiController]
[AuditArea("Přihlášení")]
public class LinkedAccountsController(
    AppDbContext db, AuthSessionService sessions, RefreshTokenService refreshTokens, TimeProvider clock) : ControllerBase
{
    public const string BadCredentials = "Neplatné přihlašovací údaje.";
    public const string BadCode = "Přepnutí na druhý účet se nepovedlo. Přihlaste se prosím.";
    public const string BadRequestCode = "Žádost o propojení je neplatná nebo vypršela. Zkuste to znovu.";
    public const string SameSchool = "Propojit jde jen účty z různých škol.";
    public const int MinSearchLength = 3;
    public const int MaxSearchResults = 10;
    public static readonly TimeSpan CodeLength = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RequestLength = TimeSpan.FromMinutes(10);

    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>Schools to link with, by name or address, without diacritics;
    /// not the demos. Only for logged-in users, a few at a time — the list
    /// as a whole shouldn't be easy to get.</summary>
    [HttpGet("api/linked-accounts/schools")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LinkableSchoolDto>>> SearchSchools(string? search = null)
    {
        var term = TextSearch.Fold((search ?? "").Trim());
        if (term.Length < MinSearchLength)
        {
            return Ok(Array.Empty<LinkableSchoolDto>());
        }
        var mine = int.TryParse(User.FindFirstValue("schoolId"), out var id) ? id : (int?)null;
        return Ok(await db.Schools.IgnoreQueryFilters()
            .Where(s => !s.IsDemo && s.Id != mine)
            .Where(s => TextSearch.Fold(s.Name).Contains(term) || TextSearch.Fold(s.Slug).Contains(term))
            .OrderBy(s => s.Name)
            .Take(MaxSearchResults)
            .Select(s => new LinkableSchoolDto(s.Name, s.Slug))
            .ToListAsync());
    }

    /// <summary>Web, step 1 of linking: the other school's own login page
    /// takes it from here with the code. Nothing is linked until the same
    /// account comes back for <see cref="CompleteRequest"/>.</summary>
    [HttpPost("api/linked-accounts/requests")]
    [Authorize]
    [CrossSchoolAudit]
    [AuditAction("Zahájeno propojení účtů")]
    public async Task<ActionResult<LinkRequestDto>> StartRequest(StartLinkRequest request)
    {
        var me = await db.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (RefusalFor(me) is { } refusal)
        {
            return BadRequest(refusal);
        }
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == request.SchoolSlug.Trim());
        if (school is null || school.IsDemo)
        {
            return BadRequest("Takovou školu jsme nenašli.");
        }
        if (school.Id == me!.SchoolId)
        {
            return BadRequest(SameSchool);
        }

        var now = clock.GetUtcNow();
        await db.DeleteAsync(db.AccountLinkRequests.Where(r => r.InitiatorUserId == me.Id || r.ExpiresAt < now));
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.AccountLinkRequests.Add(new AccountLinkRequest
        {
            CodeHash = Hash(code),
            InitiatorUserId = me.Id,
            TargetSchoolId = school.Id,
            ExpiresAt = now + RequestLength,
        });
        await db.SaveChangesAsync();
        return Ok(new LinkRequestDto(code, school.Slug));
    }

    /// <summary>Web, step 2: whose request it is, for the other school's
    /// pages to say. Anonymous: the login page asks too.</summary>
    [HttpPost("api/linked-accounts/requests/preview")]
    [AllowAnonymous]
    [CrossSchoolAudit]
    [AuditAction("Zobrazena žádost o propojení účtů")]
    public async Task<ActionResult<LinkRequestPreviewDto>> PreviewRequest(LinkCodeRequest request)
    {
        if (await FindRequestAsync(request.Code) is not { } found)
        {
            return BadRequest(BadRequestCode);
        }
        var initiator = await db.Users.IgnoreQueryFilters().Include(u => u.School)
            .FirstAsync(u => u.Id == found.InitiatorUserId);
        return Ok(new LinkRequestPreviewDto(initiator.Username, initiator.School!.Name, initiator.School.Slug));
    }

    /// <summary>Web, step 3: the account logged in at the other school says
    /// yes. Links nothing yet: the new code goes only to this browser, so
    /// whoever sent someone their request can't finish it with the first
    /// code, and that someone's browser isn't logged in as them.</summary>
    [HttpPost("api/linked-accounts/requests/approve")]
    [Authorize]
    [CrossSchoolAudit]
    [AuditAction("Potvrzeno propojení účtů")]
    public async Task<ActionResult<LinkRequestDto>> ApproveRequest(LinkCodeRequest request)
    {
        if (await FindRequestAsync(request.Code) is not { } found)
        {
            return BadRequest(BadRequestCode);
        }
        var me = await db.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (RefusalFor(me) is { } refusal)
        {
            return BadRequest(refusal);
        }
        if (me!.SchoolId != found.TargetSchoolId)
        {
            return BadRequest(BadRequestCode);
        }
        var completion = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        found.ApprovedUserId = me.Id;
        found.CompletionCodeHash = Hash(completion);
        await db.SaveChangesAsync();
        var slug = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == found.InitiatorUserId)
            .Select(u => u.School!.Slug)
            .FirstAsync();
        return Ok(new LinkRequestDto(completion, slug));
    }

    /// <summary>Web, step 4, back at the first school: links the accounts,
    /// only for the account that started it.</summary>
    [HttpPost("api/linked-accounts/requests/complete")]
    [Authorize]
    [CrossSchoolAudit]
    [AuditAction("Dokončeno propojení účtů")]
    public async Task<IActionResult> CompleteRequest(LinkCodeRequest request)
    {
        var hash = Hash(request.Code);
        var found = await db.AccountLinkRequests.FirstOrDefaultAsync(r => r.CompletionCodeHash == hash);
        if (found is not { ApprovedUserId: { } approved } || found.ExpiresAt < clock.GetUtcNow()
            || found.InitiatorUserId != CurrentUserId())
        {
            return BadRequest(BadRequestCode);
        }
        db.AccountLinkRequests.Remove(found);
        await db.SaveChangesAsync();

        var me = await db.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (RefusalFor(me) is { } refusal)
        {
            return BadRequest(refusal);
        }
        var other = await db.Users.IgnoreQueryFilters().Include(u => u.School).FirstOrDefaultAsync(u => u.Id == approved);
        if (other is null || other.ErasedAt is not null)
        {
            return BadRequest(BadRequestCode);
        }
        return await LinkAsync(me!, other, other.School!);
    }

    /// <summary>Mobile: the app's own form logs into the other account right
    /// here — no other address to go to, no one else's code to be tricked
    /// with.</summary>
    [HttpPost("api/linked-accounts")]
    [Authorize]
    [CrossSchoolAudit]
    [AuditAction("Propojen účet z jiné školy")]
    public async Task<IActionResult> Link(LinkAccountRequest request)
    {
        var me = await db.Users.Include(u => u.School).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (RefusalFor(me) is { } refusal)
        {
            return BadRequest(refusal);
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
        return await LinkAsync(me!, other, school!);
    }

    /// <summary>Why this account can't link with anything; null when it can.</summary>
    private static string? RefusalFor(User? me)
    {
        if (me?.School is null || me.IsSuperAdmin || me.School.IsDemo)
        {
            return "Tento účet propojit nejde.";
        }
        return me.MustChangePassword ? "Nejdřív si nastavte vlastní heslo." : null;
    }

    /// <summary>The caller has proved the other account is theirs.</summary>
    private async Task<IActionResult> LinkAsync(User me, User other, School school)
    {
        if (other.DeactivatedAt is not null)
        {
            return BadRequest(AuthController.DeactivatedMessage);
        }
        if (other.SchoolId == me.SchoolId)
        {
            return BadRequest(SameSchool);
        }
        if (school.IsDemo)
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

    private async Task<AccountLinkRequest?> FindRequestAsync(string code)
    {
        var hash = Hash(code);
        var found = await db.AccountLinkRequests.FirstOrDefaultAsync(r => r.CodeHash == hash);
        return found is null || found.ExpiresAt < clock.GetUtcNow() ? null : found;
    }

    /// <summary>Takes a linked account out of the caller's accounts.</summary>
    [HttpDelete("api/linked-accounts/{userId:int}")]
    [Authorize]
    [CrossSchoolAudit]
    [AuditAction("Zrušeno propojení účtů")]
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
    [AuditAction("Přepnuto na propojený účet")]
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
    [AuditAction("Přihlášení")]
    public async Task<ActionResult<LoginResponse>> RedeemCode(RedeemLoginCodeRequest request)
    {
        var audit = AuditContext.Of(HttpContext);
        audit.Action = "Neúspěšné přihlášení";
        audit.Description = "Neúspěšné přihlášení z propojeného účtu";

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
        if (user is not null)
        {
            audit.UserId = user.Id;
            audit.SchoolId = user.SchoolId;
            audit.Username = user.Username;
        }
        if (expired || user is null || user.DeactivatedAt is not null || user.ErasedAt is not null)
        {
            return BadRequest(BadCode);
        }

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

public record LinkableSchoolDto(string Name, string SchoolSlug);

public record StartLinkRequest(string SchoolSlug);

public record LinkCodeRequest(string Code);

/// <param name="SchoolSlug">The school whose page goes on with the code.</param>
public record LinkRequestDto(string Code, string SchoolSlug);

/// <summary>The account asking to be linked.</summary>
public record LinkRequestPreviewDto(string Username, string SchoolName, string SchoolSlug);

public record AccountSwitchDto(string Code, string SchoolSlug);

/// <param name="UseCookie">Web app: the refresh token goes into the cookie
/// of this school's page, see <see cref="LoginRequest"/>.</param>
public record RedeemLoginCodeRequest(string Code, bool UseCookie = false);
