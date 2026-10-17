using Sova.Api.Evidence;

namespace Sova.Api.Homework;

public class Homework
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public required string Text { get; set; }
    public DateOnly DueDate { get; set; }

    /// <summary>Default false — most homework is just informational. When
    /// true, students/parents get a "splněno" checkbox (see
    /// HomeworkCompletion); when false, no completion UI is shown at
    /// all.</summary>
    public bool RequiresCompletion { get; set; }

    public required string CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Empty means "assigned to the whole class" — a non-empty set
    /// narrows it to specific students in that class (see
    /// HomeworkController.CreateHomework's validation that every id here
    /// actually belongs to SchoolClassId).</summary>
    public List<HomeworkStudent> Students { get; set; } = [];

    public List<HomeworkAttachment> Attachments { get; set; } = [];
    public List<HomeworkCompletion> Completions { get; set; } = [];
}
