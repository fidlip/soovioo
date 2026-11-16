namespace Soovioo.Api.Support;

public enum SupportChangeOperation
{
    Added,
    Modified,
    Deleted,

    /// <summary>A stored file the session deleted — kept on disk until the
    /// journal goes (<see cref="SupportChangeCleanupJob"/>), so a reset can
    /// bring its row back; <see cref="SupportChange.KeyJson"/> is its path.</summary>
    FileDeleted,
}

/// <summary>The journal of support mode: one row changed in a support
/// session (<see cref="SupportJournal"/>), with its values before and
/// after — what a reset (<see cref="SupportReset"/>) returns. Holds
/// personal data: deleted with the files waiting for it at the end of the
/// next working day after the ticket is closed or cancelled.</summary>
public class SupportChange
{
    public long Id { get; set; }

    public int SessionId { get; set; }

    public int TicketId { get; set; }

    public int SchoolId { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The EF entity type's name.</summary>
    public required string EntityType { get; set; }

    /// <summary>The primary key, {"Id": 5}.</summary>
    public required string KeyJson { get; set; }

    public SupportChangeOperation Operation { get; set; }

    /// <summary>Every property before; null when added.</summary>
    public string? OriginalJson { get; set; }

    /// <summary>Every property after; null when deleted.</summary>
    public string? CurrentJson { get; set; }

    /// <summary>Returned (or left as it was) by a reset.</summary>
    public DateTimeOffset? ResetAt { get; set; }
}
