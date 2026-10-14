using Sova.Api.Evidence;

namespace Sova.Api.Grades;

public class Grade
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public required int Value { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
