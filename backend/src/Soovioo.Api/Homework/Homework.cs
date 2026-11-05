using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Homework;

public class Homework
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>The class, or null for homework of a <see cref="StudentGroup"/>
    /// (also across classes).</summary>
    public int? SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    /// <summary>Homework of a group, for its pupils (the members now).</summary>
    public int? StudentGroupId { get; set; }
    public StudentGroup? StudentGroup { get; set; }

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

    /// <summary>Empty means "assigned to the whole class / group" — a
    /// non-empty set narrows it to specific students of it (see
    /// HomeworkController.CreateHomework's validation that every id here
    /// actually belongs to it).</summary>
    public List<HomeworkStudent> Students { get; set; } = [];

    public List<HomeworkAttachment> Attachments { get; set; } = [];
    public List<HomeworkCompletion> Completions { get; set; } = [];
}
