using Soovioo.Api.Evidence;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Grades;

/// <summary>A pupil's texts on a grading period's report card: the slovní
/// hodnocení of behaviour (its stupeň stays the behaviour classification),
/// and on a výpis the souhrnné slovní hodnocení — all subjects at once
/// instead of each (§ 15 odst. 2 of vyhláška 48/2005 Sb.) with the overall
/// result the třídní decides by the school's rules. The summary is confirmed
/// like a report card grade; only the management reopens it.</summary>
public class ReportCardNote
{
    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int GradingPeriodId { get; set; }
    public GradingPeriod? GradingPeriod { get; set; }

    public int SchoolId { get; set; }

    public string? BehaviorText { get; set; }

    public string? SummaryText { get; set; }
    public ReportCardResult? SummaryResult { get; set; }
    public string? SummaryConfirmedByName { get; set; }
    public DateTimeOffset? SummaryConfirmedAt { get; set; }

    public required string SetByName { get; set; }
    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;
}
