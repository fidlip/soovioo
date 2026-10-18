using Soovioo.Api.Evidence;

namespace Soovioo.Api.Homework;

/// <summary>Existence of a row means "this student marked it done" — only
/// meaningful when Homework.RequiresCompletion is true (see
/// HomeworkController.MarkComplete). Composite key (HomeworkId, StudentId),
/// configured in AppDbContext.</summary>
public class HomeworkCompletion
{
    public int HomeworkId { get; set; }
    public Homework? Homework { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SchoolId { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}
