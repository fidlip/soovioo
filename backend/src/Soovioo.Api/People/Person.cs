using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>The human behind an account — one per person, whatever roles
/// they have (a teacher who is also a parent is still one Person). Roles add
/// profiles on top (<see cref="TeacherProfile"/>, <see cref="ParentProfile"/>)
/// instead of an inheritance tree, which would make that teacher-mother two
/// records with duplicated data. A pupil is a Person too, with their
/// matrika data in <see cref="Evidence.StudentProfile"/> as the pupil's profile.</summary>
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

    /// <summary>Profile photo — storage key of its files (see
    /// <see cref="PhotoStorage"/>), null = initials instead. A new upload
    /// gets a new key, so the key doubles as the clients' cache buster.</summary>
    public string? PhotoKey { get; set; }

    /// <summary>A pupil's photo: set with <see cref="PhotoKey"/>, a photo the
    /// pupil uploaded waits for a parent's (or the school's) approval. Null
    /// for everyone else.</summary>
    public PhotoStatus? PhotoStatus { get; set; }

    /// <summary>The login account — one-to-one, null for someone who
    /// doesn't use the app. The Person outlives it: deleting the account
    /// leaves the record (Administrace → Osoby).</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public TeacherProfile? TeacherProfile { get; set; }
    public ParentProfile? ParentProfile { get; set; }
    public StudentProfile? Student { get; set; }

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
