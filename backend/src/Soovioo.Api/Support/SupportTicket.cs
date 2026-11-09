using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

public enum SupportTicketStatus
{
    New,

    /// <summary>Has a SuperAdmin taking care of it (<see cref="SupportTicket.AssigneeUserId"/>).</summary>
    Assigned,

    /// <summary>Being worked on right now — also by entering the school.</summary>
    InProgress,

    /// <summary>The support asked the school something; the school's
    /// answer brings it back to <see cref="Assigned"/>.</summary>
    WaitingForSchool,
    Resolved,

    /// <summary>The school withdrew it before it was resolved.</summary>
    Cancelled,
}

/// <summary>A case of support for a school — asked by its admin, or opened
/// by a SuperAdmin. Due by the school's support plan
/// (<see cref="SupportPlanClient"/>), taken care of by one SuperAdmin, talked
/// over in its thread (<see cref="SupportTicketEntry"/>); entering the school
/// is always for a ticket and, with <see cref="SupportAccess.Approval"/>,
/// only while the school allows it on that ticket.</summary>
public class SupportTicket
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 8000;

    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int? CreatedByUserId { get; set; }

    /// <summary>Snapshot of who opened it.</summary>
    public required string CreatedByName { get; set; }

    /// <summary>Opened by a SuperAdmin for the school.</summary>
    public bool CreatedBySupport { get; set; }

    public required string Title { get; set; }

    /// <summary>Markdown.</summary>
    public required string Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The school's plan when it was created — snapshot.</summary>
    public required string PlanName { get; set; }

    /// <summary>Created + the plan's working days (Prague calendar).</summary>
    public DateOnly DueDate { get; set; }

    public SupportTicketStatus Status { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }

    public int? AssigneeUserId { get; set; }

    /// <summary>Snapshot of the SuperAdmin taking care of it.</summary>
    public string? AssigneeName { get; set; }

    /// <summary>The assignee asked to enter the school, not allowed since.</summary>
    public DateTimeOffset? AccessRequestedAt { get; set; }

    /// <summary>Entering is allowed until then (with <see cref="SupportAccess.Approval"/>);
    /// taken back = set to the moment.</summary>
    public DateTimeOffset? AccessUntil { get; set; }

    public bool IsOpen => Status is not (SupportTicketStatus.Resolved or SupportTicketStatus.Cancelled);
}

public enum SupportTicketEntryKind
{
    /// <summary>Between the school and the support — both see it.</summary>
    Message,

    /// <summary>The SuperAdmins' own — the school doesn't see it.</summary>
    Note,
    Created,

    /// <summary>Taken by a SuperAdmin; <see cref="SupportTicketEntry.Text"/>
    /// is whom they took it from, if anyone.</summary>
    Assigned,

    /// <summary>The assignee stepped back; <see cref="SupportTicketEntry.Text"/>
    /// sums up the work so far (internal).</summary>
    Released,

    /// <summary>Given to another SuperAdmin; <see cref="SupportTicketEntry.Text"/>
    /// is their name.</summary>
    HandedOver,
    StatusChanged,
    AccessRequested,
    AccessGranted,
    AccessRevoked,

    /// <summary>Leaving the school ("Opustit") — <see cref="SupportTicketEntry.Text"/>
    /// is what was done.</summary>
    Visit,

    // Not stored: the thread puts the ticket's support sessions
    // (SupportSession) and what was changed in them (the audit log) in.
    SessionStarted,
    SessionAction,

    /// <summary>Ended without leaving — allowing taken back, the ticket closed.</summary>
    SessionEnded,
    SessionExpired,
}

/// <summary>One item of a ticket's thread.</summary>
public class SupportTicketEntry
{
    public int Id { get; set; }

    public int TicketId { get; set; }
    public SupportTicket? Ticket { get; set; }

    public int SchoolId { get; set; }

    public DateTimeOffset At { get; set; }

    public SupportTicketEntryKind Kind { get; set; }

    public int? AuthorUserId { get; set; }

    /// <summary>Snapshot.</summary>
    public required string AuthorName { get; set; }

    /// <summary>Written by a SuperAdmin.</summary>
    public bool BySupport { get; set; }

    /// <summary>Only the SuperAdmins see it.</summary>
    public bool Internal { get; set; }

    /// <summary>Markdown.</summary>
    public string? Text { get; set; }

    /// <summary>With <see cref="SupportTicketEntryKind.StatusChanged"/>
    /// (<see cref="Text"/>: why it was cancelled).</summary>
    public SupportTicketStatus? Status { get; set; }

    /// <summary>With <see cref="SupportTicketEntryKind.AccessGranted"/>.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>A message or note changed afterwards — the last time
    /// (<see cref="Revisions"/>).</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Deleted by its author's side — only they and the
    /// SuperAdmins see it was there.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public List<SupportTicketEntryRevision> Revisions { get; set; } = [];

    /// <summary>With <see cref="SupportTicketEntryKind.Visit"/>, and with
    /// <see cref="SupportTicketEntryKind.StatusChanged"/> the entering made.</summary>
    public int? SessionId { get; set; }
}

public enum SupportTicketEntryChange
{
    Edited,
    Deleted,
    MadeInternal,
    MadePublic,
}

/// <summary>One change of a message or note: who, when, and the text it had
/// before.</summary>
public class SupportTicketEntryRevision
{
    public int Id { get; set; }

    public int EntryId { get; set; }

    public int SchoolId { get; set; }

    public DateTimeOffset At { get; set; }

    public SupportTicketEntryChange Change { get; set; }

    public int? AuthorUserId { get; set; }

    /// <summary>Snapshot.</summary>
    public required string AuthorName { get; set; }

    /// <summary>Markdown — the entry's text before the change.</summary>
    public string? Text { get; set; }
}
