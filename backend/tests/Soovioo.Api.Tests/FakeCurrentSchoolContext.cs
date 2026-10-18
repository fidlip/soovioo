using Soovioo.Api.Auth;

namespace Soovioo.Api.Tests;

internal class FakeCurrentSchoolContext(int? schoolId, bool isSuperAdmin = false) : ICurrentSchoolContext
{
    public int? SchoolId { get; } = schoolId;
    public bool IsSuperAdmin { get; } = isSuperAdmin;
}
