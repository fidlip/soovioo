namespace Soovioo.Api.Support;

/// <summary>A support ticket's status as the backoffice has it.</summary>
public enum SupportTicketStatus
{
    New,

    /// <summary>Has an operator taking care of it.</summary>
    Assigned,

    /// <summary>Being worked on right now — also by entering the school.</summary>
    InProgress,

    /// <summary>The support asked the school something; the school's answer brings it back.</summary>
    WaitingForSchool,

    /// <summary>Done by the support — the school confirms it (<see cref="Closed"/>)
    /// or returns it; closed by itself later.</summary>
    Resolved,

    /// <summary>The school withdrew it before it was resolved.</summary>
    Cancelled,

    /// <summary>The school confirmed it resolved, or nobody returned it in time.</summary>
    Closed,
}

/// <summary>What the school's instance puts into a ticket's thread; the rest
/// of the thread is the backoffice's.</summary>
public enum SupportTicketEntryKind
{
    /// <summary>Leaving the school ("Opustit") — <see cref="SupportTicketEntry.Text"/>
    /// is what was done.</summary>
    Visit,

    /// <summary>The support's changes returned (<see cref="SupportReset"/>) —
    /// <see cref="SupportTicketEntry.Text"/> says how it went;
    /// <see cref="SupportTicketEntry.SessionId"/> when of one session.</summary>
    Reset,

    // Not stored: the thread puts the ticket's support sessions
    // (SupportSession) and what was changed in them (the audit log) in.
    SessionStarted,
    SessionAction,

    /// <summary>Ended without leaving — allowing taken back, the ticket closed.</summary>
    SessionEnded,
    SessionExpired,
}

/// <summary>One item of a ticket's thread (<see cref="SupportTicketEntryKind"/>)
/// kept by the school's instance; <see cref="TicketId"/> is the backoffice's.</summary>
public class SupportTicketEntry
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int SchoolId { get; set; }

    public DateTimeOffset At { get; set; }

    public SupportTicketEntryKind Kind { get; set; }

    /// <summary>Snapshot.</summary>
    public required string AuthorName { get; set; }

    /// <summary>Markdown.</summary>
    public string? Text { get; set; }

    /// <summary>With <see cref="SupportTicketEntryKind.Visit"/>, and with a
    /// <see cref="SupportTicketEntryKind.Reset"/> of one session.</summary>
    public int? SessionId { get; set; }
}
