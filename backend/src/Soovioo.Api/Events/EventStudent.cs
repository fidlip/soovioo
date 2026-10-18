using Soovioo.Api.Evidence;

namespace Soovioo.Api.Events;

/// <summary>One student explicitly targeted as a recipient of an
/// <see cref="EventAudience.Selected"/> event — grants visibility to that
/// student's linked parent(s) via <see cref="ParentStudent"/> (students have
/// no login of their own yet; see <see cref="EventVisibilityExtensions"/>).</summary>
public class EventStudent
{
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
