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

    /// <summary>Why the teacher rejected the omluvenka for this day.</summary>
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Who made the record; null for older records and once the
    /// account is gone.</summary>
    public int? CreatedById { get; set; }
    public Auth.User? CreatedBy { get; set; }

    /// <summary>The omluvenka the day belongs to.</summary>
    public int? ExcuseId { get; set; }
    public Excuse? Excuse { get; set; }
}
