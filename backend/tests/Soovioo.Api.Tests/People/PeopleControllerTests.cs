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

    public PeopleControllerTests()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.SaveChanges();
    }

    private PeopleController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "999"),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Admin)),
                    ])),
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
            "Jana", "Nováková", "777", "j@n.cz", new TeacherProfileRequest(" M, F "), new ParentProfileRequest([child.Student!.Id]))));

        Assert.Equal("Jana Nováková", person.Name);
        Assert.Null(person.Account);
        Assert.Equal("M, F", person.Teacher!.Qualification);
        Assert.Equal(["Jan Novák"], person.Parent!.Children.Select(c => c.Name));
        Assert.Null(person.Student);
        Assert.Equal("1.A", child.Student.SchoolClassName);
        Assert.NotNull(child.Student.DefaultVariableSymbol);
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
        var user = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = UserRole.Teacher, SchoolId = SchoolId };
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
    public async Task APupilsProfileIsNotRemovedByAnEdit()
    {
        var pupil = Value(await Controller().AddPerson(new SavePersonRequest("Jan", "Novák", Student: Matrika())));

        var result = await Controller().UpdatePerson(pupil.Id, new SavePersonRequest("Jan", "Novák"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
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
        var user = new User { Username = "rodic", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
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
