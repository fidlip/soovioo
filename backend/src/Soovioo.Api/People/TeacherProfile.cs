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

    /// <summary>When the teacher can't or would rather not teach; a slot
    /// not listed is free.</summary>
    public List<AvailabilitySlot> Availability { get; set; } = [];

    /// <summary>Wishes the generator tries to meet: days a week without
    /// lessons (any of them), lessons a day on a day with lessons, free
    /// periods between lessons a day. Null = no wish.</summary>
    public int? FreeDays { get; set; }
    public int? MinLessonsPerDay { get; set; }
    public int? MaxLessonsPerDay { get; set; }
    public int? MaxGapsPerDay { get; set; }

    /// <summary>Days not off as a whole.</summary>
    public List<DayOfWeek> TeachingDays => AvailabilitySlot.TeachingDays(Availability);

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

public enum AvailabilityKind
{
    /// <summary>Nemůže — the generator never places a lesson there.</summary>
    Unavailable,

    /// <summary>Raději ne — only when it has to.</summary>
    Avoid,
}

/// <param name="Period">Null = the whole day.</param>
public record AvailabilitySlot(DayOfWeek Day, int? Period, AvailabilityKind Kind)
{
    public static List<DayOfWeek> TeachingDays(IEnumerable<AvailabilitySlot> slots) =>
        [.. TeacherProfile.Weekdays.Where(d => !slots.Any(s => s.Day == d && s.Period is null && s.Kind == AvailabilityKind.Unavailable))];

    /// <summary>One entry a slot; a day off as a whole drops its periods.</summary>
    public static List<AvailabilitySlot> Normalize(IEnumerable<AvailabilitySlot> slots)
    {
        var list = slots.DistinctBy(s => (s.Day, s.Period)).ToList();
        var daysOff = list.Where(s => s.Period is null && s.Kind == AvailabilityKind.Unavailable).Select(s => s.Day).ToHashSet();
        return [.. list.Where(s => s.Period is null || !daysOff.Contains(s.Day)).OrderBy(s => s.Day).ThenBy(s => s.Period ?? -1)];
    }

    /// <summary>The slot's kind, the whole day's first; null = free.</summary>
    public static AvailabilityKind? At(IEnumerable<AvailabilitySlot> slots, DayOfWeek day, int period)
    {
        var kinds = slots.Where(s => s.Day == day && (s.Period is null || s.Period == period)).Select(s => s.Kind).ToList();
        return kinds.Count == 0 ? null : kinds.Min();
    }
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
