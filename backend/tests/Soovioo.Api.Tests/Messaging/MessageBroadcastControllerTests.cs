using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Messaging;

public class MessageBroadcastControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static MessageBroadcastController NewController(AppDbContext db, int currentUserId, string role)
    {
        var principal = TestRoles.Principal(db, currentUserId, Enum.Parse<UserRole>(role), SchoolId, "tester");
        return new MessageBroadcastController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    [Fact]
    public async Task BroadcastToClassSendsOneMessagePerStudentInClass()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 };
        db.SchoolClasses.Add(schoolClass);
        db.Students.AddRange(
            new StudentProfile { Person = new Person { FirstName = "A", LastName = "A", UserId = 100, SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = 0 },
            new StudentProfile { Person = new Person { FirstName = "B", LastName = "B", UserId = 101, SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = 0 });
        await db.SaveChangesAsync();
        foreach (var student in db.Students)
        {
            student.SchoolClassId = schoolClass.Id;
        }
        await db.SaveChangesAsync();

        var result = await NewController(db, 42, "Teacher")
            .BroadcastToClass(schoolClass.Id, new BroadcastRequest("Oznámení", "Zítra ředitelské volno."));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, dto.RecipientCount);
        Assert.Equal(2, db.Conversations.Count());
        Assert.Equal(2, db.Messages.Count());
        // One private conversation per family: sender + the pupil's account.
        var participants = db.ConversationParticipants
            .GroupBy(p => p.ConversationId)
            .Select(g => g.Select(p => p.UserId).Order().ToList())
            .ToList();
        Assert.Equal(2, participants.Count);
        Assert.Contains(participants, p => p.SequenceEqual([42, 100]));
        Assert.Contains(participants, p => p.SequenceEqual([42, 101]));
        // Lets the caller attach a file to every fan-out copy afterwards
        // (ConversationsController.UploadAttachment) — see BroadcastMessageRef.
        Assert.Equal(2, dto.Messages.Count);
        Assert.All(dto.Messages, m => Assert.True(m.MessageId > 0));
    }

    [Fact]
    public async Task BroadcastToClassAllowsTeacherWithTimetableEntryEvenWithoutClassTeacherRole()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            TimetableVersion = db.CurrentTimetableVersion(SchoolId),
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            TeacherId = 7,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher")
            .BroadcastToClass(schoolClass.Id, new BroadcastRequest("Test", "Test"));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task BroadcastToClassRejectsTeacherWithNoRelationToClass()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Teacher")
            .BroadcastToClass(schoolClass.Id, new BroadcastRequest("Test", "Test"));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task BroadcastToClassAllowsAdminRegardlessOfOwnership()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 };
        db.SchoolClasses.Add(schoolClass);
        db.Students.Add(new StudentProfile { Person = new Person { FirstName = "A", LastName = "A", UserId = 100, SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = schoolClass.Id });
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Admin")
            .BroadcastToClass(schoolClass.Id, new BroadcastRequest("Test", "Test"));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.RecipientCount);
    }

    [Fact]
    public async Task BroadcastToSchoolSendsToEveryStudent()
    {
        using var db = NewDb();
        db.Students.AddRange(
            new StudentProfile { Person = new Person { FirstName = "A", LastName = "A", UserId = 100, SchoolId = SchoolId }, SchoolId = SchoolId },
            new StudentProfile { Person = new Person { FirstName = "B", LastName = "B", UserId = 101, SchoolId = SchoolId }, SchoolId = SchoolId },
            new StudentProfile { Person = new Person { FirstName = "C", LastName = "C", SchoolId = SchoolId }, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Admin")
            .BroadcastToSchool(new BroadcastRequest("Prázdniny", "Od pátku ředitelské volno."));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        // C has neither an own account nor a parent — nobody to deliver to.
        Assert.Equal(2, dto.RecipientCount);
        Assert.Equal(1, dto.SkippedCount);
    }

    [Fact]
    public async Task GetBroadcastableClassesReturnsOnlyOwnedClassesForTeacher()
    {
        using var db = NewDb();
        var owned = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 };
        var notOwned = new SchoolClass { Name = "2.A", SchoolId = SchoolId, ClassTeacherId = 999 };
        db.SchoolClasses.AddRange(owned, notOwned);
        await db.SaveChangesAsync();

        var result = await NewController(db, 42, "Teacher").GetBroadcastableClasses();

        var classes = Assert.IsAssignableFrom<IEnumerable<SchoolClass>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var single = Assert.Single(classes);
        Assert.Equal("1.A", single.Name);
    }

    [Fact]
    public async Task GetBroadcastableClassesReturnsAllClassesForAdmin()
    {
        using var db = NewDb();
        db.SchoolClasses.AddRange(
            new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 },
            new SchoolClass { Name = "2.A", SchoolId = SchoolId, ClassTeacherId = 999 });
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Admin").GetBroadcastableClasses();

        var classes = Assert.IsAssignableFrom<IEnumerable<SchoolClass>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, classes.Count());
    }
}
