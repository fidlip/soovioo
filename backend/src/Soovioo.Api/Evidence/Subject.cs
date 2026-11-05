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

    /// <summary>Taught in blocks of two lessons in a row (the generator
    /// places them so); a grade's or a class's učební plán can say otherwise.</summary>
    public bool DoubleLessons { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>A row of the učební plán taught by divisions of other
    /// subjects (<see cref="Timetable.GroupDivision.CoversSubjectId"/>) — a
    /// placeholder, not graded or given homework itself. Filled in by
    /// <see cref="SubjectsController.GetSubjects"/>.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool CoveredByDivisions { get; set; }
}
