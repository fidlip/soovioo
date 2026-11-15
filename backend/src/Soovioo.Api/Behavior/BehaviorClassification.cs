using Soovioo.Api.Evidence;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Behavior;

public enum BehaviorClassificationValue
{
    VelmiDobre,
    Uspokojive,
    Neuspokojive,
}

/// <summary>One row per (student, grading period) — composite key,
/// configured in AppDbContext. Setting it again replaces the value (see
/// BehaviorController.SetClassification), there's no history kept.
/// Who may set which value is enforced in the controller: a class teacher
/// may only ever set VelmiDobre for their own class's students; anything
/// worse (Uspokojive/Neuspokojive) is Admin-only, since it affects the
/// report card (vysvědčení).</summary>
public class BehaviorClassification
{
    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int GradingPeriodId { get; set; }
    public GradingPeriod? GradingPeriod { get; set; }

    public int SchoolId { get; set; }

    public BehaviorClassificationValue Value { get; set; }
    public required string SetByName { get; set; }
    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;
}
