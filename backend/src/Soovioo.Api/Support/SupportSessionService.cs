using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>Support mode across origins. The SuperAdmin page gets a one-time
/// code; the school's page (same app, possibly another subdomain) redeems it
/// for a support access token plus a <c>soovioo_support</c> cookie, which
/// <see cref="AuthController.Refresh"/> honours for that school only — so a
/// reload stays in support mode and the SuperAdmin's own session
/// (<c>soovioo_refresh</c>) is never touched.</summary>
public class SupportSessionService(
    AppDbContext db, JwtTokenFactory tokens, IOptions<AuthCookieOptions> cookieOptions, TimeProvider clock)
{
    public const string CookieName = "soovioo_support";
    private const string CookiePath = "/api";
    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(2);
    public static readonly TimeSpan HandoffLength = TimeSpan.FromMinutes(1);

    /// <summary>Starts the session; returns the one-time code (only its hash is stored).</summary>
    public async Task<string> StartAsync(SupportSession session)
    {
        var now = clock.GetUtcNow();
        var code = NewSecret();
        session.StartedAt = now;
        session.ExpiresAt = now + SessionLength;
        session.HandoffCodeHash = Hash(code);
        session.HandoffExpiresAt = now + HandoffLength;
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return code;
    }

    /// <summary>Code → support login (token + cookie), or null when the code
    /// is unknown, used, expired or the session is over.</summary>
    public async Task<LoginResponse?> RedeemAsync(HttpContext http, string code)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(code);
        var session = await db.SupportSessions.IgnoreQueryFilters()
            .Include(s => s.School).Include(s => s.SuperAdminUser)
            .FirstOrDefaultAsync(s => s.HandoffCodeHash == hash);
        if (session is null || session.HandoffExpiresAt < now || session.EndedAt is not null
            || session.ExpiresAt < now || session.SuperAdminUser is null || session.School is null)
        {
            return null;
        }

        // Anonymous request — the audit log learns whose entry it is here.
        var audit = AuditContext.Of(http);
        audit.SchoolId = session.SchoolId;
        audit.UserId = session.SuperAdminUserId;
        audit.SupportSessionId = session.Id;
        audit.SupportBy = session.SuperAdminUsername;

        var secret = NewSecret();
        session.HandoffCodeHash = null; // one use only
        session.HandoffExpiresAt = null;
        session.RefreshSecretHash = Hash(secret);
        await db.SaveChangesAsync();

        http.Response.Cookies.Append(CookieName, secret, CookieOptionsFor(session.ExpiresAt));
        return await RespondAsync(session);
    }

    /// <summary>A fresh support token when the request carries a live
    /// support cookie for <paramref name="schoolSlug"/>; null otherwise (the
    /// caller then falls back to the normal session). A page in support
    /// mode names its session (<paramref name="sessionId"/>) — the cookie
    /// of another one, entered since in the same browser, doesn't continue it.</summary>
    public async Task<LoginResponse?> RefreshAsync(HttpRequest request, string? schoolSlug, int? sessionId = null)
    {
        if (string.IsNullOrEmpty(schoolSlug)
            || !request.Cookies.TryGetValue(CookieName, out var secret) || string.IsNullOrEmpty(secret)
            || !request.Headers.ContainsKey(AuthSessionService.CsrfHeaderName))
        {
            return null;
        }
        var hash = Hash(secret);
        var now = clock.GetUtcNow();
        var session = await db.SupportSessions.IgnoreQueryFilters()
            .Include(s => s.School).Include(s => s.SuperAdminUser)
            .FirstOrDefaultAsync(s => s.RefreshSecretHash == hash);
        return session is { EndedAt: null, SuperAdminUser: not null, School: not null }
               && session.ExpiresAt > now && session.School.Slug == schoolSlug
               && (sessionId is null || session.Id == sessionId)
            ? await RespondAsync(session)
            : null;
    }

    /// <summary>A support cookie came along — on a school's page, its
    /// support session was there, whether it still runs or not.</summary>
    public static bool CarriesCookie(HttpRequest request) =>
        request.Cookies.TryGetValue(CookieName, out var secret) && !string.IsNullOrEmpty(secret);

    public void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, CookieOptionsFor(null));

    /// <summary>A new token of the session as it is now — after switching
    /// whom it is in the school as (<see cref="SetAsAsync"/>).</summary>
    public async Task<LoginResponse?> TokenAsync(int sessionId)
    {
        var session = await db.SupportSessions.IgnoreQueryFilters()
            .Include(s => s.School).Include(s => s.SuperAdminUser)
            .FirstAsync(s => s.Id == sessionId);
        return await RespondAsync(session);
    }

    /// <summary>Makes the session be in the school as <paramref name="userId"/>
    /// in <paramref name="roleId"/> (none: the role they're in, else their
    /// first one) — or as the school's admin without a user. Doesn't save;
    /// the error text when it isn't allowed.</summary>
    public async Task<string?> SetAsAsync(SupportSession session, int? userId, int? roleId)
    {
        if (userId is null)
        {
            if (!SupportAsPolicy.AllowsAdmin(await SchoolOfAsync(session)))
            {
                return SupportAsPolicy.AdminRefused;
            }
            session.AsUserId = null;
            session.AsRoleId = null;
            session.AsName = null;
            return null;
        }
        var (user, roles) = await AsUserAsync(session, userId.Value);
        if (user is null)
        {
            return "Uživatel ve škole není.";
        }
        if (roles.Count == 0)
        {
            return "Jako tento uživatel škola vstup podpory nepovoluje.";
        }
        var role = roleId is null
            ? roles.FirstOrDefault(r => r.Id == user.ActiveRoleId) ?? roles[0]
            : roles.FirstOrDefault(r => r.Id == roleId);
        if (role is null)
        {
            return "Tuto roli uživatel nemá.";
        }
        session.AsUserId = user.Id;
        session.AsRoleId = role.Id;
        session.AsName = SupportAsPolicy.Label(role.Name, PersonNames.Display(user));
        return null;
    }

    /// <summary>The school's user with their roles in the token's order —
    /// none when the school and the session's ticket don't let the support be them.</summary>
    private async Task<(User? User, List<Role> Roles)> AsUserAsync(SupportSession session, int userId)
    {
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Person).Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId && u.SchoolId == session.SchoolId && !u.IsSuperAdmin
                                      && u.DeactivatedAt == null && u.ErasedAt == null);
        if (user is null)
        {
            return (null, []);
        }
        var school = await SchoolOfAsync(session);
        var ticket = session.TicketId is { } ticketId
            ? await db.SupportTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == ticketId)
            : null;
        return (user, !SupportAsPolicy.Allows(school, ticket, user.Id) ? [] : user.Roles
            .OrderBy(r => r.BuiltIn is null).ThenBy(r => r.BuiltIn).ThenBy(r => r.Name)
            .ToList());
    }

    private async Task<Evidence.School> SchoolOfAsync(SupportSession session) =>
        session.School ?? await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == session.SchoolId);

    /// <summary>Null when the session may be neither whom it was nor the
    /// admin any more.</summary>
    private async Task<LoginResponse?> RespondAsync(SupportSession session)
    {
        if (session.AsUserId is { } asUserId)
        {
            var (user, roles) = await AsUserAsync(session, asUserId);
            if (user is not null && roles.FirstOrDefault(r => r.Id == session.AsRoleId) is { } role)
            {
                var (asToken, _) = tokens.CreateSupportToken(
                    session.SuperAdminUser!, session.School!, role,
                    await PermissionAccess.EffectivePermissionsAsync(db, role.Id), session.Id, session.ExpiresAt,
                    user, roles, session.AsName);
                return new LoginResponse(
                    asToken, user.Username, role.Kind.ToString(), session.School!.Name, session.School.Slug);
            }
            // Not allowed any more (or gone) — back to the admin, if allowed.
            if (await SetAsAsync(session, null, null) is not null)
            {
                return null;
            }
            await db.SaveChangesAsync();
        }
        else if (!SupportAsPolicy.AllowsAdmin(await SchoolOfAsync(session)))
        {
            return null;
        }
        var adminRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.SchoolId == session.SchoolId && r.BuiltIn == UserRole.Admin);
        var (token, _) = tokens.CreateSupportToken(
            session.SuperAdminUser!, session.School!, adminRole,
            await PermissionAccess.EffectivePermissionsAsync(db, adminRole.Id), session.Id, session.ExpiresAt);
        return new LoginResponse(
            token, session.SuperAdminUser!.Username, nameof(RoleKind.Staff), session.School!.Name, session.School.Slug);
    }

    private CookieOptions CookieOptionsFor(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = cookieOptions.Value.Secure,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
