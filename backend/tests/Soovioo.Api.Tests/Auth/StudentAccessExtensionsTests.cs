using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Auth;

public class StudentAccessExtensionsTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static ClaimsPrincipal StudentPrincipal(int userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, nameof(UserRole.Student))],
            authenticationType: "Test"));

    [Fact]
    public async Task StudentSeesOnlyTheirOwnStudentId()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        db.Students.Add(otherStudent);
        await db.SaveChangesAsync();
        var ownStudent = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(ownStudent);
        await db.SaveChangesAsync();

        var allowed = await StudentPrincipal(studentUser.Id).GetAccessibleStudentIdsAsync(db);

        Assert.NotNull(allowed);
        Assert.Equal([ownStudent.Id], allowed);
    }

    [Fact]
    public async Task StudentWithNoLinkedStudentSeesNothing()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();

        var allowed = await StudentPrincipal(studentUser.Id).GetAccessibleStudentIdsAsync(db);

        Assert.NotNull(allowed);
        Assert.Empty(allowed);
    }

    [Fact]
    public async Task StudentCannotAccessAnotherStudent()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var ownStudent = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.AddRange(ownStudent, otherStudent);
        await db.SaveChangesAsync();

        var principal = StudentPrincipal(studentUser.Id);

        Assert.True(await principal.CanAccessStudentAsync(db, ownStudent.Id));
        Assert.False(await principal.CanAccessStudentAsync(db, otherStudent.Id));
    }
}
