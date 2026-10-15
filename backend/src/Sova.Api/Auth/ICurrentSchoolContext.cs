using System.Security.Claims;

namespace Sova.Api.Auth;

/// <summary>The tenant boundary for the current request, read from the JWT
/// (not from a Host header/subdomain — the token is the source of truth for
/// authorization, so a spoofed Host can't leak another school's data).</summary>
public interface ICurrentSchoolContext
{
    int? SchoolId { get; }

    /// <summary>SuperAdmin accounts aren't tied to a school and bypass the
    /// per-school query filters entirely (used for cross-school management).</summary>
    bool IsSuperAdmin { get; }
}

public class HttpCurrentSchoolContext(IHttpContextAccessor accessor) : ICurrentSchoolContext
{
    public int? SchoolId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue("schoolId");
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsSuperAdmin =>
        accessor.HttpContext?.User.IsInRole(nameof(UserRole.SuperAdmin)) ?? false;
}
