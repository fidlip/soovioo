using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>A teacher away from school from one day to another, the whole
/// day or the same periods of each day. Their lessons then need a
/// substitute.</summary>
public class TeacherAbsence
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TeacherId { get; set; }
    public User? Teacher { get; set; }

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    /// <summary>Null = from the first period.</summary>
    public int? PeriodFrom { get; set; }

    /// <summary>Null = to the last period.</summary>
    public int? PeriodTo { get; set; }

    public bool Covers(DateOnly date, int period) =>
        DateFrom <= date && date <= DateTo
        && (PeriodFrom is null || PeriodFrom <= period) && (PeriodTo is null || period <= PeriodTo);
}

/// <summary>One lesson of one day other than the timetable has it: of a
/// class (<see cref="SchoolClassId"/>) or of a group (<see cref="StudentGroupId"/>).
/// The agenda edits these; everyone else sees a day's changes once the day
/// is published (<see cref="SubstitutionDay"/>). The version stays as it is.</summary>
public class LessonChange
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public DateOnly Date { get; set; }
    public int PeriodNumber { get; set; }

    public int? SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int? StudentGroupId { get; set; }
    public StudentGroup? StudentGroup { get; set; }

    /// <summary>A lesson the timetable doesn't have.</summary>
    public bool Extra { get; set; }

    public bool Cancelled { get; set; }

    /// <summary>Null = the timetable's.</summary>
    public int? TeacherId { get; set; }
    public User? Teacher { get; set; }

    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    /// <summary>Taught at another period of the same week instead.</summary>
    public DateOnly? MovedToDate { get; set; }
    public int? MovedToPeriod { get; set; }

    /// <summary>Joins the lesson of another class or group at the same time.</summary>
    public int? MergedWithClassId { get; set; }
    public int? MergedWithGroupId { get; set; }

    /// <summary>The timetable's lesson it was made for: once the timetable
    /// has another one then, the change doesn't apply to it.</summary>
    public int? OfSubjectId { get; set; }
    public int? OfTeacherId { get; set; }

    public LessonChangeData Data() => new(
        Date, PeriodNumber, SchoolClassId, StudentGroupId, Extra, Cancelled, TeacherId, SubjectId, ClassroomId,
        MovedToDate, MovedToPeriod, MergedWithClassId, MergedWithGroupId, OfSubjectId, OfTeacherId);
}

/// <summary>A day whose changes are published: the changes as they were
/// then. Editing them afterwards shows nowhere until the day is published
/// again.</summary>
public class SubstitutionDay
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public DateOnly Date { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    public List<LessonChangeData> Changes { get; set; } = [];
}

/// <summary>Which lesson: of a class (<see cref="ClassId"/>) or of a group.</summary>
public record LessonKey(DateOnly Date, int Period, int? ClassId, int? GroupId);

public record LessonChangeData(
    DateOnly Date, int Period, int? ClassId, int? GroupId, bool Extra, bool Cancelled, int? TeacherId, int? SubjectId,
    int? ClassroomId, DateOnly? MovedToDate, int? MovedToPeriod, int? MergedWithClassId, int? MergedWithGroupId,
    int? OfSubjectId = null, int? OfTeacherId = null)
{
    public LessonKey Key => new(Date, Period, ClassId, GroupId);

    /// <summary>Made for this lesson of the timetable (or for whichever, from before that was known).</summary>
    public bool Fits(BaseLesson lesson) =>
        OfSubjectId is null || (OfSubjectId == lesson.SubjectId && OfTeacherId == lesson.TeacherId);

    /// <summary>Changes nothing.</summary>
    public bool Empty => this with { OfSubjectId = null, OfTeacherId = null }
        == new LessonChangeData(Date, Period, ClassId, GroupId, false, false, null, null, null, null, null, null, null);

    public LessonKey? MovedTo => MovedToDate is { } date && MovedToPeriod is { } period
        ? new LessonKey(date, period, ClassId, GroupId)
        : null;

    public LessonKey? MergedWith => MergedWithClassId is not null || MergedWithGroupId is not null
        ? new LessonKey(Date, Period, MergedWithClassId, MergedWithGroupId)
        : null;
}
