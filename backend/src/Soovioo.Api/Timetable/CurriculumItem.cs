using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Učební plán: hours a week of a subject in a grade
/// (<see cref="SchoolClass.Rocnik"/>), per timetable version.</summary>
public class CurriculumItem
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int Grade { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public decimal HoursPerWeek { get; set; }
}
