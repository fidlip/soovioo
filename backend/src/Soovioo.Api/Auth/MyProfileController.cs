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
public class MyProfileController(AppDbContext db, JwtTokenFactory tokenFactory) : ControllerBase
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
    /// typed it once; reissuing here avoids that.</summary>
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
        return Ok(new ChangePasswordResponse(tokenFactory.CreateToken(user)));
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

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ChangePasswordResponse(string Token);
