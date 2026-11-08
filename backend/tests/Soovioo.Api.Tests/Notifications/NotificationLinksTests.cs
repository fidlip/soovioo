using Soovioo.Api.Notifications;

namespace Soovioo.Api.Tests.Notifications;

public class NotificationLinksTests
{
    [Theory]
    [InlineData(NotificationType.Message, "messages", 7, null, "messages?conversation=7")]
    [InlineData(NotificationType.Grade, "grades", 7, 3, "grades?grade=7")]
    [InlineData(NotificationType.Absence, "absences", 7, 3, "absences?absence=7")]
    [InlineData(NotificationType.Excuse, "absences", 7, 3, "absences?excuse=7")]
    [InlineData(NotificationType.NewExcuse, "absences", 7, 3, "absences?excuse=7")]
    [InlineData(NotificationType.Homework, "homework", 7, 3, "homework?homework=7")]
    [InlineData(NotificationType.Event, "events", 7, null, "events?event=7")]
    [InlineData(NotificationType.Photo, "my-children", 3, 3, "my-children?student=3")]
    [InlineData(NotificationType.Timetable, "timetable", 7, null,
        "admin?group=teaching&section=timetable&version=7&generation=result")]
    [InlineData(NotificationType.TimetableChange, "timetable", 7, 3, "timetable?version=7&change=start&student=3")]
    [InlineData(NotificationType.TimetableChange, "timetable", 7, null, "timetable?version=7&change=start")]
    [InlineData(NotificationType.Substitution, "timetable", 20261109, 3, "timetable?day=2026-11-09&student=3")]
    [InlineData(NotificationType.Grade, "grades", null, 3, "grades")]
    public void LeadsToTheThingOnItsPage(NotificationType type, string page, int? targetId, int? studentId, string link)
    {
        var notification = new Notification
        {
            Type = type, Title = "", Page = page, TargetId = targetId, StudentId = studentId,
        };

        Assert.Equal(link, NotificationLinks.For(notification));
    }
}
