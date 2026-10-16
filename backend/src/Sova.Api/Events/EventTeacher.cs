using Sova.Api.Auth;
using Sova.Api.Evidence;

namespace Sova.Api.Events;

/// <summary>One Teacher-role user explicitly targeted as a recipient of an
/// <see cref="EventAudience.Selected"/> event. Admin-only to set — a Teacher
/// creating an event can target classes/students/parents but never other
/// teachers, see <see cref="EventsController"/>'s write-side validation.</summary>
public class EventTeacher
{
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
