using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenFactory tokenFactory) : ControllerBase
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

        var tokenString = tokenFactory.CreateToken(user);
        return Ok(new LoginResponse(tokenString, user.Username, user.Role.ToString(), school?.Name, school?.Slug));
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

        var school = user.SchoolId is { } schoolId
            ? await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == schoolId)
            : null;

        var tokenString = tokenFactory.CreateToken(user);
        return Ok(new LoginResponse(tokenString, user.Username, user.Role.ToString(), school?.Name, school?.Slug));
    }
}

public record LoginRequest(string Username, string Password, string? SchoolSlug = null);
public record LoginResponse(string Token, string Username, string Role, string? SchoolName, string? SchoolSlug);
public record AcceptInviteRequest(string Token, string NewPassword);
