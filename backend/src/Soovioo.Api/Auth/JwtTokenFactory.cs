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
        var options = jwtOptions.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
