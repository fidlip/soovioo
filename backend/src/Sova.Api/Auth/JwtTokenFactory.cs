using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sova.Api.Auth;

/// <summary>Builds the JWT for a <see cref="User"/> — shared by
/// <see cref="AuthController"/> (fresh login) and <see cref="MyProfileController"/>
/// (password change needs a reissued token so the <c>mustChangePassword</c>
/// claim reflects the just-cleared flag without forcing a re-login).</summary>
public class JwtTokenFactory(IOptions<JwtOptions> jwtOptions)
{
    public string CreateToken(User user)
    {
        var options = jwtOptions.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("mustChangePassword", user.MustChangePassword ? "true" : "false"),
        };
        if (user.SchoolId is { } userSchoolId)
        {
            claims.Add(new Claim("schoolId", userSchoolId.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
