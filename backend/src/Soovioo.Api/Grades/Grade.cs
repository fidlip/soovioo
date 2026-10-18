using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

public class Grade
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public required int Value { get; set; }

    /// <summary>Weight in the weighted average (Czech convention — a test counts
    /// more than homework). Defaults to 1 so ungraded callers behave like a
    /// plain average.</summary>
    public int Weight { get; set; } = 1;

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
