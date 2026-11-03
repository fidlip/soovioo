namespace Soovioo.Api.Evidence;

public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Short form used on timetables/report cards where the full name
    /// doesn't fit (e.g. "Čj" for Český jazyk) — real schools expect this.</summary>
    public string? Abbreviation { get; set; }

    /// <summary>"#rrggbb" — background color for this subject's cells on the
    /// Rozvrh table. Same preset-palette pattern as Event.Color.</summary>
    public string? Color { get; set; }

    /// <summary>Where the subject is taught, first choice first: the
    /// generator and a new timetable entry take the first free one. Empty =
    /// no particular classroom.</summary>
    public List<SubjectClassroom> Classrooms { get; set; } = [];

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
