using Soovioo.Api.Evidence;

namespace Soovioo.Api.Events;

public class Event
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }

    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public bool AllDay { get; set; }

    /// <summary>"#rrggbb", nullable so pre-existing rows (and the ICS feed,
    /// which has no color concept) fall back to the calendar's default.</summary>
    public string? Color { get; set; }

    public int CreatedByUserId { get; set; }

    /// <summary>Who sees this event — see <see cref="EventAudience"/>. For
    /// <see cref="EventAudience.Selected"/>, the actual recipients live in
    /// the four collections below (any combination of them, not just one).</summary>
    public EventAudience Audience { get; set; }

    public ICollection<EventSchoolClass> SchoolClasses { get; set; } = new List<EventSchoolClass>();
    public ICollection<EventTeacher> Teachers { get; set; } = new List<EventTeacher>();
    public ICollection<EventParent> Parents { get; set; } = new List<EventParent>();
    public ICollection<EventStudent> Students { get; set; } = new List<EventStudent>();
}
