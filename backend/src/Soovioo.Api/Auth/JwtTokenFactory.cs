using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

/// <summary>Builds the JWT for a <see cref="User"/> — shared by
/// <see cref="AuthController"/> (fresh login) and <see cref="MyProfileController"/>
/// (password change needs a reissued token so the <c>mustChangePassword</c>
/// claim reflects the just-cleared flag without forcing a re-login). Valid
/// 15 minutes only; sessions last longer through <see cref="RefreshToken"/>s,
/// see <see cref="AuthSessionService"/>.</summary>
public class JwtTokenFactory(IOptions<JwtOptions> jwtOptions)
{
    /// <param name="school">The user's school, if any — its type rides along
    /// as the <c>schoolType</c> claim so every client can pick "žák" vs.
    /// "student" without an extra request.</param>
    public string CreateToken(User user, School? school = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.ActiveRole.ToString()),
            new("mustChangePassword", user.MustChangePassword ? "true" : "false"),
            // Everything the role switcher may offer — deliberately not
            // ClaimTypes.Role, which stays the single active role.
            new("roles", string.Join(",", user.Roles.Order())),
        };
        if (user.SchoolId is { } userSchoolId)
        {
            claims.Add(new Claim("schoolId", userSchoolId.ToString()));
        }
        if (school is not null)
        {
            claims.Add(new Claim("schoolType", school.Type.ToString()));
        }

        return Write(claims, DateTime.UtcNow.AddMinutes(15));
    }

    /// <summary>Support mode: the SuperAdmin acts as an Admin of
    /// <paramref name="school"/> (role and schoolId claims as for a real
    /// admin, so every tenant filter and role check applies unchanged), with
    /// the support claims marking who's behind it. 15 minutes, renewed via
    /// the support cookie (see Support.SupportSessionService).</summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateSupportToken(
        User superAdmin, School school, int sessionId, DateTimeOffset? sessionExpiresAt = null)
    {
        // Short-lived like normal tokens; the support cookie refreshes it
        // until the session itself ends.
        var expires = DateTime.UtcNow.AddMinutes(15);
        if (sessionExpiresAt is { } end && end.UtcDateTime < expires)
        {
            expires = end.UtcDateTime;
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, superAdmin.Id.ToString()),
            new(ClaimTypes.Name, superAdmin.Username),
            new(ClaimTypes.Role, nameof(UserRole.Admin)),
            new("mustChangePassword", "false"),
            new("roles", nameof(UserRole.Admin)),
            new("schoolId", school.Id.ToString()),
            new("schoolType", school.Type.ToString()),
            new(Support.SupportClaims.SessionId, sessionId.ToString()),
            new(Support.SupportClaims.By, superAdmin.Username),
            new(Support.SupportClaims.SchoolName, school.Name),
        };
        return (Write(claims, expires), new DateTimeOffset(expires));
    }

    private string Write(IEnumerable<Claim> claims, DateTime expires)
    {
        var options = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
