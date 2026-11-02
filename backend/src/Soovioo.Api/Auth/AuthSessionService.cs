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

    /// <summary>One cookie per school (<c>soovioo_refresh_{schoolId}</c>),
    /// the bare name for the SuperAdmin: the schools' pages share the API's
    /// host, so with a single cookie a login in one school's tab replaced the
    /// session of another school's tab.</summary>
    public static string CookieNameFor(int? schoolId) =>
        schoolId is null ? CookieName : $"{CookieName}_{schoolId}";

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

    /// <summary>Raw refresh token from the request body (mobile) or, on the
    /// web, from the cookie of <paramref name="schoolId"/>'s page — falling
    /// back to the bare cookie, which a SuperAdmin on a school's page and
    /// sessions from before the per-school cookies have. A cookie-borne token
    /// counts only with the CSRF header. <c>Cookie</c> names the cookie it
    /// came from, null for the body.</summary>
    public static (string? Token, string? Cookie) ReadRefreshToken(HttpRequest request, string? bodyToken, int? schoolId)
    {
        if (!string.IsNullOrEmpty(bodyToken))
        {
            return (bodyToken, null);
        }
        if (!request.Headers.ContainsKey(CsrfHeaderName))
        {
            return (null, null);
        }
        string[] names = schoolId is null ? [CookieName] : [CookieNameFor(schoolId), CookieName];
        foreach (var name in names)
        {
            if (request.Cookies.TryGetValue(name, out var cookie) && !string.IsNullOrEmpty(cookie))
            {
                return (cookie, name);
            }
        }
        return (null, null);
    }

    public async Task<LoginResponse> RespondAsync(HttpContext http, User user, IssuedRefreshToken refresh, bool useCookie)
    {
        if (useCookie)
        {
            http.Response.Cookies.Append(CookieNameFor(user.SchoolId), refresh.Token, new CookieOptions
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

        var school = await SchoolOfAsync(user);
        return new LoginResponse(
            tokenFactory.CreateToken(user, school, await PermissionsOfAsync(user)),
            user.Username,
            user.IsSuperAdmin || user.ActiveRole is null ? nameof(UserRole.SuperAdmin) : user.ActiveRole.Kind.ToString(),
            school?.Name,
            school?.Slug,
            useCookie ? null : refresh.Token);
    }

    /// <summary>Just a new access token for the current session (refresh
    /// token untouched) — e.g. after switching the active role.</summary>
    public async Task<string> IssueAccessTokenAsync(User user) =>
        tokenFactory.CreateToken(user, await SchoolOfAsync(user), await PermissionsOfAsync(user));

    private async Task<IReadOnlyDictionary<Permission, PermissionScope>?> PermissionsOfAsync(User user) =>
        user.ActiveRoleId is { } roleId ? await PermissionAccess.EffectivePermissionsAsync(db, roleId) : null;

    private async Task<Evidence.School?> SchoolOfAsync(User user) =>
        user.SchoolId is { } schoolId
            ? await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == schoolId)
            : null;

    public void ClearCookie(HttpContext http, string name) =>
        http.Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = true,
            Secure = cookieOptions.Value.Secure,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        });
}
