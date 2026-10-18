using Soovioo.Api.Evidence;

namespace Soovioo.Api.Behavior;

/// <summary>"Pololetí" — named to avoid any confusion with the US
/// "semester" (which doesn't map 1:1 to the Czech school year's two
/// halves).</summary>
public enum SchoolHalfYear
{
    First = 1,
    Second = 2,
}

public enum BehaviorClassificationValue
{
    VelmiDobre,
    Uspokojive,
    Neuspokojive,
}

/// <summary>One row per (student, school year, half) — composite key,
/// configured in AppDbContext. Setting it again replaces the value (see
/// BehaviorController.SetClassification), there's no history kept.
/// Who may set which value is enforced in the controller: a class teacher
/// may only ever set VelmiDobre for their own class's students; anything
/// worse (Uspokojive/Neuspokojive) is Admin-only, since it affects the
/// report card (vysvědčení).</summary>
public class BehaviorClassification
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SchoolYearStartYear { get; set; }
    public SchoolHalfYear HalfYear { get; set; }

    public int SchoolId { get; set; }

    public BehaviorClassificationValue Value { get; set; }
    public required string SetByName { get; set; }
    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;
}
