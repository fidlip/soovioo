using Sova.Api.Auth;
using Sova.Api.Evidence;

namespace Sova.Api.Timetable;

public class TimetableEntry
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

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
}
