using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.People;

public class PeopleControllerTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly SchoolClass schoolClass;
    private readonly Subject maths, history;

    public PeopleControllerTests()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        history = new Subject { Name = "Dějepis", Abbreviation = "D", SchoolId = SchoolId };
        db.Subjects.AddRange(maths, history);
        db.SaveChanges();
    }

    private PeopleController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = TestRoles.Principal(db, 999, UserRole.Admin, SchoolId),
                },
            },
        };

    private static T Value<T>(ActionResult<T> result) =>
        (T)Assert.IsAssignableFrom<ObjectResult>(result.Result).Value!;

    private StudentMatrika Matrika() => new(schoolClass.Id);

    [Fact]
    public async Task APersonIsCreatedWithTheProfilesAsked()
    {
        var child = Value(await Controller().AddPerson(new SavePersonRequest("Jan", "Novák", Student: Matrika())));

        var person = Value(await Controller().AddPerson(new SavePersonRequest(
            "Jana", "Nováková", "777", "j@n.cz", new TeacherProfileRequest(
                [new(maths.Id), new(history.Id, 6, 9)], " 1. stupeň ", 22,
                [
                    new(DayOfWeek.Friday, 1, AvailabilityKind.Avoid), new(DayOfWeek.Wednesday, 2, AvailabilityKind.Unavailable),
                    new(DayOfWeek.Wednesday, null, AvailabilityKind.Unavailable), new(DayOfWeek.Thursday, null, AvailabilityKind.Unavailable),
                ], FreeDays: 1, MaxLessonsPerDay: 5), new ParentProfileRequest([child.Student!.Id]))));

        Assert.Equal("Jana Nováková", person.Name);
        Assert.Null(person.Account);
        Assert.Equal("D 6.–9., M, 1. stupeň", person.Teacher!.Qualification);
        Assert.Equal((22m, "1. stupeň"), (person.Teacher.ContractedHours, person.Teacher.QualificationNote));
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Friday], person.Teacher.TeachingDays);
        // A day off drops its periods.
        Assert.Equal(
            [
                new(DayOfWeek.Wednesday, null, AvailabilityKind.Unavailable), new(DayOfWeek.Thursday, null, AvailabilityKind.Unavailable),
                new AvailabilitySlot(DayOfWeek.Friday, 1, AvailabilityKind.Avoid),
            ], person.Teacher.Availability);
        Assert.Equal((1, (int?)null, 5), (person.Teacher.FreeDays, person.Teacher.MinLessonsPerDay, person.Teacher.MaxLessonsPerDay));
        Assert.Equal(["Jan Novák"], person.Parent!.Children.Select(c => c.Name));
        Assert.Null(person.Student);
        Assert.Equal("1.A", child.Student.SchoolClassName);
        Assert.NotNull(child.Student.DefaultVariableSymbol);
    }

    [Fact]
    public async Task TheAprobaceIsReplacedByTheOneSaved()
    {
        var person = Value(await Controller().AddPerson(new SavePersonRequest(
            "Jana", "Nováková", Teacher: new TeacherProfileRequest([new(maths.Id), new(history.Id)]))));

        var updated = Value(await Controller().UpdatePerson(person.Id, new SavePersonRequest(
            "Jana", "Nováková", Teacher: new TeacherProfileRequest([new(history.Id, 6)]))));

        Assert.Equal([new QualificationDto(history.Id, 6)], updated.Teacher!.Qualifications);
        Assert.Equal(TeacherProfile.Weekdays, updated.Teacher.TeachingDays);
        Assert.Equal(1, await db.TeacherQualifications.CountAsync());
        var refused = await Controller().UpdatePerson(person.Id, new SavePersonRequest(
            "Jana", "Nováková", Teacher: new TeacherProfileRequest([new(history.Id, 9, 6)])));
        Assert.IsType<BadRequestObjectResult>(refused.Result);
        var fewerThanAtLeast = await Controller().UpdatePerson(person.Id, new SavePersonRequest(
            "Jana", "Nováková", Teacher: new TeacherProfileRequest([], MinLessonsPerDay: 4, MaxLessonsPerDay: 3)));
        Assert.IsType<BadRequestObjectResult>(fewerThanAtLeast.Result);
    }

    [Fact]
    public async Task AProfileLeftOutIsRemoved()
    {
        var person = Value(await Controller().AddPerson(new SavePersonRequest(
            "Jana", "Nováková", Teacher: new TeacherProfileRequest(), Parent: new ParentProfileRequest([]))));

        var updated = Value(await Controller().UpdatePerson(person.Id, new SavePersonRequest("Jana", "Nováková", Parent: new ParentProfileRequest([]))));

        Assert.Null(updated.Teacher);
        Assert.NotNull(updated.Parent);
        Assert.False(await db.TeacherProfiles.AnyAsync());
    }

    [Fact]
    public async Task TheProfileOfTheAccountsRoleStays()
    {
        var user = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(user);
        var person = new Person { User = user, LastName = "Učitelová", SchoolId = SchoolId };
        person.TeacherProfile = new TeacherProfile { Person = person, SchoolId = SchoolId };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var result = await Controller().UpdatePerson(person.Id, new SavePersonRequest("Jana", "Učitelová"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.True(await db.TeacherProfiles.AnyAsync());
    }

    [Fact]
    public async Task APupilsProfileWithoutHistoryIsRemovedAndThePersonStays()
    {
        var person = Value(await Controller().AddPerson(new SavePersonRequest(
            "Pan", "Učitel", Teacher: new TeacherProfileRequest(), Student: Matrika())));

        var updated = Value(await Controller().UpdatePerson(person.Id, new SavePersonRequest(
            "Pan", "Učitel", Teacher: new TeacherProfileRequest())));

        Assert.Null(updated.Student);
        Assert.NotNull(updated.Teacher);
        Assert.False(await db.Students.AnyAsync());
        Assert.True(await db.Persons.AnyAsync(p => p.Id == person.Id));
    }

    [Fact]
    public async Task APupilsProfileWithHistoryStays()
    {
        var pupil = Value(await Controller().AddPerson(new SavePersonRequest("Jan", "Novák", Student: Matrika())));
        db.Grades.Add(new Grade { StudentId = pupil.Student!.Id, SubjectId = 1, Value = 1, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await Controller().UpdatePerson(pupil.Id, new SavePersonRequest("Jan", "Novák"));

        Assert.Contains("1 známku", Assert.IsType<ConflictObjectResult>(result.Result).Value!.ToString());
        Assert.True(await db.Students.AnyAsync());
    }

    [Fact]
    public async Task TheListLeavesOutFormerPupils()
    {
        Value(await Controller().AddPerson(new SavePersonRequest("Jana", "Nováková", Parent: new ParentProfileRequest([]))));
        var former = Value(await Controller().AddPerson(new SavePersonRequest("Petr", "Bývalý", Student: Matrika())));
        var student = await db.Students.SingleAsync(s => s.Id == former.Student!.Id);
        student.LeftAt = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        await db.SaveChangesAsync();

        var people = Value(await Controller().GetPeople());

        Assert.Equal(["Jana Nováková"], people.Select(p => p.Name));
    }

    [Fact]
    public async Task SomeoneWithAnAccountIsNotDeleted()
    {
        var user = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(user);
        var person = new Person { User = user, LastName = "Rodič", SchoolId = SchoolId };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        Assert.IsType<ConflictObjectResult>(await Controller().DeletePerson(person.Id));
    }

    [Fact]
    public async Task APupilWithHistoryIsNotDeleted()
    {
        var pupil = Value(await Controller().AddPerson(new SavePersonRequest("Jan", "Novák", Student: Matrika())));
        db.Grades.Add(new Grade { StudentId = pupil.Student!.Id, SubjectId = 1, Value = 1, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await Controller().DeletePerson(pupil.Id);

        Assert.Contains("1 známku", Assert.IsType<ConflictObjectResult>(result).Value!.ToString());
    }

    [Fact]
    public async Task APersonWithoutAnAccountIsDeletedWithTheirProfiles()
    {
        var child = Value(await Controller().AddPerson(new SavePersonRequest("Jan", "Novák", Student: Matrika())));
        var parent = Value(await Controller().AddPerson(new SavePersonRequest(
            "Jana", "Nováková", Parent: new ParentProfileRequest([child.Student!.Id]))));

        Assert.IsType<NoContentResult>(await Controller().DeletePerson(parent.Id));
        Assert.IsType<NoContentResult>(await Controller().DeletePerson(child.Id));

        Assert.False(await db.Persons.AnyAsync());
        Assert.False(await db.ParentStudents.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.Students.AnyAsync());
    }
}
