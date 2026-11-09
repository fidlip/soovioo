using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
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

    public const string DeactivatedMessage = "Účet už není aktivní. S dotazy se obraťte na školu.";

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        // Anonymous request — the audit log learns who it was from here.
        var audit = AuditContext.Of(HttpContext);
        audit.Username = request.Username;
        audit.Action = "Neúspěšné přihlášení";
        audit.Description = $"Neúspěšné přihlášení ({request.Username})";

        School? school = null;
        if (!string.IsNullOrWhiteSpace(request.SchoolSlug))
        {
            school = await db.Schools.FirstOrDefaultAsync(s => s.Slug == request.SchoolSlug);
            if (school is null)
            {
                return Unauthorized();
            }
        }
        audit.SchoolId = school?.Id;

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

        audit.UserId = user.Id;
        if (user.ErasedAt is not null)
        {
            return Unauthorized();
        }
        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }
        // After the password, so it doesn't tell strangers the account exists.
        if (user.DeactivatedAt is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, DeactivatedMessage);
        }
        audit.Action = "Přihlášení";
        audit.Description = "Přihlášení";

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
        if (user is null || user.InvitationTokenExpiresAt is null || user.InvitationTokenExpiresAt < DateTimeOffset.UtcNow
            || user.DeactivatedAt is not null)
        {
            return BadRequest("Pozvánka je neplatná nebo už vypršela.");
        }

        var audit = AuditContext.Of(HttpContext);
        audit.UserId = user.Id;
        audit.SchoolId = user.SchoolId;

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.InvitationToken = null;
        user.InvitationTokenExpiresAt = null;
        await db.SaveChangesAsync();

        // New password = every older session of this account is over.
        await refreshTokens.RevokeAllForUserAsync(user.Id);
        // The school can send an invite to any address — its way in must not
        // lead to the owner's accounts at other schools.
        await db.UnlinkAccountAsync(user.Id);
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
        // Its support session is over (left, taken back by the school,
        // expired) — the page doesn't go on as another session of the browser.
        if (!string.IsNullOrEmpty(request?.SchoolSlug) && Support.SupportSessionService.CarriesCookie(Request))
        {
            supportSessions.ClearCookie(HttpContext);
            return Unauthorized();
        }

        if (await PresentedTokenAsync(request) is not { } presented)
        {
            return NoContent();
        }
        if (!presented.ForThisPage)
        {
            // Another school's session: this page mustn't continue as its
            // account, and the cookie stays for the page it belongs to.
            return Unauthorized();
        }

        var rotated = await refreshTokens.RotateAsync(presented.Token);
        if (rotated is null || rotated.User.DeactivatedAt is not null)
        {
            if (presented.Cookie is { } cookie)
            {
                sessions.ClearCookie(HttpContext, cookie);
            }
            return Unauthorized();
        }

        var response = await sessions.RespondAsync(HttpContext, rotated.User, rotated.Successor, presented.Cookie is not null);
        // A session from before the per-school cookies moves to its school's.
        if (presented.Cookie is { } read && read != AuthSessionService.CookieNameFor(rotated.User.SchoolId))
        {
            sessions.ClearCookie(HttpContext, read);
        }
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest? request)
    {
        if (await PresentedTokenAsync(request) is not { ForThisPage: true } presented)
        {
            return NoContent();
        }
        AuditContext.Of(HttpContext).UserId = await refreshTokens.RevokeAsync(presented.Token);
        if (presented.Cookie is { } cookie)
        {
            sessions.ClearCookie(HttpContext, cookie);
        }
        return NoContent();
    }

    /// <summary>The refresh token the request presents, null when none. A web
    /// page sends its school's slug (none in the SuperAdmin area); a cookie
    /// is <c>ForThisPage</c> only when it's that school's account's or the
    /// SuperAdmin's.</summary>
    private async Task<PresentedToken?> PresentedTokenAsync(RefreshRequest? request)
    {
        int? schoolId = null;
        if (!string.IsNullOrEmpty(request?.SchoolSlug))
        {
            schoolId = await db.Schools.IgnoreQueryFilters()
                .Where(s => s.Slug == request.SchoolSlug)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
            if (schoolId is null && string.IsNullOrEmpty(request.RefreshToken))
            {
                return null;
            }
        }

        var (token, cookie) = AuthSessionService.ReadRefreshToken(Request, request?.RefreshToken, schoolId);
        if (token is null)
        {
            return null;
        }
        if (cookie is null)
        {
            return new PresentedToken(token, null, ForThisPage: true);
        }
        var owner = await refreshTokens.FindUserAsync(token);
        var forThisPage = owner is null || owner.IsSuperAdmin || owner.SchoolId == schoolId;
        return new PresentedToken(token, cookie, forThisPage);
    }

    private record PresentedToken(string Token, string? Cookie, bool ForThisPage);
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
/// <param name="SchoolSlug">The school whose page asks (web) — picks that
/// school's refresh cookie, refuses another school's session and lets a
/// support-mode session continue there, see SupportSessionService.</param>
public record RefreshRequest(string? RefreshToken = null, string? SchoolSlug = null);
