namespace Sova.Api.Evidence;

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

    /// <summary>Pre-fills a new timetable entry's classroom when this
    /// subject is picked — for subjects only ever taught in one specialized
    /// room (fyzika, tělocvik...). Still overridable per entry, see
    /// TimetableEntry.ClassroomId.</summary>
    public int? DefaultClassroomId { get; set; }
    public Classroom? DefaultClassroom { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
