using Sova.Api.Evidence;

namespace Sova.Api.Attendance;

public class Absence
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public DateOnly Date { get; set; }
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Unexcused;
    public string? ExcuseReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
