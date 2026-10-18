using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Events;

/// <summary>One Parent-role user explicitly targeted as a recipient of an
/// <see cref="EventAudience.Selected"/> event.</summary>
public class EventParent
{
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
