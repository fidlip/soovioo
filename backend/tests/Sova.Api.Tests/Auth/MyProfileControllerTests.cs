using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Tests.Auth;

public class MyProfileControllerTests
{
    private const int SchoolId = 1;
    private static readonly PasswordHasher<User> Hasher = new();

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static MyProfileController NewController(AppDbContext db, int currentUserId)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())]);
        return new MyProfileController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static User NewUser(string password) =>
        new()
        {
            Username = "ucitel",
            PasswordHash = Hasher.HashPassword(null!, password),
            Role = UserRole.Teacher,
            SchoolId = SchoolId,
        };

    [Fact]
    public async Task UpdateProfileChangesPhoneAndEmail()
    {
        using var db = NewDb();
        var user = NewUser("heslo123");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, user.Id).UpdateProfile(new UpdateMyProfileRequest("123456789", "a@b.cz"));

        var dto = Assert.IsType<MyProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("123456789", dto.Phone);
        Assert.Equal("a@b.cz", dto.Email);
    }

    [Fact]
    public async Task ChangePasswordRejectsWrongCurrentPassword()
    {
        using var db = NewDb();
        var user = NewUser("heslo123");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var originalHash = user.PasswordHash;

        var result = await NewController(db, user.Id).ChangePassword(new ChangePasswordRequest("spatne", "nove-heslo"));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(originalHash, (await db.Users.FindAsync(user.Id))!.PasswordHash);
    }

    [Fact]
    public async Task ChangePasswordSucceedsWithCorrectCurrentPassword()
    {
        using var db = NewDb();
        var user = NewUser("heslo123");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, user.Id).ChangePassword(new ChangePasswordRequest("heslo123", "nove-heslo"));

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Users.FindAsync(user.Id) ?? throw new InvalidOperationException();
        Assert.Equal(
            PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(reloaded, reloaded.PasswordHash, "nove-heslo"));
    }
}
