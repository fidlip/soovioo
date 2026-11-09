using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
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
            User = TestRoles.PrincipalOf(user),
        },
    };

    /// <summary>5.A with teacher "ucitel", pupil Jan with own account "zak"
    /// and parent "rodic"; another teacher and a parent of a pupil in 5.B.</summary>
    private sealed class World
    {
        public required User Teacher, OtherTeacher, Parent, StudentUser, OtherParent;
        public required StudentProfile Student, OtherStudent;
        public required SchoolClass Class, OtherClass;
        public required Subject Subject;
    }

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        User NewUser(string name, UserRole role) =>
            new() { Username = name, PasswordHash = "x", ActiveRole = db.Role(role, SchoolId), SchoolId = SchoolId };
        var w = new World
        {
            Teacher = NewUser("ucitel", UserRole.Teacher),
            OtherTeacher = NewUser("ucitel2", UserRole.Teacher),
            Parent = NewUser("rodic", UserRole.Parent),
            StudentUser = NewUser("zak", UserRole.Student),
            OtherParent = NewUser("rodic2", UserRole.Parent),
            Student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolId = SchoolId },
            OtherStudent = new StudentProfile { Person = new Person { FirstName = "Eva", LastName = "Malá", SchoolId = SchoolId }, SchoolId = SchoolId },
            Class = new SchoolClass { Name = "5.A", SchoolId = SchoolId },
            OtherClass = new SchoolClass { Name = "5.B", SchoolId = SchoolId },
            Subject = new Subject { Name = "Matematika", SchoolId = SchoolId },
        };
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola" });
        db.Users.AddRange(w.Teacher, w.OtherTeacher, w.Parent, w.StudentUser, w.OtherParent);
        await db.SaveChangesAsync();
        w.Class.ClassTeacherId = w.Teacher.Id;
        db.SchoolClasses.AddRange(w.Class, w.OtherClass);
        db.Subjects.Add(w.Subject);
        await db.SaveChangesAsync();
        w.Student.SchoolClassId = w.Class.Id;
        w.Student.Person!.UserId = w.StudentUser.Id;
        w.OtherStudent.SchoolClassId = w.OtherClass.Id;
        db.Students.AddRange(w.Student, w.OtherStudent);
        await db.SaveChangesAsync();
        db.Teaches(w.Teacher.Id, w.Class.Id, w.Subject.Id);
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
        Assert.Equal("Jan: 1 (váha 2)", notification.Body);
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
        var excuse = new Excuse { StudentId = w.Student.Id, DateFrom = new DateOnly(2026, 10, 5), DateTo = new DateOnly(2026, 10, 5), Reason = "Nemoc", SchoolId = SchoolId };
        var absence = new Absence
        {
            StudentId = w.Student.Id, Date = new DateOnly(2026, 10, 5), Periods = [1, 2], SchoolId = SchoolId,
            Status = AbsenceStatus.PendingExcuse, Excuse = excuse,
        };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System), TimeProvider.System,
            new Soovioo.Api.Messaging.MessageAttachmentStorageService(new FakeWebHostEnvironment()))
            { ControllerContext = ContextOf(w.Teacher) }
            .ReviewAbsence(absence.Id, new ReviewAbsenceRequest(true));

        var notification = Assert.Single(await Of(db, w.Parent));
        Assert.Equal(NotificationType.Excuse, notification.Type);
        Assert.Equal("Omluvenka schválena", notification.Title);
        Assert.Equal("Jan — 5. 10. 2026", notification.Body);
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

    private static async Task<User> AdminAsync(AppDbContext db)
    {
        var admin = new User { Username = "reditel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, SchoolId), SchoolId = SchoolId };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    [Fact]
    public async Task EditedGradeReplacesItsNotificationReadOrNotAndSavingItUnchangedDoesNothing()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var created = await Grades(db, w.Teacher).AddGrade(new CreateGradeRequest(w.Student.Id, w.Subject.Id, 3, 1, null));
        var grade = (GradeDto)((CreatedAtActionResult)created.Result!).Value!;
        var first = Assert.Single(await Of(db, w.Parent));
        await Bell(db, w.Parent).MarkRead(first.Id);

        await Grades(db, w.Teacher).UpdateGrade(grade.Id, new CreateGradeRequest(w.Student.Id, w.Subject.Id, 2, 1, null, grade.GradedOn));

        var changed = Assert.Single(await Of(db, w.Parent));
        Assert.True(changed.Id > first.Id);
        Assert.Equal("Změna známky — Matematika", changed.Title);
        Assert.Equal("Jan: 2", changed.Body);
        Assert.Null(changed.ReadAt);
        Assert.Equal(grade.Id, changed.TargetId);
        Assert.Equal("Změna známky — Matematika", Assert.Single(await Of(db, w.StudentUser)).Title);

        await Grades(db, w.Teacher).UpdateGrade(grade.Id, new CreateGradeRequest(w.Student.Id, w.Subject.Id, 2, 1, null, grade.GradedOn));
        Assert.Equal(changed.Id, Assert.Single(await Of(db, w.Parent)).Id);
    }

    [Fact]
    public async Task GradeGivenToAnotherPupilLeavesTheFamilyItNoLongerConcerns()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var admin = await AdminAsync(db);
        var created = await Grades(db, admin).AddGrade(new CreateGradeRequest(w.Student.Id, w.Subject.Id, 1, 1, null));
        var grade = (GradeDto)((CreatedAtActionResult)created.Result!).Value!;

        await Grades(db, admin).UpdateGrade(grade.Id, new CreateGradeRequest(w.OtherStudent.Id, w.Subject.Id, 1, 1, null));

        Assert.Empty(await Of(db, w.Parent));
        Assert.Empty(await Of(db, w.StudentUser));
        var theirs = Assert.Single(await Of(db, w.OtherParent));
        Assert.Equal("Nová známka — Matematika", theirs.Title);
        Assert.Equal("Eva: 1", theirs.Body);
        Assert.Equal(w.OtherStudent.Id, theirs.StudentId);
    }

    [Fact]
    public async Task GradeOpensOnlyWhileItIsThereAndTheCallerSeesIt()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var created = await Grades(db, w.Teacher).AddGrade(new CreateGradeRequest(w.Student.Id, w.Subject.Id, 1, 1, null));
        var id = ((GradeDto)((CreatedAtActionResult)created.Result!).Value!).Id;

        Assert.Equal(1, Assert.IsType<GradeDto>(Assert.IsType<OkObjectResult>((await Grades(db, w.Parent).GetGrade(id)).Result).Value).Value);
        Assert.IsType<NotFoundResult>((await Grades(db, w.OtherParent).GetGrade(id)).Result);
        await Grades(db, w.Teacher).DeleteGrade(id);
        Assert.IsType<NotFoundResult>((await Grades(db, w.Parent).GetGrade(id)).Result);
    }

    [Fact]
    public async Task EditedAbsenceReplacesItsNotification()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var absences = new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System), TimeProvider.System,
            new Soovioo.Api.Messaging.MessageAttachmentStorageService(new FakeWebHostEnvironment()))
            { ControllerContext = ContextOf(w.Teacher) };
        var created = await absences.AddAbsence(new CreateAbsenceRequest(w.Student.Id, new DateOnly(2026, 10, 5), [1]));
        var id = ((AbsenceDto)((CreatedAtActionResult)created.Result!).Value!).Id;
        Assert.Single(await Of(db, w.Parent));

        await absences.UpdateAbsence(id, new CreateAbsenceRequest(w.Student.Id, new DateOnly(2026, 10, 6), [1]));

        var changed = Assert.Single(await Of(db, w.Parent));
        Assert.Equal(NotificationType.Absence, changed.Type);
        Assert.Equal("Změna absence", changed.Title);
        Assert.Equal("Jan — 6. 10. 2026, hodiny 1", changed.Body);
    }

    [Fact]
    public async Task HomeworkMovedToAnotherClassLeavesTheOldOneAndIsNewToTheOther()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var admin = await AdminAsync(db);
        var homework = new Soovioo.Api.Homework.HomeworkController(
            db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()),
            new Notifier(db, TimeProvider.System)) { ControllerContext = ContextOf(admin) };
        var due = new DateOnly(2026, 11, 20);
        var created = await homework.CreateHomework(new Soovioo.Api.Homework.CreateHomeworkRequest(w.Class.Id, w.Subject.Id, "Str. 12", due, false));
        var id = ((Soovioo.Api.Homework.HomeworkDto)((CreatedAtActionResult)created.Result!).Value!).Id;
        Assert.Equal("Nový domácí úkol — Matematika", Assert.Single(await Of(db, w.Parent)).Title);

        await homework.UpdateHomework(id, new Soovioo.Api.Homework.CreateHomeworkRequest(w.Class.Id, w.Subject.Id, "Str. 13", due, false));
        var changed = Assert.Single(await Of(db, w.Parent));
        Assert.Equal("Změna úkolu — Matematika", changed.Title);
        Assert.Equal("Str. 13 (do 20. 11. 2026)", changed.Body);

        await homework.UpdateHomework(id, new Soovioo.Api.Homework.CreateHomeworkRequest(w.OtherClass.Id, w.Subject.Id, "Str. 13", due, false));
        Assert.Empty(await Of(db, w.Parent));
        Assert.Empty(await Of(db, w.StudentUser));
        Assert.Equal("Nový domácí úkol — Matematika", Assert.Single(await Of(db, w.OtherParent)).Title);
    }

    [Fact]
    public async Task EditedEventReachesItsNewAudienceAndLeavesTheOneLeftOut()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var admin = await AdminAsync(db);
        var start = new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
        CreateEventRequest Request(string title, params int[] classIds) => new(
            title, null, null, start, start.AddHours(4), false, EventAudience.Selected, SchoolClassIds: classIds);
        EventsController Events(User user) => new(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
            { ControllerContext = ContextOf(user) };
        var created = await Events(w.Teacher).CreateEvent(Request("Exkurze", w.Class.Id));
        var id = ((EventDto)((CreatedAtActionResult)created.Result!).Value!).Id;
        var first = Assert.Single(await Of(db, w.Parent));

        await Events(w.Teacher).UpdateEvent(id, Request("Exkurze", w.Class.Id));
        Assert.Equal(first.Id, Assert.Single(await Of(db, w.Parent)).Id);

        await Events(admin).UpdateEvent(id, Request("Exkurze do muzea", w.OtherClass.Id));
        Assert.Empty(await Of(db, w.Parent));
        Assert.Equal("Nová akce: Exkurze do muzea", Assert.Single(await Of(db, w.OtherParent)).Title);

        await Events(admin).UpdateEvent(id, Request("Exkurze do muzea", w.Class.Id, w.OtherClass.Id));
        Assert.Equal("Změna akce: Exkurze do muzea", Assert.Single(await Of(db, w.OtherParent)).Title);
        Assert.Equal("Nová akce: Exkurze do muzea", Assert.Single(await Of(db, w.Parent)).Title);
        Assert.Empty(await Of(db, admin));
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
        var unread = Assert.IsType<NotificationPageDto>(Assert.IsType<OkObjectResult>((await bell.GetNotifications(null, unread: true)).Result).Value);
        Assert.Equal($"Úkol {NotificationsController.PageSize}", unread.Items[0].Title);
        Assert.All(unread.Items, n => Assert.False(n.Read));
        var unreadRest = Assert.IsType<NotificationPageDto>(Assert.IsType<OkObjectResult>((await bell.GetNotifications(unread.Items[^1].Id, unread: true)).Result).Value);
        Assert.Single(unreadRest.Items);
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
        Assert.Equal([NotificationType.Message, NotificationType.Event, NotificationType.TimetableChange, NotificationType.Substitution, NotificationType.NewExcuse, NotificationType.Nickname], teacher.Types.Select(t => t.Type));
        var admin = new User { Username = "reditel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, SchoolId), SchoolId = SchoolId };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var adminSettings = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>((await Bell(db, admin).GetSettings()).Result).Value);
        Assert.Equal([NotificationType.Message, NotificationType.Event, NotificationType.TimetableChange, NotificationType.Substitution, NotificationType.NewExcuse, NotificationType.Nickname, NotificationType.Timetable], adminSettings.Types.Select(t => t.Type));

        var result = await Bell(db, w.Parent).SetSettings(new NotificationSettingsRequest([NotificationType.Event, NotificationType.Event]));
        var parent = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(Enum.GetValues<NotificationType>().Length - 2, parent.Types.Count);
        Assert.DoesNotContain(parent.Types, t => t.Type == NotificationType.Timetable);
        Assert.DoesNotContain(parent.Types, t => t.Type == NotificationType.NewExcuse);
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

    private static async Task GiveEmailAsync(AppDbContext db, User user, string email)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (person is null)
        {
            person = new Person { SchoolId = SchoolId, UserId = user.Id };
            db.Persons.Add(person);
        }
        person.Email = email;
        await db.SaveChangesAsync();
    }

    private static NotificationDigestJob Digest(AppDbContext db, TimeProvider clock, IEmailSender email) =>
        new(db, email, Options.Create(new AppLinkOptions { PathBasedTenant = true, FrontendBaseUrl = "http://app" }),
            clock, NullLogger<NotificationDigestJob>.Instance);

    /// <summary>17:00 in Prague on 2026-10-26 (winter time, UTC+1).</summary>
    private static readonly DateTimeOffset SchoolDigest = new(2026, 10, 26, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OffUnderTheBellButOnInTheEmailIsKeptOnlyForTheEmail()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Parent.MutedNotifications = [NotificationType.Event];
        w.OtherParent.MutedNotifications = [NotificationType.Event];
        w.StudentUser.MutedNotifications = [NotificationType.Event];
        w.StudentUser.MutedEmailNotifications = [NotificationType.Event];
        await db.SaveChangesAsync();
        await GiveEmailAsync(db, w.Parent, "rodic@example.com");
        await GiveEmailAsync(db, w.StudentUser, "zak@example.com");
        var notifier = new Notifier(db, TimeProvider.System);

        await notifier.NotifyAsync(SchoolId, [w.Parent.Id, w.OtherParent.Id, w.StudentUser.Id], NotificationType.Event, "Výlet", null, "events");
        await db.SaveChangesAsync();

        Assert.False(Assert.Single(await Of(db, w.Parent)).InApp);
        // No e-mail address to send it to.
        Assert.Empty(await Of(db, w.OtherParent));
        Assert.Empty(await Of(db, w.StudentUser));
        var bell = Bell(db, w.Parent);
        var page = Assert.IsType<NotificationPageDto>(Assert.IsType<OkObjectResult>((await bell.GetNotifications(null)).Result).Value);
        Assert.Empty(page.Items);
        var counts = Assert.IsType<UnreadCountsDto>(Assert.IsType<OkObjectResult>((await bell.GetUnreadCounts()).Result).Value);
        Assert.Equal(0, counts.Notifications);
    }

    [Fact]
    public async Task SchoolForcedEmailComesEvenToWhoeverSwitchedItOff()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        (await db.Schools.FindAsync(SchoolId))!.ForcedEmailNotifications = [NotificationType.Event];
        w.Parent.MutedNotifications = [NotificationType.Event];
        w.Parent.MutedEmailNotifications = [NotificationType.Event];
        await db.SaveChangesAsync();
        await GiveEmailAsync(db, w.Parent, "rodic@example.com");

        await new Notifier(db, TimeProvider.System).NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Výlet", null, "events");
        await db.SaveChangesAsync();

        Assert.False(Assert.Single(await Of(db, w.Parent)).InApp);
        var settings = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>((await Bell(db, w.Parent).GetSettings()).Result).Value);
        var events = settings.Types.Single(t => t.Type == NotificationType.Event);
        Assert.True(events.Email);
        Assert.True(events.EmailForced);
        Assert.True(settings.HasEmail);
    }

    [Fact]
    public async Task DailyEmailGoesAtTheSchoolsTimeOnceWithWhatIsNewAndUnread()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        await GiveEmailAsync(db, w.Parent, "rodic@example.com");
        var clock = new FakeClock(SchoolDigest - TimeSpan.FromHours(3));
        var notifier = new Notifier(db, clock);
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Grade, "Nová známka — Matematika", "Jan Novák: 1", "grades");
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Message, "Nová zpráva: Výlet", "Učitelka: <b>středa</b>", "messages", targetId: 7);
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Přečtená akce", null, "events");
        await db.SaveChangesAsync();
        (await db.Notifications.SingleAsync(n => n.Title == "Přečtená akce")).ReadAt = clock.Now;
        await db.SaveChangesAsync();
        var mailer = new FakeEmailSender();

        clock.Now = SchoolDigest - TimeSpan.FromMinutes(1);
        await Digest(db, clock, mailer).RunAsync();
        Assert.Empty(mailer.Sent);

        clock.Now = SchoolDigest + TimeSpan.FromMinutes(5);
        await Digest(db, clock, mailer).RunAsync();
        var (to, subject, body) = Assert.Single(mailer.Sent);
        Assert.Equal("rodic@example.com", to);
        Assert.Equal("2 nová upozornění — ZŠ Lipová", subject);
        Assert.Contains("""<a href="http://app/skola/grades">Nová známka — Matematika</a>""", body);
        Assert.Contains("""<a href="http://app/skola/messages?conversation=7">""", body);
        Assert.Contains("Učitelka: &lt;b&gt;středa&lt;/b&gt;", body);
        Assert.DoesNotContain("Přečtená akce", body);
        var token = (await db.Users.FindAsync(w.Parent.Id))!.EmailUnsubscribeToken;
        Assert.NotNull(token);
        Assert.Contains($"http://app/skola/unsubscribe?token={token}", body);

        clock.Now += TimeSpan.FromMinutes(15);
        await Digest(db, clock, mailer).RunAsync();
        Assert.Single(mailer.Sent);

        // The next day: only what came after.
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Homework, "Nový úkol", null, "homework");
        await db.SaveChangesAsync();
        clock.Now += TimeSpan.FromDays(1);
        await Digest(db, clock, mailer).RunAsync();
        Assert.Equal(2, mailer.Sent.Count);
        Assert.Equal("1 nové upozornění — ZŠ Lipová", mailer.Sent[1].Subject);
        Assert.DoesNotContain("Nová známka", mailer.Sent[1].HtmlBody);
    }

    [Fact]
    public async Task DailyEmailLeavesOutKindsSwitchedOffAndGoesAtThePersonsOwnTime()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        await GiveEmailAsync(db, w.Parent, "rodic@example.com");
        await Bell(db, w.Parent).SetEmailSettings(new NotificationEmailSettingsRequest([NotificationType.Grade], new TimeOnly(19, 30, 12)));
        var clock = new FakeClock(SchoolDigest - TimeSpan.FromHours(1));
        var notifier = new Notifier(db, clock);
        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Grade, "Nová známka", null, "grades");
        await db.SaveChangesAsync();
        var mailer = new FakeEmailSender();

        clock.Now = SchoolDigest + TimeSpan.FromHours(3);
        await Digest(db, clock, mailer).RunAsync();
        // Only the grade, which stays under the bell but not in the e-mail.
        Assert.Empty(mailer.Sent);
        Assert.True(Assert.Single(await Of(db, w.Parent)).InApp);

        await notifier.NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Výlet", null, "events");
        await db.SaveChangesAsync();
        clock.Now = SchoolDigest.AddDays(1) + TimeSpan.FromHours(2);
        await Digest(db, clock, mailer).RunAsync();
        Assert.Empty(mailer.Sent);
        clock.Now = SchoolDigest.AddDays(1) + TimeSpan.FromHours(2.5);
        await Digest(db, clock, mailer).RunAsync();
        Assert.Contains("Výlet", Assert.Single(mailer.Sent).HtmlBody);
        Assert.Equal(new TimeOnly(19, 30), (await db.Users.FindAsync(w.Parent.Id))!.DigestTime);
    }

    [Fact]
    public async Task DemoSchoolsDailyEmailIsOnlyPreviewed()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        (await db.Schools.FindAsync(SchoolId))!.IsDemo = true;
        await db.SaveChangesAsync();
        await GiveEmailAsync(db, w.Parent, "rodic@example.com");
        var clock = new FakeClock(SchoolDigest - TimeSpan.FromHours(1));
        await new Notifier(db, clock).NotifyAsync(SchoolId, [w.Parent.Id], NotificationType.Event, "Výlet", null, "events");
        await db.SaveChangesAsync();
        var mailer = new FakeEmailSender();

        clock.Now = SchoolDigest;
        await Digest(db, clock, mailer).RunAsync();

        Assert.Empty(mailer.Sent);
        var preview = Assert.Single(await db.DemoEmails.ToListAsync());
        Assert.Equal("rodic@example.com", preview.To);
        Assert.Equal("1 nové upozornění — ZŠ Lipová", preview.Subject);
    }

    [Fact]
    public async Task UnsubscribeLinkSwitchesOffEveryEmailTheSchoolDoesNotForce()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        (await db.Schools.FindAsync(SchoolId))!.ForcedEmailNotifications = [NotificationType.Absence];
        w.Parent.EmailUnsubscribeToken = "ABC";
        await db.SaveChangesAsync();
        var anonymous = new NotificationsController(db, TimeProvider.System)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<NotFoundResult>((await anonymous.Unsubscribe(new UnsubscribeRequest("XYZ"))).Result);
        var result = Assert.IsType<UnsubscribeResultDto>(
            Assert.IsType<OkObjectResult>((await anonymous.Unsubscribe(new UnsubscribeRequest("ABC"))).Result).Value);

        Assert.Equal([NotificationType.Absence], result.Forced);
        var parent = (await db.Users.FindAsync(w.Parent.Id))!;
        Assert.Equal(Enum.GetValues<NotificationType>(), parent.MutedEmailNotifications);
        Assert.Empty(parent.MutedNotifications);
    }
}
