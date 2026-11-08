namespace Soovioo.Api.Notifications;

/// <summary>Where a notification leads: the school's page with what opens
/// the thing on it — the same for the bell, the daily e-mail and the
/// mobile app.</summary>
public static class NotificationLinks
{
    /// <summary>Without the school part, e.g. <c>grades?grade=12</c>.</summary>
    public static string For(Notification notification)
    {
        if (notification.TargetId is not { } id)
        {
            return notification.Page;
        }
        var student = notification.StudentId is { } studentId ? $"&student={studentId}" : "";
        return notification.Type switch
        {
            NotificationType.Message => $"messages?conversation={id}",
            NotificationType.Grade => $"grades?grade={id}",
            NotificationType.Absence => $"absences?absence={id}",
            NotificationType.Excuse or NotificationType.NewExcuse => $"absences?excuse={id}",
            NotificationType.Homework => $"homework?homework={id}",
            NotificationType.Event => $"events?event={id}",
            NotificationType.Photo => $"my-children?student={id}",
            NotificationType.Timetable => $"admin?group=teaching&section=timetable&version={id}&generation=result",
            NotificationType.TimetableChange => $"timetable?version={id}&change=start{student}",
            NotificationType.Substitution => $"timetable?day={id / 10000:D4}-{id / 100 % 100:D2}-{id % 100:D2}{student}",
            _ => notification.Page,
        };
    }
}
