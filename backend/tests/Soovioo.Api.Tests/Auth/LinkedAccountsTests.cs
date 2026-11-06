using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.Retention;

namespace Soovioo.Api.Tests.Auth;

/// <summary>One person's accounts at several schools: linking them with the
/// other account's credentials, switching with a one-time code, what ends a
/// link, the phones they share — and that no school gets to see any of it.</summary>
public class LinkedAccountsTests
{
    private const int SchoolA = 1;
    private const int SchoolB = 2;
    private const int SchoolC = 3;
    private const int DemoSchool = 4;
    private const string Password = "heslo123";
    private static readonly PasswordHasher<User> Hasher = new();

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero));
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly List<AppDbContext> contexts = [];
    private readonly User inA;
    private readonly User inB;
    private readonly User inC;
    private readonly User otherInB;
    private readonly User inDemo;

    public LinkedAccountsTests()
    {
        var db = DbOf(SchoolA);
        db.Schools.AddRange(
            new School { Id = SchoolA, Name = "ZŠ Kopec", Slug = "kopec" },
            new School { Id = SchoolB, Name = "SŠ Údolí", Slug = "udoli" },
            new School { Id = SchoolC, Name = "Gymnázium Les", Slug = "les" },
            new School { Id = DemoSchool, Name = "Ukázka", Slug = "demo-1", IsDemo = true });
        inA = NewUser(db, "rodic", SchoolA);
        inB = NewUser(db, "novak", SchoolB);
        inC = NewUser(db, "novakova", SchoolC);
        otherInB = NewUser(db, "druhy", SchoolB);
        inDemo = NewUser(db, "rodic", DemoSchool);
        db.SaveChanges();
    }

    private AppDbContext DbOf(int schoolId)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeCurrentSchoolContext(schoolId));
        contexts.Add(db);
        return db;
    }

    private static User NewUser(AppDbContext db, string username, int schoolId)
    {
        var user = new User { Username = username, PasswordHash = "", ActiveRole = db.Role(UserRole.Parent, schoolId), SchoolId = schoolId };
        user.PasswordHash = Hasher.HashPassword(user, Password);
        db.Users.Add(user);
        return user;
    }

    private LinkedAccountsController Controller(User? me = null, HttpContext? http = null)
    {
        var db = DbOf(me?.SchoolId ?? SchoolA);
        var context = http ?? new DefaultHttpContext();
        if (me is not null)
        {
            context.User = TestRoles.PrincipalOf(me);
        }
        return new LinkedAccountsController(
            db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private Task<IActionResult> LinkAsync(User me, string slug, string username, string password = Password) =>
        Controller(me).Link(new LinkAccountRequest(slug, username, password));

    private async Task<IReadOnlyList<LinkedAccountDto>?> LinkedOfAsync(User me)
    {
        var bell = new NotificationsController(DbOf(me.SchoolId!.Value), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestRoles.PrincipalOf(me) } },
        };
        var counts = Assert.IsType<UnreadCountsDto>(Assert.IsType<OkObjectResult>((await bell.GetUnreadCounts()).Result).Value);
        return counts.LinkedAccounts;
    }

    private async Task<string> SwitchCodeAsync(User me, User target) =>
        Assert.IsType<AccountSwitchDto>(Assert.IsType<OkObjectResult>((await Controller(me).Switch(target.Id)).Result).Value).Code;

    private static string ErrorOf(IActionResult result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result).Value);

    [Fact]
    public async Task LinkingShowsEachAccountToTheOtherWithItsUnreadNotifications()
    {
        Assert.IsType<NoContentResult>(await LinkAsync(inA, "udoli", "novak"));
        var db = DbOf(SchoolA);
        db.Notifications.AddRange(
            new Notification { SchoolId = SchoolB, UserId = inB.Id, Type = NotificationType.Grade, Title = "1", Page = "grades", CreatedAt = clock.Now },
            new Notification { SchoolId = SchoolB, UserId = inB.Id, Type = NotificationType.Grade, Title = "2", Page = "grades", CreatedAt = clock.Now, ReadAt = clock.Now },
            new Notification { SchoolId = SchoolB, UserId = inB.Id, Type = NotificationType.Grade, Title = "3", Page = "grades", CreatedAt = clock.Now, InApp = false });
        await db.SaveChangesAsync();

        Assert.Equal(new LinkedAccountDto(inB.Id, "SŠ Údolí", "novak", 1), Assert.Single((await LinkedOfAsync(inA))!));
        Assert.Equal(new LinkedAccountDto(inA.Id, "ZŠ Kopec", "rodic", 0), Assert.Single((await LinkedOfAsync(inB))!));
    }

    [Fact]
    public async Task AnAccountWithoutLinksHasNoList() =>
        Assert.Null(await LinkedOfAsync(inA));

    [Theory]
    [InlineData("udoli", "novak", "spatne")]
    [InlineData("udoli", "nikdo", Password)]
    [InlineData("nikde", "novak", Password)]
    public async Task WrongCredentialsAllAnswerTheSameAndLinkNothing(string slug, string username, string password)
    {
        Assert.Equal(LinkedAccountsController.BadCredentials, ErrorOf(await LinkAsync(inA, slug, username, password)));

        Assert.Empty(DbOf(SchoolA).AccountLinks);
    }

    [Fact]
    public async Task AnAccountWithoutAPasswordCantBeLinked()
    {
        var db = DbOf(SchoolB);
        var invited = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inB.Id);
        invited.PasswordHash = "";
        await db.SaveChangesAsync();

        Assert.Equal(LinkedAccountsController.BadCredentials, ErrorOf(await LinkAsync(inA, "udoli", "novak", "")));
    }

    [Fact]
    public async Task AccountsOfOneSchoolDontLink()
    {
        ErrorOf(await LinkAsync(inB, "udoli", "druhy"));
        ErrorOf(await LinkAsync(inB, "udoli", "novak"));

        Assert.Empty(DbOf(SchoolA).AccountLinks);
    }

    [Fact]
    public async Task ADemoSchoolLinksNeitherWay()
    {
        ErrorOf(await LinkAsync(inA, "demo-1", "rodic"));
        ErrorOf(await LinkAsync(inDemo, "kopec", "rodic"));

        Assert.Empty(DbOf(SchoolA).AccountLinks);
    }

    [Fact]
    public async Task AnAdminAssignedPasswordLinksNeitherWay()
    {
        var db = DbOf(SchoolA);
        var other = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inB.Id);
        other.MustChangePassword = true;
        await db.SaveChangesAsync();
        ErrorOf(await LinkAsync(inA, "udoli", "novak"));

        other.MustChangePassword = false;
        var mine = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inA.Id);
        mine.MustChangePassword = true;
        await db.SaveChangesAsync();
        ErrorOf(await LinkAsync(inA, "udoli", "novak"));

        Assert.Empty(db.AccountLinks);
    }

    [Fact]
    public async Task ADeactivatedAccountAnswersOnlyToItsPassword()
    {
        var db = DbOf(SchoolA);
        var other = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inB.Id);
        other.DeactivatedAt = clock.Now;
        await db.SaveChangesAsync();

        Assert.Equal(LinkedAccountsController.BadCredentials, ErrorOf(await LinkAsync(inA, "udoli", "novak", "spatne")));
        Assert.Equal(AuthController.DeactivatedMessage, ErrorOf(await LinkAsync(inA, "udoli", "novak")));
    }

    [Fact]
    public async Task ASuperAdminInSupportModeCantLink()
    {
        var db = DbOf(SchoolA);
        var superAdmin = new User { Username = "superadmin", PasswordHash = "", IsSuperAdmin = true };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext { User = TestRoles.PrincipalOf(superAdmin) };

        ErrorOf(await Controller(http: http).Link(new LinkAccountRequest("udoli", "novak", Password)));

        Assert.Empty(db.AccountLinks);
    }

    [Fact]
    public async Task LinkingAgainChangesNothing()
    {
        await LinkAsync(inA, "udoli", "novak");

        Assert.IsType<NoContentResult>(await LinkAsync(inB, "kopec", "rodic"));

        Assert.Equal(2, await DbOf(SchoolA).AccountLinks.CountAsync());
    }

    [Fact]
    public async Task AThirdAccountJoinsAndLinkedGroupsMerge()
    {
        await LinkAsync(inA, "udoli", "novak");
        await LinkAsync(inC, "kopec", "rodic");

        Assert.Equal([inA.Id, inB.Id], (await LinkedOfAsync(inC))!.Select(a => a.UserId).Order());
        Assert.Equal([inB.Id, inC.Id], (await LinkedOfAsync(inA))!.Select(a => a.UserId).Order());
    }

    [Fact]
    public async Task EachSchoolHasOneAccountAmongLinkedOnes()
    {
        await LinkAsync(inA, "udoli", "novak");

        ErrorOf(await LinkAsync(inA, "udoli", "druhy"));

        Assert.Equal(2, await DbOf(SchoolA).AccountLinks.CountAsync());
    }

    [Fact]
    public async Task UnlinkingEndsItForBothWhenNobodyElseIsThere()
    {
        await LinkAsync(inA, "udoli", "novak");

        Assert.IsType<NoContentResult>(await Controller(inA).Unlink(inB.Id));

        Assert.Empty(DbOf(SchoolA).AccountLinks);
        Assert.Null(await LinkedOfAsync(inA));
    }

    [Fact]
    public async Task UnlinkingOneLeavesTheOthersLinked()
    {
        await LinkAsync(inA, "udoli", "novak");
        await LinkAsync(inA, "les", "novakova");

        await Controller(inA).Unlink(inB.Id);

        Assert.Equal(inC.Id, Assert.Single((await LinkedOfAsync(inA))!).UserId);
        Assert.Null(await LinkedOfAsync(inB));
    }

    [Fact]
    public async Task AnAccountNotLinkedToTheCallerCantBeTouched()
    {
        await LinkAsync(inA, "udoli", "novak");

        Assert.IsType<NotFoundResult>(await Controller(inC).Unlink(inB.Id));
        Assert.IsType<NotFoundResult>((await Controller(inC).Switch(inB.Id)).Result);
        Assert.IsType<NotFoundResult>(await Controller(inA).Unlink(inA.Id));
        Assert.IsType<NotFoundResult>((await Controller(inA).Switch(inA.Id)).Result);
        Assert.Equal(2, await DbOf(SchoolA).AccountLinks.CountAsync());
    }

    [Fact]
    public async Task TheCodeLogsIntoTheOtherAccountOnceAndNamesItsSchool()
    {
        await LinkAsync(inA, "udoli", "novak");
        var dto = Assert.IsType<AccountSwitchDto>(Assert.IsType<OkObjectResult>((await Controller(inA).Switch(inB.Id)).Result).Value);
        Assert.Equal("udoli", dto.SchoolSlug);

        var login = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(dto.Code))).Result).Value);

        Assert.Equal("novak", login.Username);
        Assert.Equal("udoli", login.SchoolSlug);
        Assert.NotNull(login.RefreshToken);
        Assert.Equal(LinkedAccountsController.BadCode, Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(dto.Code))).Result).Value);
    }

    [Fact]
    public async Task OnlyTheHashOfTheCodeIsStored()
    {
        await LinkAsync(inA, "udoli", "novak");

        var code = await SwitchCodeAsync(inA, inB);

        var stored = await DbOf(SchoolA).AccountLinks.FirstAsync(l => l.UserId == inB.Id);
        Assert.NotNull(stored.SwitchCodeHash);
        Assert.NotEqual(code, stored.SwitchCodeHash);
    }

    [Fact]
    public async Task AnExpiredCodeIsRefusedAndGone()
    {
        await LinkAsync(inA, "udoli", "novak");
        var code = await SwitchCodeAsync(inA, inB);

        clock.Now += LinkedAccountsController.CodeLength + TimeSpan.FromSeconds(1);

        Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(code))).Result);
        Assert.Null((await DbOf(SchoolA).AccountLinks.FirstAsync(l => l.UserId == inB.Id)).SwitchCodeHash);
    }

    [Fact]
    public async Task ANewCodeReplacesTheOlderOne()
    {
        await LinkAsync(inA, "udoli", "novak");
        var older = await SwitchCodeAsync(inA, inB);
        var newer = await SwitchCodeAsync(inA, inB);

        Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(older))).Result);
        Assert.IsType<OkObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(newer))).Result);
    }

    [Fact]
    public async Task AnUnknownCodeIsRefused() =>
        Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest("nesmysl"))).Result);

    [Fact]
    public async Task UnlinkingKillsACodeOnItsWay()
    {
        await LinkAsync(inA, "udoli", "novak");
        var code = await SwitchCodeAsync(inA, inB);

        await Controller(inA).Unlink(inB.Id);

        Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(code))).Result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWebSessionContinuesTheWayItWasLoggedIn(bool remembered)
    {
        await LinkAsync(inA, "udoli", "novak");
        var refresh = AuthTestServices.RefreshTokens(DbOf(SchoolA), clock);
        var session = await refresh.IssueAsync(inA, remembered);
        var page = new DefaultHttpContext();
        page.Request.Headers.Cookie = $"{AuthSessionService.CookieNameFor(SchoolA)}={session.Token}";
        page.Request.Headers[AuthSessionService.CsrfHeaderName] = "1";
        var code = Assert.IsType<AccountSwitchDto>(Assert.IsType<OkObjectResult>((await Controller(inA, page).Switch(inB.Id)).Result).Value).Code;

        var target = new DefaultHttpContext();
        var login = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
            (await Controller(http: target).RedeemCode(new RedeemLoginCodeRequest(code, UseCookie: true))).Result).Value);

        Assert.Null(login.RefreshToken);
        var cookie = Assert.Single(target.Response.Headers.SetCookie, c => c!.StartsWith(AuthSessionService.CookieNameFor(SchoolB)))!;
        Assert.Equal(remembered, cookie.Contains("expires", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheOtherSchoolsLogGetsAnOrdinaryLogin()
    {
        await LinkAsync(inA, "udoli", "novak");
        var code = await SwitchCodeAsync(inA, inB);
        var http = new DefaultHttpContext();

        await Controller(http: http).RedeemCode(new RedeemLoginCodeRequest(code));

        var audit = AuditContext.Of(http);
        Assert.Equal(SchoolB, audit.SchoolId);
        Assert.Equal(inB.Id, audit.UserId);
        Assert.Equal("Přihlášení", audit.Action);
        Assert.Equal("Přihlášení", audit.Description);
    }

    [Theory]
    [InlineData(nameof(LinkedAccountsController.Link))]
    [InlineData(nameof(LinkedAccountsController.Unlink))]
    [InlineData(nameof(LinkedAccountsController.Switch))]
    public void LinkingAndSwitchingStayOutOfTheSchoolsLog(string action) =>
        Assert.NotNull(typeof(LinkedAccountsController).GetMethod(action)!
            .GetCustomAttributes(typeof(CrossSchoolAuditAttribute), false).SingleOrDefault());

    [Fact]
    public async Task AdminResettingThePasswordEndsTheLink()
    {
        await LinkAsync(inA, "udoli", "novak");
        var db = DbOf(SchoolA);
        var admin = NewUser(db, "admin", SchoolA);
        var person = new Soovioo.Api.People.Person { UserId = inA.Id, LastName = "Rodič", SchoolId = SchoolA };
        person.ParentProfile = new Soovioo.Api.People.ParentProfile { Person = person, SchoolId = SchoolA };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        var users = new UsersController(
            db, new FakeCurrentSchoolContext(SchoolA), new FakeEmailSender(),
            Options.Create(new AppLinkOptions()), AuthTestServices.RefreshTokens(db, clock),
            NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestRoles.PrincipalOf(admin) } },
        };

        Assert.IsType<OkObjectResult>((await users.UpdateUser(inA.Id, new UpdateUserRequest("rodic", [db.Role(UserRole.Parent, SchoolA).Id], person.Id, Password: "od-skoly"))).Result);

        Assert.Empty(DbOf(SchoolA).AccountLinks);
    }

    [Fact]
    public async Task AdminEditingWithoutAPasswordKeepsTheLink()
    {
        await LinkAsync(inA, "udoli", "novak");
        var db = DbOf(SchoolA);
        var admin = NewUser(db, "admin", SchoolA);
        var person = new Soovioo.Api.People.Person { UserId = inA.Id, LastName = "Rodič", SchoolId = SchoolA };
        person.ParentProfile = new Soovioo.Api.People.ParentProfile { Person = person, SchoolId = SchoolA };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        var users = new UsersController(
            db, new FakeCurrentSchoolContext(SchoolA), new FakeEmailSender(),
            Options.Create(new AppLinkOptions()), AuthTestServices.RefreshTokens(db, clock),
            NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = TestRoles.PrincipalOf(admin) } },
        };

        Assert.IsType<OkObjectResult>((await users.UpdateUser(inA.Id, new UpdateUserRequest("rodic", [db.Role(UserRole.Parent, SchoolA).Id], person.Id))).Result);

        Assert.Equal(2, await DbOf(SchoolA).AccountLinks.CountAsync());
    }

    [Fact]
    public async Task AcceptingAnInviteEndsTheLink()
    {
        await LinkAsync(inA, "udoli", "novak");
        var db = DbOf(SchoolA);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inA.Id);
        user.InvitationToken = "pozvanka";
        user.InvitationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        var auth = new AuthController(db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock), AuthTestServices.SupportSessions(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await auth.AcceptInvite(new AcceptInviteRequest("pozvanka", "nove-heslo"));

        Assert.Empty(DbOf(SchoolA).AccountLinks);
    }

    [Fact]
    public async Task DeactivatingEndsTheLinkAndItsCode()
    {
        await LinkAsync(inA, "udoli", "novak");
        var code = await SwitchCodeAsync(inA, inB);
        var db = DbOf(SchoolB);
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == inB.Id);

        await new AccountEraser(db, AuthTestServices.RefreshTokens(db, clock), null!, clock).DeactivateAsync(user);
        await db.SaveChangesAsync();

        Assert.Empty(DbOf(SchoolA).AccountLinks);
        Assert.IsType<BadRequestObjectResult>((await Controller().RedeemCode(new RedeemLoginCodeRequest(code))).Result);
    }

    private sealed class FakePushSender : IPushSender
    {
        public List<(string Token, PushMessage Message)> Sent { get; } = [];
        public HashSet<string> Invalid { get; } = [];

        public Task<PushResult> SendAsync(string token, PushMessage message)
        {
            if (Invalid.Contains(token))
            {
                return Task.FromResult(PushResult.InvalidToken);
            }
            Sent.Add((token, message));
            return Task.FromResult(PushResult.Sent);
        }
    }

    private async Task AddDeviceAsync(User user, string token)
    {
        var db = DbOf(user.SchoolId!.Value);
        db.PushDevices.Add(new PushDevice { SchoolId = user.SchoolId.Value, UserId = user.Id, Token = token, CreatedAt = clock.Now, LastSeenAt = clock.Now });
        await db.SaveChangesAsync();
    }

    private async Task NotifyAsync(User user, int schoolId)
    {
        var db = DbOf(schoolId);
        await new Notifier(db, clock).NotifyAsync(schoolId, [user.Id], NotificationType.Grade, "Nová známka", "Jan: 1", "grades");
        await db.SaveChangesAsync();
    }

    private async Task<FakePushSender> RunPushAsync(FakePushSender? sender = null)
    {
        sender ??= new FakePushSender();
        await new PushJob(DbOf(SchoolA), sender, clock).RunAsync();
        return sender;
    }

    [Fact]
    public async Task APushOfTheOtherAccountComesToThePhoneLoggedInAsThisOne()
    {
        await LinkAsync(inA, "udoli", "novak");
        await AddDeviceAsync(inA, "telefon");

        await NotifyAsync(inB, SchoolB);

        var sent = Assert.Single((await RunPushAsync()).Sent);
        Assert.Equal("telefon", sent.Token);
        Assert.DoesNotContain("Údolí", sent.Message.Title + sent.Message.Body);
    }

    [Fact]
    public async Task WithoutALinkThePushStaysAway()
    {
        await AddDeviceAsync(inA, "telefon");

        await NotifyAsync(inB, SchoolB);

        Assert.Empty((await RunPushAsync()).Sent);
    }

    [Fact]
    public async Task AfterUnlinkingAWaitingPushDoesntReachTheOtherPhone()
    {
        await LinkAsync(inA, "udoli", "novak");
        await AddDeviceAsync(inA, "telefon");
        await NotifyAsync(inB, SchoolB);

        await Controller(inA).Unlink(inB.Id);

        Assert.Empty((await RunPushAsync()).Sent);
    }

    [Fact]
    public async Task ADeadPhoneSharedByBothAccountsIsRemovedOnceAndAnsweredOnce()
    {
        await LinkAsync(inA, "udoli", "novak");
        await AddDeviceAsync(inA, "mrtvy");
        await NotifyAsync(inA, SchoolA);
        await NotifyAsync(inB, SchoolB);
        var sender = new FakePushSender();
        sender.Invalid.Add("mrtvy");

        await RunPushAsync(sender);

        Assert.Empty(sender.Sent);
        Assert.Empty(DbOf(SchoolA).PushDevices.IgnoreQueryFilters());
    }

    [Fact]
    public async Task EachAccountsOwnPushGoesToAllTheLinkedPhones()
    {
        await LinkAsync(inA, "udoli", "novak");
        await AddDeviceAsync(inA, "telefon-a");
        await AddDeviceAsync(inB, "telefon-b");

        await NotifyAsync(inA, SchoolA);

        Assert.Equal(["telefon-a", "telefon-b"], (await RunPushAsync()).Sent.Select(s => s.Token).Order());
    }
}
