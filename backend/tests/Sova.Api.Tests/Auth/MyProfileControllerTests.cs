using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Tests.Auth;

public class MyProfileControllerTests
{
    private const int SchoolId = 1;
    private static readonly PasswordHasher<User> Hasher = new();

    private static readonly JwtTokenFactory TokenFactory = new(Options.Create(
        new JwtOptions { SigningKey = "test-signing-key-at-least-32-chars-long", Issuer = "sova-tests", Audience = "sova-tests" }));

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static MyProfileController NewController(AppDbContext db, int currentUserId)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())]);
        return new MyProfileController(db, TokenFactory)
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

        Assert.IsType<BadRequestObjectResult>(result.Result);
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

        var dto = Assert.IsType<ChangePasswordResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotEmpty(dto.Token);
        var reloaded = await db.Users.FindAsync(user.Id) ?? throw new InvalidOperationException();
        Assert.Equal(
            PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(reloaded, reloaded.PasswordHash, "nove-heslo"));
    }

    [Fact]
    public async Task ChangePasswordClearsMustChangePasswordFlagAndReissuesToken()
    {
        using var db = NewDb();
        var user = NewUser("heslo123");
        user.MustChangePassword = true;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, user.Id).ChangePassword(new ChangePasswordRequest("heslo123", "nove-heslo"));

        var dto = Assert.IsType<ChangePasswordResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(dto.Token);
        Assert.Equal("false", jwt.Claims.Single(c => c.Type == "mustChangePassword").Value);
        Assert.False((await db.Users.FindAsync(user.Id))!.MustChangePassword);
    }
}
