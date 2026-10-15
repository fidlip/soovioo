namespace Sova.Api.Auth;

public enum UserRole
{
    /// <summary>Manages schools themselves (create/list tenants). Not tied to any
    /// one School — <see cref="User.SchoolId"/> is null.</summary>
    SuperAdmin,

    /// <summary>Manages one School: users, classes, subjects, parent links.</summary>
    Admin,
    Teacher,
    Parent,
}
