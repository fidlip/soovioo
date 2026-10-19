using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    AuthSessionService sessions,
    RefreshTokenService refreshTokens,
    Support.SupportSessionService supportSessions) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        School? school = null;
        if (!string.IsNullOrWhiteSpace(request.SchoolSlug))
        {
            school = await db.Schools.FirstOrDefaultAsync(s => s.Slug == request.SchoolSlug);
            if (school is null)
            {
                return Unauthorized();
            }
        }

        // Unauthenticated request: no ambient tenant yet, so the per-school
        // query filter would otherwise hide every row. Scope explicitly by
        // the resolved school (or null, for a SuperAdmin login with no slug).
        int? schoolId = school?.Id;
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.SchoolId == schoolId);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        // The mobile app has no "Zůstat přihlášen" checkbox — an app stays
        // signed in until the user logs out, like any other mobile app.
        var persistent = !request.UseCookie || request.RememberMe;
        return Ok(await sessions.StartAsync(HttpContext, user, persistent, request.UseCookie));
    }

    /// <summary>Consumes a <see cref="User.InvitationToken"/> e-mailed by
    /// <see cref="UsersController"/>'s invite endpoint — the recipient never
    /// had a password of their own to prove, the token stands in for it.
    /// Unauthenticated like <see cref="Login"/>, and for the same reason
    /// returns a fresh <see cref="LoginResponse"/>: the invite flow ends
    /// with the user logged in, not back at the login form.</summary>
    [HttpPost("accept-invite")]
    public async Task<ActionResult<LoginResponse>> AcceptInvite(AcceptInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest("Nové heslo nesmí být prázdné.");
        }

        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.InvitationToken == request.Token);
        if (user is null || user.InvitationTokenExpiresAt is null || user.InvitationTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            return BadRequest("Pozvánka je neplatná nebo už vypršela.");
        }

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.InvitationToken = null;
        user.InvitationTokenExpiresAt = null;
        await db.SaveChangesAsync();

        // New password = every older session of this account is over.
        await refreshTokens.RevokeAllForUserAsync(user.Id);
        return Ok(await sessions.StartAsync(HttpContext, user, persistent: !request.UseCookie, request.UseCookie));
    }

    /// <summary>Swaps a refresh token (cookie for the web app, body for
    /// mobile) for a fresh access token, rotating the refresh token. 204 when
    /// no refresh token was presented at all — the web app calls this on
    /// every startup to restore a session, and a logged-out visitor isn't an
    /// error worth a red 401 in the console.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest? request)
    {
        // On a school's page with a live support session for that school,
        // stay in support mode (see SupportSessionService).
        if (await supportSessions.RefreshAsync(Request, request?.SchoolSlug) is { } support)
        {
            return Ok(support);
        }

        var (token, fromCookie) = AuthSessionService.ReadRefreshToken(Request, request?.RefreshToken);
        if (token is null)
        {
            return NoContent();
        }

        var rotated = await refreshTokens.RotateAsync(token);
        if (rotated is null)
        {
            if (fromCookie)
            {
                sessions.ClearCookie(HttpContext);
            }
            return Unauthorized();
        }

        return Ok(await sessions.RespondAsync(HttpContext, rotated.User, rotated.Successor, fromCookie));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest? request)
    {
        var (token, fromCookie) = AuthSessionService.ReadRefreshToken(Request, request?.RefreshToken);
        if (token is not null)
        {
            await refreshTokens.RevokeAsync(token);
        }
        if (fromCookie || Request.Cookies.ContainsKey(AuthSessionService.CookieName))
        {
            sessions.ClearCookie(HttpContext);
        }
        return NoContent();
    }
}

/// <param name="UseCookie">Web app: deliver the refresh token as an httpOnly
/// cookie instead of in the response body.</param>
/// <param name="RememberMe">"Zůstat přihlášen" — 30-day persistent cookie
/// instead of a browser-session one. Only meaningful with
/// <paramref name="UseCookie"/>.</param>
public record LoginRequest(
    string Username,
    string Password,
    string? SchoolSlug = null,
    bool UseCookie = false,
    bool RememberMe = false);

/// <param name="RefreshToken">Only for body-delivery (mobile) clients; null
/// when it went into the cookie.</param>
public record LoginResponse(
    string Token,
    string Username,
    string Role,
    string? SchoolName,
    string? SchoolSlug,
    string? RefreshToken = null);

public record AcceptInviteRequest(string Token, string NewPassword, bool UseCookie = false);

/// <param name="RefreshToken">Mobile clients only; the web app's travels in
/// the cookie.</param>
/// <param name="SchoolSlug">The school whose page asks (web) — lets a
/// support-mode session continue there, see SupportSessionService.</param>
public record RefreshRequest(string? RefreshToken = null, string? SchoolSlug = null);
