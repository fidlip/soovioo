using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

/// <summary>When a SuperAdmin may enter the school in support mode — the
/// school's choice (<see cref="School.SupportAccess"/>). Entering is always
/// for a <see cref="SupportTicket"/>.</summary>
public enum SupportAccess
{
    Always,

    /// <summary>While the ticket's entering is allowed (<see cref="SupportTicket.AccessUntil"/>).</summary>
    Approval,
    Never,
}

/// <summary>Bound from the <c>Support</c> config section.</summary>
public class SupportOptions
{
    /// <summary>How long allowing entering for a ticket lasts.</summary>
    public int ApprovalMinutes { get; set; } = 24 * 60;

    public TimeSpan ApprovalLength => TimeSpan.FromMinutes(ApprovalMinutes);
}
