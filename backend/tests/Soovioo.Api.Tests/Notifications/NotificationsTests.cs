using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Notifications;

public class NotificationsTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static ControllerContext ContextOf(User user) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
            ], "Test")),
        },
    };

    /// <summary>5.A with teacher "ucitel", pupil Jan with own account "zak"
    /// and parent "rodic"; another teacher and a parent of a pupil in 5.B.</summary>
    private sealed class World
    {
        public required User Teacher, OtherTeacher, Parent, StudentUser, OtherParent;
        public required Student Student, OtherStudent;
        public required SchoolClass Class, OtherClass;
        public required Subject Subject;
    }

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        User NewUser(string name, UserRole role) =>
            new() { Username = name, PasswordHash = "x", Role = role, SchoolId = SchoolId };
        var w = new World
        {
            Teacher = NewUser("ucitel", UserRole.Teacher),
            OtherTeacher = NewUser("ucitel2", UserRole.Teacher),
            Parent = NewUser("rodic", UserRole.Parent),
            StudentUser = NewUser("zak", UserRole.Student),
            OtherParent = NewUser("rodic2", UserRole.Parent),
            Student = new Student { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId },
            OtherStudent = new Student { FirstName = "Eva", LastName = "Malá", SchoolId = SchoolId },
            Class = new SchoolClass { Name = "5.A", SchoolId = SchoolId },
            OtherClass = new SchoolClass { Name = "5.B", SchoolId = SchoolId },
            Subject = new Subject { Name = "Matematika", SchoolId = SchoolId },
        };
        db.Users.AddRange(w.Teacher, w.OtherTeacher, w.Parent, w.StudentUser, w.OtherParent);
        await db.SaveChangesAsync();
        w.Class.ClassTeacherId = w.Teacher.Id;
        db.SchoolClasses.AddRange(w.Class, w.OtherClass);
        db.Subjects.Add(w.Subject);
        await db.SaveChangesAsync();
        w.Student.SchoolClassId = w.Class.Id;
        w.Student.UserId = w.StudentUser.Id;
        w.OtherStudent.SchoolClassId = w.OtherClass.Id;
        db.Students.AddRange(w.Student, w.OtherStudent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(w.Parent.Id, w.Student.Id, SchoolId);
        await db.LinkChildAsync(w.OtherParent.Id, w.OtherStudent.Id, SchoolId);
        await db.SaveChangesAsync();
        return w;
    }

    private static Task<List<Notification>> Of(AppDbContext db, User user) =>
        db.Notifications.Where(n => n.UserId == user.Id).ToListAsync();

    private static GradesController Grades(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)) { ControllerContext = ContextOf(user) };

    private static ConversationsController Conversations(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()),
            new Notifier(db, TimeProvider.System)) { ControllerContext = ContextOf(user) };

    private static NotificationsController Bell(AppDbContext db, User user, TimeProvider? clock = null) =>
        new(db, clock ?? TimeProvider.System) { ControllerContext = ContextOf(user) };

    [Fact]
    public async Task NewGradeReachesPupilAndParentsButNotWhoeverMutedIt()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.StudentUser.MutedNotifications = [NotificationType.Grade];
        await db.SaveChangesAsync();

        await Grades(db, w.Teacher).AddGrade(new CreateGradeRequest(w.Student.Id, w.Subject.Id, 1, 2, null));

        var notification = Assert.Single(await Of(db, w.Parent));
        Assert.Equal(NotificationType.Grade, notification.Type);
        Assert.Equal("Nová známka — Matematika", notification.Title);
        Assert.Equal("Jan Novák: 1 (váha 2)", notification.Body);
        Assert.Equal("grades", notification.Page);
        Assert.Equal(w.Student.Id, notification.StudentId);
        Assert.Empty(await Of(db, w.StudentUser));
        Assert.Empty(await Of(db, w.OtherParent));
        Assert.Empty(await Of(db, w.Teacher));
    }

    [Fact]
    public async Task DeactivatedAccountGetsNothing()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Parent.DeactivatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await Grades(db, w.Teacher).AddGrade(new CreateGradeRequest(w.Student.Id, w.Subject.Id, 2, 1, null));

        Assert.Empty(await Of(db, w.Parent));
        Assert.Single(await Of(db, w.StudentUser));
    }

    [Fact]
    public async Task RepliesUpdateTheUnreadNotificationAndReadingTheConversationReadsIt()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var started = await Conversations(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Ve středu jedeme.", w.Student.Id));
        var id = ((ConversationDto)((CreatedAtActionResult)started.Result!).Value!).Id;

        var first = Assert.Single(await Of(db, w.Parent));
        Assert.Equal("Nová zpráva: Výlet", first.Title);
        Assert.Equal(id, first.TargetId);
        Assert.Empty(await Of(db, w.Teacher));

        await Conversations(db, w.Teacher).Reply(id, new ReplyRequest("Sraz v 7:30."));
        var updated = Assert.Single(await Of(db, w.Parent));
        Assert.EndsWith("Sraz v 7:30.", updated.Body);

        await Conversations(db, w.Parent).MarkRead(id);
        Assert.NotNull((await Of(db, w.Parent)).Single().ReadAt);

        await Conversations(db, w.Teacher).Reply(id, new ReplyRequest("A svačinu."));
        Assert.Equal(2, (await Of(db, w.Parent)).Count);
        // The parent's own reply goes to the teacher, not back to the parent.
        await Conversations(db, w.Parent).Reply(id, new ReplyRequest("Díky."));
        Assert.Single(await Of(db, w.Teacher));
        Assert.Equal(2, (await Of(db, w.Parent)).Count);
    }

    [Fact]
    public async Task ExcuseReviewTellsTheFamily()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var absence = new Absence { StudentId = w.Student.Id, Date = new DateOnly(2026, 10, 5), Periods = [1, 2], SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
            { ControllerContext = ContextOf(w.Teacher) }
            .ReviewAbsence(absence.Id, new ReviewAbsenceRequest(true));

        var notification = Assert.Single(await Of(db, w.Parent));
        Assert.Equal(NotificationType.Excuse, notification.Type);
        Assert.Equal("Omluvenka schválena", notification.Title);
        Assert.Equal("Jan Novák — 5. 10. 2026, hodiny 1, 2", notification.Body);
    }

    [Fact]
    public async Task SelectedEventReachesItsAudienceOnly()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var start = new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

        var result = await new EventsController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
            { ControllerContext = ContextOf(w.Teacher) }
            .CreateEvent(new CreateEventRequest(
                "Exkurze", null, "Muzeum", start, start.AddHours(4), false, EventAudience.Selected,
                SchoolClassIds: [w.Class.Id]));

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var notification = Assert.Single(await Of(db, w.Parent));
        Assert.Equal("Nová akce: Exkurze", notification.Title);
        Assert.Equal("3. 11. 2026 9:00, Muzeum", notification.Body);
        Assert.Single(await Of(db, w.StudentUser));
        Assert.Empty(await Of(db, w.OtherParent));
        Assert.Empty(await Of(db, w.OtherTeacher));
        Assert.Empty(await Of(db, w.Teacher));
    }

    private static async Task<int> UnreadAsync(NotificationsController bell) =>
        Assert.IsType<UnreadCountsDto>(Assert.IsType<OkObjectResult>((await bell.GetUnreadCounts()).Result).Value).Notifications;

    [Fact]
    public async Task UnreadCountsCarryTheMessagesBadgeToo()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        await Conversations(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Ve středu jedeme.", w.Student.Id));

        var counts = Assert.IsType<UnreadCountsDto>(
            Assert.IsType<OkObjectResult>((await Bell(db, w.Parent).GetUnreadCounts()).Result).Value);

        Assert.Equal(new UnreadCountsDto(1, 1), counts);
    }

    [Fact]
    public async Task BellListsPagesCountsAndMarksRead()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var notifier = new Notifier(db, TimeProvider.System);
        for (var i = 0; i < NotificationsController.PageSize + 2; i++)
        {
            await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Homework, $"Úkol {i}", null, "homework");
        }
        await notifier.NotifyAsync(SchoolId, [w.OtherParent.Id], NotificationType.Homework, "Cizí", null, "homework");
        await db.SaveChangesAsync();
        var bell = Bell(db, w.Parent);

        var page = Assert.IsType<NotificationPageDto>(Assert.IsType<OkObjectResult>((await bell.GetNotifications(null)).Result).Value);
        Assert.Equal(NotificationsController.PageSize, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal($"Úkol {NotificationsController.PageSize + 1}", page.Items[0].Title);
        var rest = Assert.IsType<NotificationPageDto>(Assert.IsType<OkObjectResult>((await bell.GetNotifications(page.Items[^1].Id)).Result).Value);
        Assert.Equal(2, rest.Items.Count);
        Assert.False(rest.HasMore);

        Assert.Equal(NotificationsController.PageSize + 2, (await UnreadAsync(bell)));
        await bell.MarkRead(page.Items[0].Id);
        Assert.Equal(NotificationsController.PageSize + 1, (await UnreadAsync(bell)));
        Assert.IsType<NotFoundResult>(await bell.MarkRead((await Of(db, w.OtherParent)).Single().Id));
        await bell.MarkAllRead();
        Assert.Equal(0, (await UnreadAsync(bell)));
        Assert.Null((await Of(db, w.OtherParent)).Single().ReadAt);
    }

    [Fact]
    public async Task SettingsOfferTheKindsForTheRolesAndStoreWhatIsOff()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var teacher = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>((await Bell(db, w.Teacher).GetSettings()).Result).Value);
        Assert.Equal([NotificationType.Message, NotificationType.Event], teacher.Types.Select(t => t.Type));

        var result = await Bell(db, w.Parent).SetSettings(new NotificationSettingsRequest([NotificationType.Event, NotificationType.Event]));
        var parent = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(Enum.GetValues<NotificationType>().Length, parent.Types.Count);
        Assert.False(parent.Types.Single(t => t.Type == NotificationType.Event).Enabled);
        Assert.True(parent.Types.Single(t => t.Type == NotificationType.Grade).Enabled);
        Assert.Equal([NotificationType.Event], (await db.Users.FindAsync(w.Parent.Id))!.MutedNotifications);
    }

    [Fact]
    public async Task CleanupDropsWhatIsOlderThanRetention()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var notifier = new Notifier(db, clock);
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Stará", null, "events");
        await db.SaveChangesAsync();
        clock.Now += NotificationCleanupJob.Retention;
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Nová", null, "events");
        await db.SaveChangesAsync();
        clock.Now += TimeSpan.FromSeconds(1);

        await new NotificationCleanupJob(db, clock).RunAsync();

        Assert.Equal("Nová", (await Of(db, w.Parent)).Single().Title);
    }
}
