using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.ClassBook;

/// <summary>A lesson written into the třídní kniha: which lesson of the day
/// (<see cref="LessonKey"/>) and what was taught. Subject and teacher as they
/// were when it was written.</summary>
public class LessonRecord
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public DateOnly Date { get; set; }
    public int PeriodNumber { get; set; }

    /// <summary>A lesson of the whole class; null for a group's.</summary>
    public int? SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    /// <summary>No foreign key: the record stays when the timetable version
    /// with the group goes.</summary>
    public int? StudentGroupId { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int? TeacherId { get; set; }
    public User? Teacher { get; set; }

    public string Topic { get; set; } = "";

    public int? RecordedById { get; set; }
    public User? RecordedBy { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public LessonKey Key => new(Date, PeriodNumber, SchoolClassId, StudentGroupId);
}
