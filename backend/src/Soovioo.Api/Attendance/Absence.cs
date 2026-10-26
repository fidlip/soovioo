using Soovioo.Api.Evidence;

namespace Soovioo.Api.Attendance;

public class Absence
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Missed lesson numbers (0 = "nultá hodina"), sorted. Empty =
    /// the whole day without detail (records from before periods were
    /// tracked).</summary>
    public List<int> Periods { get; set; } = [];
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Unexcused;
    public string? ExcuseReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
