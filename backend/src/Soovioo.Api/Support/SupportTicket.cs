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
    Assigned,

    /// <summary>The assignee stepped back; <see cref="SupportTicketEntry.Text"/>
    /// sums up the work so far (internal).</summary>
    Released,
    StatusChanged,
    AccessRequested,
    AccessGranted,
    AccessRevoked,

    /// <summary>A support session — <see cref="SupportTicketEntry.Text"/> is
    /// what was done, written on leaving.</summary>
    Visit,
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

    /// <summary>With <see cref="SupportTicketEntryKind.StatusChanged"/>.</summary>
    public SupportTicketStatus? Status { get; set; }

    /// <summary>With <see cref="SupportTicketEntryKind.AccessGranted"/>.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>With <see cref="SupportTicketEntryKind.Visit"/>.</summary>
    public int? SessionId { get; set; }
}
