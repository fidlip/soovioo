namespace Soovioo.Api.Auth;

/// <summary>The built-in roles (<see cref="Role.BuiltIn"/>) — every school
/// has these four, plus any of its own. SuperAdmin isn't a school role
/// (<see cref="User.IsSuperAdmin"/>); its name is the role claim of a
/// SuperAdmin's token.</summary>
public enum UserRole
{
    SuperAdmin,

    /// <summary>Manages one School: users, roles, classes, subjects, settings.</summary>
    Admin,
    Teacher,
    Parent,

    /// <summary>A pupil logging in for themselves. Linked to exactly one
    /// <see cref="Evidence.StudentProfile"/> through its Person.</summary>
    Student,
}
