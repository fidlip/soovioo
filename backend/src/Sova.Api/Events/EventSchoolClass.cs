using Sova.Api.Evidence;

namespace Sova.Api.Events;

/// <summary>One class targeted as a recipient of an
/// <see cref="EventAudience.Selected"/> event — grants visibility to the
/// parents of every student in that class (see <see cref="EventVisibilityExtensions"/>).</summary>
public class EventSchoolClass
{
    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
