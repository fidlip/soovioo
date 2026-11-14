namespace Soovioo.SupportPlans.Data;

/// <summary>A support plan (Free, Placený, Prioritní…): how many working days
/// a school's support ticket may take.</summary>
public class SupportPlan
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int ResponseWorkingDays { get; set; }
}

/// <summary>A school's plan from <see cref="ValidFrom"/> to <see cref="ValidTo"/>
/// (both inclusive; no end = until further notice). <see cref="SchoolId"/> is
/// the school's id in the Soovioo app.</summary>
public class SchoolPlan
{
    public int Id { get; set; }
    public int SchoolId { get; set; }
    public int PlanId { get; set; }
    public SupportPlan Plan { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}
