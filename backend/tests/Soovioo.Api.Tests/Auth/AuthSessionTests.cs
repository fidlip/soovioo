using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Auth;

/// <summary>Refresh-token sessions: cookie vs body delivery, rotation, reuse
/// detection, expiry and revocation.</summary>
public class AuthSessionTests
{
    private const int SchoolId = 1;
    private const int OtherSchoolId = 2;
    private static readonly string Cookie = AuthSessionService.CookieNameFor(SchoolId);

    /// <summary>What the web app sends from the demo school's page.</summary>
    private static readonly RefreshRequest Page = new(SchoolSlug: "demo");
    private const string Password = "heslo123";
    private static readonly PasswordHasher<User> Hasher = new();

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext db;
    private readonly User user;

    public AuthSessionTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        db.Schools.Add(new School { Id = OtherSchoolId, Name = "Jiná škola", Slug = "jina" });
        user = new User { Username = "ucitel", PasswordHash = "", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        user.PasswordHash = Hasher.HashPassword(user, Password);
        db.Users.Add(user);
        db.SaveChanges();
    }

    private AuthController Controller(HttpContext? http = null) =>
        new(db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock), AuthTestServices.SupportSessions(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = http ?? new DefaultHttpContext() },
        };

    /// <summary>A browser request carrying the refresh cookie (and, unless
    /// told otherwise, the CSRF header the web app always sends).</summary>
    private static DefaultHttpContext CookieRequest(string token, bool csrfHeader = true, string? cookie = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{cookie ?? Cookie}={token}";
        if (csrfHeader)
        {
            http.Request.Headers[AuthSessionService.CsrfHeaderName] = "1";
        }
        return http;
    }

    private static string? SetCookie(HttpContext http, string? cookie = null) =>
        http.Response.Headers.SetCookie.FirstOrDefault(c => c!.StartsWith((cookie ?? Cookie) + "="));

    private static string CookieValue(HttpContext http, string? cookie = null) =>
        SetCookie(http, cookie)!.Split(';')[0].Split('=', 2)[1];

    private async Task<string> WebLoginAsync(bool rememberMe = false)
    {
        var http = new DefaultHttpContext();
        await Controller(http).Login(new LoginRequest("ucitel", Password, "demo", UseCookie: true, RememberMe: rememberMe));
        return CookieValue(http);
    }

    private async Task<string> MobileLoginAsync()
    {
        var result = await Controller().Login(new LoginRequest("ucitel", Password, "demo"));
        return Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).RefreshToken!;
    }

    [Fact]
    public async Task WebLoginPutsTheRefreshTokenInAnHttpOnlySessionCookieOnly()
    {
        var http = new DefaultHttpContext();

        var result = await Controller(http).Login(new LoginRequest("ucitel", Password, "demo", UseCookie: true));

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Null(response.RefreshToken);
        Assert.NotEmpty(response.Token);
        var cookie = SetCookie(http)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RememberMeMakesThePersistent30DayCookie()
    {
        var http = new DefaultHttpContext();

        await Controller(http).Login(new LoginRequest("ucitel", Password, "demo", UseCookie: true, RememberMe: true));

        Assert.Contains("expires", SetCookie(http)!, StringComparison.OrdinalIgnoreCase);
        var stored = await db.RefreshTokens.SingleAsync();
        Assert.True(stored.Persistent);
        Assert.Equal(clock.Now.AddDays(30), stored.ExpiresAt);
    }

    [Fact]
    public async Task MobileLoginGetsTheRefreshTokenInTheBodyAndNoCookie()
    {
        var http = new DefaultHttpContext();

        var result = await Controller(http).Login(new LoginRequest("ucitel", Password, "demo"));

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(response.RefreshToken);
        Assert.Null(SetCookie(http));
    }

    [Fact]
    public async Task OnlyAHashOfTheRefreshTokenIsStored()
    {
        var token = await MobileLoginAsync();

        Assert.DoesNotContain(db.RefreshTokens, t => t.TokenHash == token);
    }

    [Fact]
    public async Task RefreshRotatesTheCookieAndReturnsAnAccessToken()
    {
        var token = await WebLoginAsync();
        var http = CookieRequest(token);

        var result = await Controller(http).Refresh(Page);

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("ucitel", response.Username);
        Assert.Equal("demo", response.SchoolSlug);
        Assert.NotEqual(token, CookieValue(http));
    }

    [Fact]
    public async Task RefreshWithoutAnyTokenIsNoContentNotAnError()
    {
        var result = await Controller().Refresh(Page);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task CookieWithoutTheCsrfHeaderIsIgnored()
    {
        var token = await WebLoginAsync();

        var result = await Controller(CookieRequest(token, csrfHeader: false)).Refresh(Page);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task ReusingARotatedTokenAfterTheGracePeriodRevokesTheWholeFamily()
    {
        var first = await MobileLoginAsync();
        var rotated = await Controller().Refresh(new RefreshRequest(first));
        var second = ((LoginResponse)((OkObjectResult)rotated.Result!).Value!).RefreshToken!;
        clock.Now += TimeSpan.FromMinutes(5);

        var replay = await Controller().Refresh(new RefreshRequest(first));
        var legit = await Controller().Refresh(new RefreshRequest(second));

        Assert.IsType<UnauthorizedResult>(replay.Result);
        Assert.IsType<UnauthorizedResult>(legit.Result);
    }

    [Fact]
    public async Task ConcurrentRefreshFromTwoTabsWithinTheGracePeriodSucceeds()
    {
        var token = await WebLoginAsync();
        await Controller(CookieRequest(token)).Refresh(Page);
        clock.Now += TimeSpan.FromSeconds(5);

        var second = await Controller(CookieRequest(token)).Refresh(Page);

        Assert.IsType<OkObjectResult>(second.Result);
    }

    [Fact]
    public async Task BrowserSessionCannotOutliveEightHoursEvenWhenRefreshed()
    {
        var token = await WebLoginAsync();
        clock.Now += TimeSpan.FromHours(7);
        var http = CookieRequest(token);
        await Controller(http).Refresh(Page);
        var successor = CookieValue(http);
        clock.Now += TimeSpan.FromHours(2);

        var result = await Controller(CookieRequest(successor)).Refresh(Page);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task PersistentSessionSlidesWithEachRefresh()
    {
        var token = await WebLoginAsync(rememberMe: true);
        clock.Now += TimeSpan.FromDays(20);
        var http = CookieRequest(token);
        await Controller(http).Refresh(Page);
        clock.Now += TimeSpan.FromDays(20);

        var result = await Controller(CookieRequest(CookieValue(http))).Refresh(Page);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task InvalidCookieIsClearedOnFailedRefresh()
    {
        var http = CookieRequest("not-a-real-token");

        var result = await Controller(http).Refresh(Page);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(http)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EachSchoolHasItsOwnCookie()
    {
        var other = new User { Username = "ucitel", PasswordHash = "", ActiveRole = db.Role(UserRole.Teacher, OtherSchoolId), SchoolId = OtherSchoolId };
        other.PasswordHash = Hasher.HashPassword(other, Password);
        db.Users.Add(other);
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext();

        await Controller(http).Login(new LoginRequest("ucitel", Password, "jina", UseCookie: true));

        Assert.NotNull(SetCookie(http, AuthSessionService.CookieNameFor(OtherSchoolId)));
        Assert.Null(SetCookie(http));
    }

    [Fact]
    public async Task AnotherSchoolsSessionIsRefusedOnThisSchoolsPageAndKept()
    {
        // A session from before the per-school cookies, in the bare cookie.
        var token = await WebLoginAsync();
        var http = CookieRequest(token, cookie: AuthSessionService.CookieName);

        var result = await Controller(http).Refresh(new RefreshRequest(SchoolSlug: "jina"));

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
        Assert.Null((await db.RefreshTokens.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task LogoutOnAnotherSchoolsPageLeavesTheSessionAlone()
    {
        var token = await WebLoginAsync();

        await Controller(CookieRequest(token, cookie: AuthSessionService.CookieName)).Logout(new RefreshRequest(SchoolSlug: "jina"));

        Assert.Null((await db.RefreshTokens.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task ASessionInTheBareCookieMovesToItsSchoolsCookie()
    {
        var token = await WebLoginAsync();
        var http = CookieRequest(token, cookie: AuthSessionService.CookieName);

        var result = await Controller(http).Refresh(Page);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(SetCookie(http));
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(http, AuthSessionService.CookieName)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSuperAdminsSessionCarriesOnOnASchoolsPage()
    {
        var admin = new User { Username = "superadmin", PasswordHash = "", IsSuperAdmin = true };
        admin.PasswordHash = Hasher.HashPassword(admin, Password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var login = new DefaultHttpContext();
        await Controller(login).Login(new LoginRequest("superadmin", Password, UseCookie: true));
        var token = CookieValue(login, AuthSessionService.CookieName);

        var result = await Controller(CookieRequest(token, cookie: AuthSessionService.CookieName)).Refresh(Page);

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("superadmin", response.Username);
    }

    [Fact]
    public async Task LogoutRevokesTheRefreshToken()
    {
        var token = await WebLoginAsync();
        await Controller(CookieRequest(token)).Logout(Page);

        var result = await Controller(CookieRequest(token)).Refresh(Page);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task ChangingPasswordSignsOutOtherSessionsButKeepsTheCurrentOne()
    {
        var otherDevice = await MobileLoginAsync();
        var thisBrowser = await WebLoginAsync(rememberMe: true);
        var http = CookieRequest(thisBrowser);
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())]));
        var profile = new MyProfileController(db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var change = await profile.ChangePassword(new ChangePasswordRequest(Password, "nove-heslo"));

        var response = Assert.IsType<ChangePasswordResponse>(Assert.IsType<OkObjectResult>(change.Result).Value);
        Assert.Null(response.RefreshToken);
        Assert.IsType<UnauthorizedResult>((await Controller().Refresh(new RefreshRequest(otherDevice))).Result);
        Assert.IsType<OkObjectResult>((await Controller(CookieRequest(CookieValue(http))).Refresh(Page)).Result);
        Assert.True((await db.RefreshTokens.SingleAsync(t => t.RevokedAt == null)).Persistent);
    }

    [Fact]
    public async Task AdminPasswordResetRevokesTheUsersSessions()
    {
        var token = await MobileLoginAsync();
        var admin = new User { Username = "admin", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, SchoolId), SchoolId = SchoolId };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var users = new UsersController(
            db, new FakeCurrentSchoolContext(SchoolId), new FakeEmailSender(),
            Options.Create(new AppLinkOptions()), AuthTestServices.RefreshTokens(db, clock),
            NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString())])),
                },
            },
        };

        var person = new Soovioo.Api.People.Person { User = user, LastName = "Učitelová", SchoolId = SchoolId };
        person.TeacherProfile = new Soovioo.Api.People.TeacherProfile { Person = person, SchoolId = SchoolId };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        await users.UpdateUser(user.Id, new UpdateUserRequest("ucitel", [db.Role(UserRole.Teacher, SchoolId).Id], person.Id, Password: "reset-heslo"));

        Assert.IsType<UnauthorizedResult>((await Controller().Refresh(new RefreshRequest(token))).Result);
    }
}
