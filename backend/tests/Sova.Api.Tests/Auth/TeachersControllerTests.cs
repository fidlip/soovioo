using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Tests.Auth;

public class TeachersControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    [Fact]
    public async Task GetTeachersReturnsOnlyTeacherRoleUsers()
    {
        using var db = NewDb();
        db.Users.AddRange(
            new User { Username = "ucitel", PasswordHash = "", Role = UserRole.Teacher, SchoolId = SchoolId },
            new User { Username = "admin", PasswordHash = "", Role = UserRole.Admin, SchoolId = SchoolId },
            new User { Username = "rodic", PasswordHash = "", Role = UserRole.Parent, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await new TeachersController(db).GetTeachers();

        var teachers = Assert.IsAssignableFrom<IEnumerable<TeacherDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var teacher = Assert.Single(teachers);
        Assert.Equal("ucitel", teacher.Username);
    }
}
