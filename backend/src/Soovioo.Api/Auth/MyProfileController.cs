using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>Self-service profile for the caller's own <see cref="User"/> row —
/// unlike <see cref="UsersController"/> (Admin-only, manages anyone in the
/// school), every logged-in role (including SuperAdmin) can read/update their
/// own contact info and password here.</summary>
[Authorize]
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
        user.Phone = request.Phone;
        user.Email = request.Email;
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

    private async Task<User> CurrentUserAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Users.FirstAsync(u => u.Id == userId);
    }

    private static MyProfileDto ToDto(User user) =>
        new(user.Username, user.Role.ToString(), user.Phone, user.Email);
}

public record MyProfileDto(string Username, string Role, string? Phone, string? Email);

public record UpdateMyProfileRequest(string? Phone, string? Email);

/// <param name="RefreshToken">Mobile clients only — the web app's refresh
/// token travels in the cookie.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken = null);

/// <param name="RefreshToken">Null for cookie (web) callers.</param>
public record ChangePasswordResponse(string Token, string? RefreshToken = null);
