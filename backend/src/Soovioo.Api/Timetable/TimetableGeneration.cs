namespace Soovioo.Api.Timetable;

/// <summary>One run of <see cref="TimetableGenerator"/> on a draft version,
/// with what it placed and what didn't fit.</summary>
public class TimetableGeneration
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public TimetableGenerationMode Mode { get; set; }
    public TimetableGenerationStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Lessons added to the version.</summary>
    public int Placed { get; set; }

    /// <summary>What didn't fit or couldn't be done, "5.A M: 1 h" — to fix
    /// before generating again.</summary>
    public List<string> Messages { get; set; } = [];

    /// <summary>How the result looks: class gaps, teachers' gaps…</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>The best timetable so far while it runs.</summary>
    public TimetableGenerator.Progress? Progress { get; set; }
}

public enum TimetableGenerationMode
{
    /// <summary>Keeps the version's lessons and adds what the plan misses.</summary>
    Fill,

    /// <summary>Keeps only the locked lessons and places the rest again.</summary>
    All,
}

public enum TimetableGenerationStatus
{
    Queued,
    Running,
    Done,
    Failed,
}
