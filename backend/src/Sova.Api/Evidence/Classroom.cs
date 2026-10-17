namespace Sova.Api.Evidence;

public class Classroom
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Free text on purpose (e.g. "kmenová", "odborná — fyzika",
    /// "tělocvična") — a fixed enum would need updating for every school's
    /// own naming, and this field is purely informational, never filtered
    /// on.</summary>
    public string? Type { get; set; }

    public int? Capacity { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
