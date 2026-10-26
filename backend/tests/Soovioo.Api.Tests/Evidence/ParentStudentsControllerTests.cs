using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Evidence;

public class ParentStudentsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static ParentStudentsController NewController(AppDbContext db, int currentUserId, string role) =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString()), new Claim(ClaimTypes.Role, role)])),
                },
            },
        };

    private sealed record Fixture(
        User Teacher, SchoolClass OwnClass, SchoolClass OtherClass, Student OwnStudent, Student OtherStudent, User Parent);

    private static async Task<Fixture> SeedAsync(AppDbContext db)
    {
        var teacher = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.AddRange(teacher, parent);
        await db.SaveChangesAsync();
        var ownClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        var otherClass = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(ownClass, otherClass);
        await db.SaveChangesAsync();
        var ownStudent = new Student { Person = new Person { FirstName = "Žák", LastName = "Vlastní", SchoolId = SchoolId }, SchoolClassId = ownClass.Id, SchoolId = SchoolId };
        var otherStudent = new Student { Person = new Person { FirstName = "Žák", LastName = "Cizí", SchoolId = SchoolId }, SchoolClassId = otherClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(ownStudent, otherStudent);
        await db.SaveChangesAsync();

        return new Fixture(teacher, ownClass, otherClass, ownStudent, otherStudent, parent);
    }

    [Fact]
    public async Task AdminSeesEveryLink()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await db.LinkChildAsync(fx.Parent.Id, fx.OwnStudent.Id, SchoolId);
        await db.LinkChildAsync(fx.Parent.Id, fx.OtherStudent.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Admin").GetLinks();

        var links = Assert.IsAssignableFrom<IEnumerable<ParentStudentDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, links.Count());
    }

    [Fact]
    public async Task ClassTeacherSeesOnlyLinksForTheirOwnClass()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await db.LinkChildAsync(fx.Parent.Id, fx.OwnStudent.Id, SchoolId);
        await db.LinkChildAsync(fx.Parent.Id, fx.OtherStudent.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, fx.Teacher.Id, "Teacher").GetLinks();

        var links = Assert.IsAssignableFrom<IEnumerable<ParentStudentDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(links);
        Assert.Equal(fx.OwnStudent.Id, links.Single().StudentId);
    }

    [Fact]
    public async Task ClassTeacherCanLinkAParentToTheirOwnStudent()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var result = await NewController(db, fx.Teacher.Id, "Teacher")
            .AddLink(new CreateParentStudentLinkRequest(fx.Parent.Id, fx.OwnStudent.Id));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task ClassTeacherCannotLinkAParentToAnotherClasssStudent()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var result = await NewController(db, fx.Teacher.Id, "Teacher")
            .AddLink(new CreateParentStudentLinkRequest(fx.Parent.Id, fx.OtherStudent.Id));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ClassTeacherCannotRemoveALinkForAnotherClasssStudent()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await db.LinkChildAsync(fx.Parent.Id, fx.OtherStudent.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, fx.Teacher.Id, "Teacher").RemoveLink(fx.Parent.Id, fx.OtherStudent.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.Contains(fx.OtherStudent.Id, db.ChildIdsOf(fx.Parent.Id));
    }
}
