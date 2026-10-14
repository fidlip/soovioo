namespace Sova.Api.Grades;

public class Grade
{
    public int Id { get; set; }
    public required string StudentName { get; set; }
    public required string Subject { get; set; }
    public required int Value { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
