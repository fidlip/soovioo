using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Events;

public class EventsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static EventsController NewController(AppDbContext db, int currentUserId, string role)
    {
        var principal = TestRoles.Principal(db, currentUserId, Enum.Parse<UserRole>(role), SchoolId, "tester");
        return new EventsController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    [Fact]
    public async Task CreateEventAssignsSchoolIdAndCreator()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Admin")
            .CreateEvent(new CreateEventRequest(
                "Školní výlet", "Celodenní výlet do zoo", null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 16, 0, 0, TimeSpan.Zero),
                AllDay: false, Audience: EventAudience.Everyone));

        var dto = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Školní výlet", dto.Title);
        var saved = await db.Events.FindAsync(dto.Id);
        Assert.NotNull(saved);
        Assert.Equal(SchoolId, saved!.SchoolId);
        Assert.Equal(7, saved.CreatedByUserId);
    }

    [Fact]
    public async Task CreateEventAcceptsAndReturnsValidColor()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Admin")
            .CreateEvent(new CreateEventRequest(
                "Zelená akce", null, null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                AllDay: false, Audience: EventAudience.Everyone, Color: "#1ABC9C"));

        var dto = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("#1ABC9C", dto.Color);
    }

    [Fact]
    public async Task CreateEventRejectsMalformedColor()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Admin")
            .CreateEvent(new CreateEventRequest(
                "Špatná barva", null, null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                AllDay: false, Audience: EventAudience.Everyone, Color: "red"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateEventRejectsEndBeforeStart()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Admin")
            .CreateEvent(new CreateEventRequest(
                "Neplatná akce", null, null,
                new DateTimeOffset(2026, 5, 1, 16, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                AllDay: false, Audience: EventAudience.Everyone));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetEventsFiltersByDateRange()
    {
        using var db = NewDb();
        db.Events.AddRange(
            new Event
            {
                SchoolId = SchoolId,
                Title = "Akce v květnu",
                StartAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                Audience = EventAudience.Everyone,
            },
            new Event
            {
                SchoolId = SchoolId,
                Title = "Akce v červnu",
                StartAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                Audience = EventAudience.Everyone,
            });
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Teacher").GetEvents(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
        Assert.Equal("Akce v červnu", events.Single().Title);
    }

    [Fact]
    public async Task DeleteEventRemovesIt()
    {
        using var db = NewDb();
        var @event = new Event
        {
            SchoolId = SchoolId,
            Title = "Ke smazání",
            StartAt = DateTimeOffset.UtcNow,
            EndAt = DateTimeOffset.UtcNow.AddHours(1),
            Audience = EventAudience.Everyone,
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Admin").DeleteEvent(@event.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Events.FindAsync(@event.Id));
    }

    /// <summary>Shared fixture for the audience/visibility tests below: two
    /// classes, a teacher who owns only class A, a student in each class,
    /// and a parent of each student.</summary>
    private sealed class AudienceFixture
    {
        public required SchoolClass ClassA { get; init; }
        public required SchoolClass ClassB { get; init; }
        public required User TeacherA { get; init; }
        public required User OtherTeacher { get; init; }
        public required StudentProfile StudentA { get; init; }
        public required StudentProfile StudentB { get; init; }
        public required User ParentA { get; init; }
        public required User ParentB { get; init; }
        public required User StudentUserA { get; init; }

        public static async Task<AudienceFixture> SeedAsync(AppDbContext db)
        {
            var teacherA = new User { Username = "ucitelA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
            var otherTeacher = new User { Username = "ucitelB", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
            var studentUserA = new User { Username = "zakA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
            db.Users.AddRange(teacherA, otherTeacher, studentUserA);
            await db.SaveChangesAsync();

            var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacherA.Id };
            var classB = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
            db.SchoolClasses.AddRange(classA, classB);
            await db.SaveChangesAsync();

            var studentA = new StudentProfile { Person = new Person { FirstName = "Žák", LastName = "A", UserId = studentUserA.Id, SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = classA.Id };
            var studentB = new StudentProfile { Person = new Person { FirstName = "Žák", LastName = "B", SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = classB.Id };
            db.Students.AddRange(studentA, studentB);
            await db.SaveChangesAsync();

            var parentA = new User { Username = "rodicA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
            var parentB = new User { Username = "rodicB", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
            db.Users.AddRange(parentA, parentB);
            await db.SaveChangesAsync();

            await db.LinkChildAsync(parentA.Id, studentA.Id, SchoolId);
            await db.LinkChildAsync(parentB.Id, studentB.Id, SchoolId);
            await db.SaveChangesAsync();

            return new AudienceFixture
            {
                ClassA = classA, ClassB = classB, TeacherA = teacherA, OtherTeacher = otherTeacher,
                StudentA = studentA, StudentB = studentB, ParentA = parentA, ParentB = parentB,
                StudentUserA = studentUserA,
            };
        }
    }

    [Fact]
    public async Task TeacherCannotTargetClassTheyDoNotTeach()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);

        var result = await NewController(db, fx.TeacherA.Id, "Teacher")
            .CreateEvent(new CreateEventRequest(
                "Mimo dosah", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
                AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassB.Id]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TeacherCannotTargetOtherTeachers()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);

        var result = await NewController(db, fx.TeacherA.Id, "Teacher")
            .CreateEvent(new CreateEventRequest(
                "Kolega", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
                AllDay: false, Audience: EventAudience.Selected, TeacherUserIds: [fx.OtherTeacher.Id]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AdminSeesEveryEventRegardlessOfAudience()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, fx.TeacherA.Id, "Teacher").CreateEvent(new CreateEventRequest(
            "Soukromá poznámka učitele", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.OnlyMe));

        var result = await NewController(db, 999, "Admin").GetEvents(null, null);

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
    }

    [Fact]
    public async Task OnlyMeIsInvisibleToOtherTeacherButVisibleToCreator()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, fx.TeacherA.Id, "Teacher").CreateEvent(new CreateEventRequest(
            "Soukromé", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.OnlyMe));

        var otherTeacherResult = await NewController(db, fx.OtherTeacher.Id, "Teacher").GetEvents(null, null);
        var creatorResult = await NewController(db, fx.TeacherA.Id, "Teacher").GetEvents(null, null);

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(otherTeacherResult.Result).Value));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(creatorResult.Result).Value));
    }

    [Fact]
    public async Task TeacherSeesEventTargetingTheirOwnClassButNotAnUnrelatedOne()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro třídu B", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassB.Id]));

        var result = await NewController(db, fx.TeacherA.Id, "Teacher").GetEvents(null, null);

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public async Task ParentSeesEventTargetingTheirChildsClassButNotAnUnrelatedOne()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro třídu A", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassA.Id]));

        var parentAResult = await NewController(db, fx.ParentA.Id, "Parent").GetEvents(null, null);
        var parentBResult = await NewController(db, fx.ParentB.Id, "Parent").GetEvents(null, null);

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(parentAResult.Result).Value));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(parentBResult.Result).Value));
    }

    [Fact]
    public async Task ParentSeesEventTargetingTheirChildDirectly()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, fx.TeacherA.Id, "Teacher").CreateEvent(new CreateEventRequest(
            "Pro žáka A", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, StudentIds: [fx.StudentA.Id]));

        var result = await NewController(db, fx.ParentA.Id, "Parent").GetEvents(null, null);

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public async Task EveryoneAudienceIsVisibleToAllRoles()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro celou školu", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Everyone));

        var parentResult = await NewController(db, fx.ParentB.Id, "Parent").GetEvents(null, null);
        var otherTeacherResult = await NewController(db, fx.OtherTeacher.Id, "Teacher").GetEvents(null, null);
        var studentResult = await NewController(db, fx.StudentUserA.Id, "Student").GetEvents(null, null);

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(parentResult.Result).Value));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(otherTeacherResult.Result).Value));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(studentResult.Result).Value));
    }

    [Fact]
    public async Task StudentSeesEventTargetingTheirOwnClassButNotAnUnrelatedOne()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro třídu A", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassA.Id]));
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro třídu B", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassB.Id]));

        var result = await NewController(db, fx.StudentUserA.Id, "Student").GetEvents(null, null);

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
        Assert.Equal("Pro třídu A", events.Single().Title);
    }

    [Fact]
    public async Task StudentSeesEventTargetingThemselvesDirectly()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        await NewController(db, fx.TeacherA.Id, "Teacher").CreateEvent(new CreateEventRequest(
            "Pro žáka A", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, StudentIds: [fx.StudentA.Id]));

        var result = await NewController(db, fx.StudentUserA.Id, "Student").GetEvents(null, null);

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    [Fact]
    public async Task StudentWithNoLinkedStudentSeesOnlyEveryoneAudience()
    {
        using var db = NewDb();
        var fx = await AudienceFixture.SeedAsync(db);
        var unlinkedStudentUser = new User { Username = "zakC", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
        db.Users.Add(unlinkedStudentUser);
        await db.SaveChangesAsync();
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro třídu A", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Selected, SchoolClassIds: [fx.ClassA.Id]));
        await NewController(db, 999, "Admin").CreateEvent(new CreateEventRequest(
            "Pro celou školu", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            AllDay: false, Audience: EventAudience.Everyone));

        var result = await NewController(db, unlinkedStudentUser.Id, "Student").GetEvents(null, null);

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
        Assert.Equal("Pro celou školu", events.Single().Title);
    }
}
