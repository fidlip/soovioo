using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

public class TimetableEntry
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public DayOfWeek DayOfWeek { get; set; }
    public int PeriodNumber { get; set; }
    public WeekParity WeekParity { get; set; } = WeekParity.Every;

    /// <summary>Vyučující této konkrétní hodiny — může se lišit mezi předměty
    /// i od třídního učitele. Nullable, bez FK vynucujícího roli Teacher
    /// (stejný vzor jako <see cref="SchoolClass.ClassTeacherId"/>).</summary>
    public int? TeacherId { get; set; }
    public User? Teacher { get; set; }

    /// <summary>Defaults from Subject.DefaultClassroomId when the entry is
    /// created, but overridable per entry (see TimetableController.AddEntry).</summary>
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    /// <summary>The generator leaves it where it is.</summary>
    public bool Locked { get; set; }
}
