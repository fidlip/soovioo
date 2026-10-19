using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

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

        var secret = NewSecret();
        session.HandoffCodeHash = null; // one use only
        session.HandoffExpiresAt = null;
        session.RefreshSecretHash = Hash(secret);
        await db.SaveChangesAsync();

        http.Response.Cookies.Append(CookieName, secret, CookieOptionsFor(session.ExpiresAt));
        return Respond(session);
    }

    /// <summary>A fresh support token when the request carries a live
    /// support cookie for <paramref name="schoolSlug"/>; null otherwise (the
    /// caller then falls back to the normal session).</summary>
    public async Task<LoginResponse?> RefreshAsync(HttpRequest request, string? schoolSlug)
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
            ? Respond(session)
            : null;
    }

    public void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, CookieOptionsFor(null));

    private LoginResponse Respond(SupportSession session)
    {
        var (token, _) = tokens.CreateSupportToken(session.SuperAdminUser!, session.School!, session.Id, session.ExpiresAt);
        return new LoginResponse(
            token, session.SuperAdminUser!.Username, nameof(UserRole.Admin), session.School!.Name, session.School.Slug);
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
