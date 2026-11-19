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

    /// <summary>When the teacher went through the pupils; null while not.</summary>
    public DateTimeOffset? AttendanceTakenAt { get; set; }

    /// <summary>The pupils who weren't there the whole lesson; the rest were.</summary>
    public List<LessonAttendance> Attendance { get; set; } = [];

    public LessonKey Key => new(Date, PeriodNumber, SchoolClassId, StudentGroupId);
}

/// <summary>A pupil not in the whole lesson. Only <see cref="LessonAttendanceStatus.Absent"/>
/// is an absence (in Docházka).</summary>
public class LessonAttendance
{
    public int LessonRecordId { get; set; }
    public LessonRecord? LessonRecord { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SchoolId { get; set; }

    public LessonAttendanceStatus Status { get; set; }

    /// <summary>How late, or how long before the end they left.</summary>
    public int? Minutes { get; set; }
}

public enum LessonAttendanceStatus
{
    Absent,
    Late,
    LeftEarly,

    /// <summary>Away for the school (a competition, an event) — no absence.</summary>
    Away,
}
