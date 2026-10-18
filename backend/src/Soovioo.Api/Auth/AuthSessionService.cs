using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>Turns a verified <see cref="User"/> into a session: a short-lived
/// access JWT plus a rotating <see cref="RefreshToken"/>. The web app gets
/// the refresh token as an httpOnly cookie (never readable by page script);
/// the NativeScript app, which has no cookie jar worth relying on, gets it in
/// the response body and keeps sending Bearer tokens.</summary>
public class AuthSessionService(
    AppDbContext db,
    JwtTokenFactory tokenFactory,
    RefreshTokenService refreshTokens,
    IOptions<AuthCookieOptions> cookieOptions)
{
    public const string CookieName = "soovioo_refresh";

    /// <summary>Every request the web app sends that relies on the refresh
    /// cookie must carry this header. A custom header can't be sent
    /// cross-origin without a CORS preflight, which the allow-list rejects —
    /// so together with SameSite=Strict a foreign page can't ride the cookie
    /// (CSRF).</summary>
    public const string CsrfHeaderName = "X-Soovioo-Csrf";

    // Scoped to /api: besides /api/auth/* it's also needed by
    // /api/users/me/change-password, which reissues the session.
    private const string CookiePath = "/api";

    public async Task<LoginResponse> StartAsync(HttpContext http, User user, bool persistent, bool useCookie)
    {
        var refresh = await refreshTokens.IssueAsync(user, persistent);
        return await RespondAsync(http, user, refresh, useCookie);
    }

    /// <summary>Raw refresh token from the cookie (web) or the request body
    /// (mobile). A cookie-borne token counts only with the CSRF header.</summary>
    public static (string? Token, bool FromCookie) ReadRefreshToken(HttpRequest request, string? bodyToken)
    {
        if (!string.IsNullOrEmpty(bodyToken))
        {
            return (bodyToken, false);
        }
        if (request.Cookies.TryGetValue(CookieName, out var cookie) && !string.IsNullOrEmpty(cookie)
            && request.Headers.ContainsKey(CsrfHeaderName))
        {
            return (cookie, true);
        }
        return (null, false);
    }

    public async Task<LoginResponse> RespondAsync(HttpContext http, User user, IssuedRefreshToken refresh, bool useCookie)
    {
        if (useCookie)
        {
            http.Response.Cookies.Append(CookieName, refresh.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = cookieOptions.Value.Secure,
                SameSite = SameSiteMode.Strict,
                Path = CookiePath,
                // No Expires = browser-session cookie, gone when the browser
                // closes (the server-side 8 h cap still applies on top).
                Expires = refresh.Persistent ? refresh.ExpiresAt : null,
                IsEssential = true,
            });
        }

        var school = user.SchoolId is { } schoolId
            ? await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == schoolId)
            : null;
        return new LoginResponse(
            tokenFactory.CreateToken(user, school),
            user.Username,
            user.Role.ToString(),
            school?.Name,
            school?.Slug,
            useCookie ? null : refresh.Token);
    }

    public void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = cookieOptions.Value.Secure,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        });
}
