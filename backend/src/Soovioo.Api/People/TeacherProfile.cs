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

    /// <summary>Aprobace — the subjects (and grades) the teacher may teach.</summary>
    public List<TeacherQualification> Qualifications { get; set; } = [];

    /// <summary>What the subject list can't say about the aprobace, free text.</summary>
    public string? QualificationNote { get; set; }

    /// <summary>Nasmlouvané hodiny přímé výuky týdně; null = not entered.</summary>
    public decimal? ContractedHours { get; set; }

    /// <summary>Days the teacher can teach.</summary>
    public List<DayOfWeek> TeachingDays { get; set; } = [.. Weekdays];

    /// <summary>"D 6.–9., Čj, 1. stupeň" — with the subjects loaded.</summary>
    public string? Describe()
    {
        var parts = Qualifications.OrderBy(q => q.Subject?.Name).Select(q => q.Describe()).ToList();
        if (QualificationNote is { } note)
        {
            parts.Add(note);
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
}

/// <summary>One subject of a teacher's aprobace, for every grade or only
/// for ročníky <see cref="GradeFrom"/>–<see cref="GradeTo"/> (either end open).</summary>
public class TeacherQualification
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TeacherProfileId { get; set; }
    public TeacherProfile? TeacherProfile { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int? GradeFrom { get; set; }
    public int? GradeTo { get; set; }

    public string Describe()
    {
        var name = Subject?.Abbreviation ?? Subject?.Name ?? "";
        return (GradeFrom, GradeTo) switch
        {
            (null, null) => name,
            ({ } from, { } to) when from == to => $"{name} {from}.",
            ({ } from, null) => $"{name} od {from}.",
            (null, { } to) => $"{name} do {to}.",
            var (from, to) => $"{name} {from}.–{to}.",
        };
    }

    /// <summary>A class without a ročník is covered by any range.</summary>
    public bool Covers(int subjectId, int? grade) =>
        SubjectId == subjectId
        && (grade is null || ((GradeFrom is null || GradeFrom <= grade) && (GradeTo is null || GradeTo >= grade)));
}
