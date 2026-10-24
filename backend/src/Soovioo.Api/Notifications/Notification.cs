using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Notifications;

public enum NotificationType
{
    Grade,
    Message,
    Absence,

    /// <summary>An excuse was approved or rejected.</summary>
    Excuse,
    Homework,
    Event,

    /// <summary>A pupil's photo waits for a parent's approval.</summary>
    Photo,
}

/// <summary>One item under the bell — something new for one user, with a
/// link to the page (and the thing) it's about.</summary>
public class Notification
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public NotificationType Type { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>The page of the app it opens, e.g. <c>grades</c> — the
    /// school's routes without the school part.</summary>
    public required string Page { get; set; }

    /// <summary>The row it's about on that page (a conversation, an event…).
    /// A newer notification about the same one replaces an unread one.</summary>
    public int? TargetId { get; set; }

    /// <summary>The pupil it's about — erasing the pupil takes it along.</summary>
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
