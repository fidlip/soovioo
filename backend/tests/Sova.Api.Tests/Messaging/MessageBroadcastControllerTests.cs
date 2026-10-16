using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;
using Sova.Api.Messaging;
using Sova.Api.Timetable;

namespace Sova.Api.Tests.Messaging;

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
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Name, "tester"),
        ]);
        return new MessageBroadcastController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
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
            new Student { FirstName = "A", LastName = "A", SchoolId = SchoolId, SchoolClassId = 0 },
            new Student { FirstName = "B", LastName = "B", SchoolId = SchoolId, SchoolClassId = 0 });
        await db.SaveChangesAsync();
        foreach (var student in db.Students)
        {
            student.SchoolClassId = schoolClass.Id;
        }
        await db.SaveChangesAsync();

        var result = await NewController(db, 42, "Teacher")
            .BroadcastToClass(schoolClass.Id, new SendMessageRequest("Oznámení", "Zítra ředitelské volno."));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, dto.RecipientCount);
        Assert.Equal(2, db.Messages.Count());
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
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            TeacherId = 7,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher")
            .BroadcastToClass(schoolClass.Id, new SendMessageRequest("Test", "Test"));

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
            .BroadcastToClass(schoolClass.Id, new SendMessageRequest("Test", "Test"));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task BroadcastToClassAllowsAdminRegardlessOfOwnership()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = 42 };
        db.SchoolClasses.Add(schoolClass);
        db.Students.Add(new Student { FirstName = "A", LastName = "A", SchoolId = SchoolId, SchoolClassId = schoolClass.Id });
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Admin")
            .BroadcastToClass(schoolClass.Id, new SendMessageRequest("Test", "Test"));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.RecipientCount);
    }

    [Fact]
    public async Task BroadcastToSchoolSendsToEveryStudent()
    {
        using var db = NewDb();
        db.Students.AddRange(
            new Student { FirstName = "A", LastName = "A", SchoolId = SchoolId },
            new Student { FirstName = "B", LastName = "B", SchoolId = SchoolId },
            new Student { FirstName = "C", LastName = "C", SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Admin")
            .BroadcastToSchool(new SendMessageRequest("Prázdniny", "Od pátku ředitelské volno."));

        var dto = Assert.IsType<BroadcastResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(3, dto.RecipientCount);
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
