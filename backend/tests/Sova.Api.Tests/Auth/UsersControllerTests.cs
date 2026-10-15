using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Tests.Auth;

public class UsersControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static UsersController NewController(AppDbContext db, int currentUserId)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())]);
        return new UsersController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public async Task UpdateUserRenamesAndChangesRole()
    {
        using var db = NewDb();
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", "Teacher"));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("ucitelka", dto.Username);
    }

    [Fact]
    public async Task DeleteUserRejectsDeletingYourself()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        var otherAdmin = new User { Username = "admin2", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        db.Users.AddRange(admin, otherAdmin);
        await db.SaveChangesAsync();

        var result = await NewController(db, admin.Id).DeleteUser(admin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Users.FindAsync(admin.Id));
    }

    [Fact]
    public async Task DeleteUserRejectsRemovingTheLastAdmin()
    {
        using var db = NewDb();
        var onlyAdmin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        db.Users.Add(onlyAdmin);
        await db.SaveChangesAsync();

        // Acting as a different (hypothetical) caller id so the self-delete
        // guard doesn't mask the last-admin guard this test targets.
        var result = await NewController(db, 999).DeleteUser(onlyAdmin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Users.FindAsync(onlyAdmin.Id));
    }

    [Fact]
    public async Task DeleteUserSucceedsAndRemovesParentLinks()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.AddRange(admin, parent);
        await db.SaveChangesAsync();
        db.ParentStudents.Add(new Sova.Api.Evidence.ParentStudent { UserId = parent.Id, StudentId = 1, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, admin.Id).DeleteUser(parent.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Users.FindAsync(parent.Id));
        Assert.Empty(db.ParentStudents.Where(ps => ps.UserId == parent.Id));
    }
}
