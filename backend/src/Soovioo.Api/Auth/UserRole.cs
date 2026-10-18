namespace Soovioo.Api.Auth;

public enum UserRole
{
    /// <summary>Manages schools themselves (create/list tenants). Not tied to any
    /// one School — <see cref="User.SchoolId"/> is null.</summary>
    SuperAdmin,

    /// <summary>Manages one School: users, classes, subjects, parent links.</summary>
    Admin,
    Teacher,
    Parent,

    /// <summary>A student logging in for themselves — read-only access to
    /// their own grades/absences/timetable/messages (same shape as Parent),
    /// but unlike Parent, never allowed to submit an excuse (see
    /// AbsencesController.SubmitExcuse). Linked to exactly one
    /// <see cref="Evidence.Student"/> via <see cref="Evidence.Student.UserId"/>,
    /// not a new join table — one Student row has at most one Student-role
    /// account.</summary>
    Student,
}
