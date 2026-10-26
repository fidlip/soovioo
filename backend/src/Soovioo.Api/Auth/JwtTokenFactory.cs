using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
    /// <param name="permissions">Of the active role — for the clients to show
    /// what's allowed; the API itself checks the database
    /// (<see cref="PermissionAccess"/>).</param>
    public string CreateToken(
        User user, School? school = null, IReadOnlyDictionary<Permission, PermissionScope>? permissions = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("mustChangePassword", user.MustChangePassword ? "true" : "false"),
        };
        if (user.IsSuperAdmin || user.ActiveRole is null)
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(UserRole.SuperAdmin)));
        }
        else
        {
            claims.AddRange(RoleClaims(user.ActiveRole, user.Roles, permissions));
        }
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
        User superAdmin, School school, Role adminRole, IReadOnlyDictionary<Permission, PermissionScope> permissions,
        int sessionId, DateTimeOffset? sessionExpiresAt = null)
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
            new("mustChangePassword", "false"),
            new("schoolId", school.Id.ToString()),
            new("schoolType", school.Type.ToString()),
            new(Support.SupportClaims.SessionId, sessionId.ToString()),
            new(Support.SupportClaims.By, superAdmin.Username),
            new(Support.SupportClaims.SchoolName, school.Name),
        };
        claims.AddRange(RoleClaims(adminRole, [adminRole], permissions));
        return (Write(claims, expires), new DateTimeOffset(expires));
    }

    /// <summary>The role claim is the active role's <see cref="RoleKind"/>
    /// (<c>[Authorize(Roles = "Parent,Student")]</c> etc.); <c>roles</c> is
    /// everything the role switcher may offer, <c>perms</c> the active role's
    /// permissions as <c>Permission:Scope</c> pairs.</summary>
    private static readonly JsonSerializerOptions RelaxedJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static IEnumerable<Claim> RoleClaims(
        Role active, IEnumerable<Role> all, IReadOnlyDictionary<Permission, PermissionScope>? permissions)
    {
        yield return new Claim(ClaimTypes.Role, active.Kind.ToString());
        yield return new Claim(PermissionAccess.RoleIdClaim, active.Id.ToString());
        yield return new Claim("roleName", active.Name);
        yield return new Claim("roles", JsonSerializer.Serialize(
            all.OrderBy(r => r.BuiltIn is null).ThenBy(r => r.BuiltIn).ThenBy(r => r.Name)
                .Select(r => new { id = r.Id, name = r.Name, kind = r.Kind.ToString() }), RelaxedJson));
        yield return new Claim("perms", string.Join(",",
            (permissions ?? new Dictionary<Permission, PermissionScope>()).Select(p => $"{p.Key}:{p.Value}")));
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
