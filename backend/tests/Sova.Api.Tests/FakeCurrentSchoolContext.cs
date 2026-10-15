using Sova.Api.Auth;

namespace Sova.Api.Tests;

internal class FakeCurrentSchoolContext(int? schoolId, bool isSuperAdmin = false) : ICurrentSchoolContext
{
    public int? SchoolId { get; } = schoolId;
    public bool IsSuperAdmin { get; } = isSuperAdmin;
}
