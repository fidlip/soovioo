using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>The human behind an account — one per person, whatever roles
/// they have (a teacher who is also a parent is still one Person). Roles add
/// profiles on top (<see cref="TeacherProfile"/>, <see cref="ParentProfile"/>)
/// instead of an inheritance tree, which would make that teacher-mother two
/// records with duplicated data. A pupil's matrika data stays in
/// <see cref="Student"/>, linked to their account via <c>Student.UserId</c>.</summary>
public class Person
{
    public int Id { get; set; }

    /// <summary>Null only for a SuperAdmin's person (not tied to a school).</summary>
    public int? SchoolId { get; set; }
    public School? School { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";

    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Own data this person hid from parents/pupils in their
    /// Settings — counts only where the school allows it, see
    /// <see cref="Visibility.Shows"/>.</summary>
    public VisibilityOption HiddenData { get; set; }

    /// <summary>The login account — one-to-one. Nullable so a person can
    /// later be kept on record without an account (e.g. a legal guardian
    /// who doesn't use the system); today every Person has one, and deleting
    /// the account deletes the Person with its profiles.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public TeacherProfile? TeacherProfile { get; set; }
    public ParentProfile? ParentProfile { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>How a person is shown in the UI.</summary>
public static class PersonNames
{
    /// <summary>"Jana Nováková", falling back to the login name for accounts
    /// without a Person (SuperAdmin) or without a filled-in name. Usable as
    /// the final step of an EF projection (evaluated client-side).</summary>
    public static string Display(Person? person, string username) =>
        person is null || person.FullName.Length == 0 ? username : person.FullName;

    public static string Display(User user) => Display(user.Person, user.Username);
}
