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

    /// <summary>A timetable the user started generating is done.</summary>
    Timetable,

    /// <summary>A published version changes the user's (or their child's) lessons.</summary>
    TimetableChange,

    /// <summary>A published day changes the user's (or their child's) lessons.</summary>
    Substitution,
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
    public StudentProfile? Student { get; set; }

    /// <summary>Shown under the bell. False = the user switched the kind
    /// off there and it's only for the daily e-mail.</summary>
    public bool InApp { get; set; } = true;

    /// <summary>Goes to the user's phones as a push (<see cref="PushJob"/>).</summary>
    public bool Push { get; set; }

    /// <summary>When the push went out (or was given up on); null while it
    /// waits — for the next run, or for the end of the night's quiet.</summary>
    public DateTimeOffset? PushedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
