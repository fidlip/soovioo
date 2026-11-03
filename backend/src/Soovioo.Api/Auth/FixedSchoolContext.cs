namespace Soovioo.Api.Auth;

/// <summary>One school's data outside a request (a background job).</summary>
public class FixedSchoolContext(int schoolId) : ICurrentSchoolContext
{
    public int? SchoolId { get; } = schoolId;
    public bool IsSuperAdmin => false;
}
