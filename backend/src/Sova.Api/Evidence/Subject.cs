namespace Sova.Api.Evidence;

public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Short form used on timetables/report cards where the full name
    /// doesn't fit (e.g. "Čj" for Český jazyk) — real schools expect this.</summary>
    public string? Abbreviation { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
