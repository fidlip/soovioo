using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Teacher-specific data of a <see cref="Person"/>. Created when the
/// account gets the Teacher role; kept (not deleted) when the role is taken
/// away, so re-adding it restores the data. Třídnictví stays on
/// <see cref="SchoolClass.ClassTeacherId"/>.</summary>
public class TeacherProfile
{
    /// <summary>Primary key = the person's id (one profile per person).</summary>
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public int SchoolId { get; set; }

    /// <summary>Aprobace — subjects the teacher is qualified for, free text.</summary>
    public string? Qualification { get; set; }
}
