using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Školní "zvonění" — čas od-do pro dané číslo hodiny, společný pro
/// celou školu napříč třídami. Drženo odděleně od <see cref="TimetableEntry"/>,
/// aby se čas nezadával znovu u každé jednotlivé hodiny v rozvrhu.</summary>
public class SchoolPeriod
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int PeriodNumber { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
