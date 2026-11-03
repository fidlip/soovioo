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

/// <summary>A class's own učební plán for a subject, in place of its grade's
/// (<see cref="CurriculumItem"/>): 7.A has Aj, 7.B Nj. Zero hours = the
/// class doesn't have the subject.</summary>
public class ClassCurriculumItem
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public decimal HoursPerWeek { get; set; }
}
