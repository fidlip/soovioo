using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Tests.Auth;

public class AuthControllerTests
{
    private const int SchoolId = 1;
    private static readonly PasswordHasher<User> Hasher = new();

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static AuthController NewController(AppDbContext db, TimeProvider? clock = null) =>
        new(db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock), AuthTestServices.SupportSessions(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task AcceptInviteRejectsEmptyPassword()
    {
        using var db = NewDb();

        var result = await NewController(db).AcceptInvite(new AcceptInviteRequest("sometoken", ""));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AcceptInviteRejectsUnknownToken()
    {
        using var db = NewDb();

        var result = await NewController(db).AcceptInvite(new AcceptInviteRequest("no-such-token", "nove-heslo"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AcceptInviteRejectsAnExpiredToken()
    {
        using var db = NewDb();
        var user = new User
        {
            Username = "ucitel",
            PasswordHash = "x",
            ActiveRole = UserRole.Teacher,
            SchoolId = SchoolId,
            InvitationToken = "expired-token",
            InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var originalHash = user.PasswordHash;

        var result = await NewController(db).AcceptInvite(new AcceptInviteRequest("expired-token", "nove-heslo"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(originalHash, (await db.Users.FindAsync(user.Id))!.PasswordHash);
    }

    [Fact]
    public async Task AcceptInviteSetsThePasswordAndLogsIn()
    {
        using var db = NewDb();
        var user = new User
        {
            Username = "ucitel",
            PasswordHash = "x",
            ActiveRole = UserRole.Teacher,
            SchoolId = SchoolId,
            MustChangePassword = true,
            InvitationToken = "valid-token",
            InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db).AcceptInvite(new AcceptInviteRequest("valid-token", "nove-heslo"));

        var dto = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotEmpty(dto.Token);
        Assert.Equal("ucitel", dto.Username);
        var reloaded = await db.Users.FindAsync(user.Id) ?? throw new InvalidOperationException();
        Assert.Equal(
            PasswordVerificationResult.Success,
            Hasher.VerifyHashedPassword(reloaded, reloaded.PasswordHash, "nove-heslo"));
        Assert.False(reloaded.MustChangePassword);
        Assert.Null(reloaded.InvitationToken);
        Assert.Null(reloaded.InvitationTokenExpiresAt);
    }

    [Fact]
    public async Task AcceptInviteTokenCanOnlyBeUsedOnce()
    {
        using var db = NewDb();
        var user = new User
        {
            Username = "ucitel",
            PasswordHash = "x",
            ActiveRole = UserRole.Teacher,
            SchoolId = SchoolId,
            InvitationToken = "valid-token",
            InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await NewController(db).AcceptInvite(new AcceptInviteRequest("valid-token", "nove-heslo"));

        var result = await NewController(db).AcceptInvite(new AcceptInviteRequest("valid-token", "jine-heslo"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
