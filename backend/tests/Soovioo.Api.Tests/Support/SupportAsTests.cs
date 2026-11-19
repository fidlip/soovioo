using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Support;

/// <summary>"Vstoupit jako…" — the support in the school as one of its users.</summary>
public class SupportAsTests
{
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    private AppDbContext SuperAdminDb() => new(Options(), new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private AppDbContext SchoolDb(int schoolId) => new(Options(), new FakeCurrentSchoolContext(schoolId));

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private static HttpContext Http(IEnumerable<Claim> claims) =>
        new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };

    private static List<Claim> UserClaims(int userId, int? roleId = null)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
        if (roleId is not null)
        {
            claims.Add(new Claim(PermissionAccess.RoleIdClaim, roleId.ToString()!));
        }
        return claims;
    }

    private SupportTicketsController Tickets(AppDbContext db, IEnumerable<Claim> claims, int? schoolId = null) =>
        new(db, new FakeCurrentSchoolContext(schoolId, isSuperAdmin: schoolId is null), new Notifier(db, clock),
            new SupportPlanClient(Soovioo.Api.Tests.Backoffice.FakeBackoffice.NotConnected(),
                Microsoft.Extensions.Options.Options.Create(new SupportPlanOptions()),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SupportPlanClient>.Instance),
            AuthTestServices.SupportSessions(db, clock), new SupportReset(db, clock),
            Microsoft.Extensions.Options.Options.Create(new SupportOptions()), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(claims) },
        };

    private SupportPolicyController Policy(AppDbContext db, User admin) =>
        new(db, new FakeCurrentSchoolContext(admin.SchoolId), new Notifier(db, clock),
            Microsoft.Extensions.Options.Options.Create(new SupportOptions()), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(UserClaims(admin.Id, admin.ActiveRoleId)) },
        };

    private SupportController Support(AppDbContext db, IEnumerable<Claim> claims) =>
        new(db, AuthTestServices.SupportSessions(db, clock), AuthTestServices.RefreshTokens(db, clock), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(claims) },
        };

    private record World(School School, User SuperAdmin, User Admin, User Teacher, User Teacher2, User Student, int TicketId)
    {
        public Role RoleOf(UserRole builtIn) => School.Roles.Single(r => r.BuiltIn == builtIn);
    }

    private async Task<World> SeedAsync()
    {
        using var db = SuperAdminDb();
        var school = new School { Name = "ZŠ Ukázkov", Slug = "ukazkov", SupportAccess = SupportAccess.Always };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        var superAdmin = new User { Username = "superadmin", PasswordHash = "x", IsSuperAdmin = true };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        User Account(string username, string first, string last, params UserRole[] roles)
        {
            var user = new User
            {
                Username = username, PasswordHash = "x", SchoolId = school.Id,
                Roles = [.. roles.Select(r => school.Roles.Single(x => x.BuiltIn == r))],
                Person = new Person { FirstName = first, LastName = last, SchoolId = school.Id },
            };
            user.ActiveRole = user.Roles[0];
            db.Users.Add(user);
            return user;
        }
        var admin = Account("reditel", "Řehoř", "Ředitel", UserRole.Admin);
        var teacher = Account("novak", "Jan", "Novák", UserRole.Teacher, UserRole.Parent);
        var teacher2 = Account("mala", "Eva", "Malá", UserRole.Teacher);
        var student = Account("zak", "Petr", "Žák", UserRole.Student);
        await db.SaveChangesAsync();
        var ticket = new SupportTicket
        {
            SchoolId = school.Id, CreatedByUserId = admin.Id, CreatedByName = "Řehoř Ředitel", Title = "Rozvrh",
            Description = "", PlanName = "Free", AssigneeUserId = superAdmin.Id, AssigneeName = "superadmin",
            Status = SupportTicketStatus.Assigned,
        };
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        return new World(school, superAdmin, admin, teacher, teacher2, student, ticket.Id);
    }

    private async Task SchoolAllowsAsync(World w, SupportAsScope scope)
    {
        using var db = SchoolDb(w.School.Id);
        Assert.IsType<OkObjectResult>((await Policy(db, w.Admin).SetSupportAs(new SetSupportAsRequest(scope))).Result);
    }

    private async Task<ActionResult<SupportTicketDetailDto>> NameOnTicketAsync(World w, params int[] userIds)
    {
        using var db = SchoolDb(w.School.Id);
        return await Tickets(db, UserClaims(w.Admin.Id, w.Admin.ActiveRoleId), w.School.Id)
            .SetSupportAsUsers(w.TicketId, new SetSupportAsUsersRequest(userIds));
    }

    private async Task<IReadOnlyList<SupportAsUserDto>> CandidatesAsync(World w)
    {
        using var db = SuperAdminDb();
        return Assert.IsAssignableFrom<IReadOnlyList<SupportAsUserDto>>(
            (await Tickets(db, UserClaims(w.SuperAdmin.Id)).GetEnterAs(w.TicketId)).Value);
    }

    private async Task<ActionResult<SupportSessionStartDto>> EnterAsync(World w, int? userId = null, int? roleId = null)
    {
        using var db = SuperAdminDb();
        return await Tickets(db, UserClaims(w.SuperAdmin.Id)).Enter(w.TicketId, new SupportAsRequest(userId, roleId));
    }

    /// <summary>Entered and redeemed: the token's claims.</summary>
    private async Task<List<Claim>> TokenAsync(World w, int? userId = null, int? roleId = null)
    {
        var started = Assert.IsType<SupportSessionStartDto>(
            Assert.IsType<OkObjectResult>((await EnterAsync(w, userId, roleId)).Result).Value);
        using var db = SuperAdminDb();
        var login = await AuthTestServices.SupportSessions(db, clock).RedeemAsync(new DefaultHttpContext(), started.Code);
        return new JwtSecurityTokenHandler().ReadJwtToken(login!.Token).Claims.ToList();
    }

    private static string? Claim(List<Claim> claims, string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;

    [Fact]
    public async Task ByDefaultOnlyAsAUserTheTicketNamesNotAsTheAdmin()
    {
        var w = await SeedAsync();
        // Nobody named yet: no way in.
        Assert.Empty(await CandidatesAsync(w));
        Assert.IsType<BadRequestObjectResult>((await EnterAsync(w)).Result);
        Assert.IsType<BadRequestObjectResult>((await EnterAsync(w, w.Teacher.Id)).Result);

        Assert.IsType<OkObjectResult>((await NameOnTicketAsync(w, w.Teacher.Id)).Result);
        // Not as the admin of no one, nor as someone not named.
        Assert.IsType<BadRequestObjectResult>((await EnterAsync(w)).Result);
        Assert.IsType<BadRequestObjectResult>((await EnterAsync(w, w.Student.Id)).Result);
        Assert.IsType<OkObjectResult>((await EnterAsync(w, w.Teacher.Id)).Result);
    }

    [Fact]
    public async Task AsTheAdminWhenTheSchoolSaysSoAndThenAsNobodyElse()
    {
        var w = await SeedAsync();
        await SchoolAllowsAsync(w, SupportAsScope.Admin);
        Assert.IsType<BadRequestObjectResult>((await NameOnTicketAsync(w, w.Teacher.Id)).Result);
        Assert.Empty(await CandidatesAsync(w));
        Assert.IsType<BadRequestObjectResult>((await EnterAsync(w, w.Teacher.Id)).Result);

        var claims = await TokenAsync(w);
        Assert.Equal(w.SuperAdmin.Id.ToString(), Claim(claims, ClaimTypes.NameIdentifier));
        Assert.Equal("Admin", Claim(claims, "roleName"));
        Assert.Null(Claim(claims, SupportClaims.AsUserId));
    }

    [Fact]
    public async Task ANewTicketNamesItsAuthorUnlessTheyNameOthersAndTheAuthorComesFirst()
    {
        var w = await SeedAsync();
        using var db = SchoolDb(w.School.Id);
        var tickets = Tickets(db, UserClaims(w.Admin.Id, w.Admin.ActiveRoleId), w.School.Id);

        var mine = Assert.IsType<SupportTicketDetailDto>(Assert.IsType<OkObjectResult>(
            (await tickets.CreateSchoolTicket(new CreateSchoolTicketRequest("Rozvrh", ""))).Result).Value);
        Assert.Equal([w.Admin.Id], mine.Ticket.SupportAsUsers!.Select(u => u.Id));

        var both = Assert.IsType<SupportTicketDetailDto>(Assert.IsType<OkObjectResult>(
            (await tickets.CreateSchoolTicket(new CreateSchoolTicketRequest(
                "Známky", "", SupportAsUserIds: [w.Teacher.Id, w.Admin.Id]))).Result).Value);
        Assert.Equal([w.Admin.Id, w.Teacher.Id], both.Ticket.SupportAsUsers!.Select(u => u.Id));

        var none = Assert.IsType<SupportTicketDetailDto>(Assert.IsType<OkObjectResult>(
            (await tickets.CreateSchoolTicket(new CreateSchoolTicketRequest("Nic", "", SupportAsUserIds: []))).Result).Value);
        Assert.Empty(none.Ticket.SupportAsUsers!);
    }

    [Fact]
    public async Task AsAUserTheTicketNamesTheTokenIsTheirsInAnyOfTheirRoles()
    {
        var w = await SeedAsync();
        var detail = Assert.IsType<SupportTicketDetailDto>(Assert.IsType<OkObjectResult>(
            (await NameOnTicketAsync(w, w.Teacher.Id)).Result).Value);
        Assert.Equal(["Jan Novák"], detail.Ticket.SupportAsUsers!.Select(u => u.Name));
        Assert.Equal("Jan Novák",
            detail.Entries.Single(e => e.Kind == SupportTicketEntryKind.SupportAsChanged).Text);
        var candidate = Assert.Single(await CandidatesAsync(w));
        Assert.Equal(["Rodič", "Učitel"], candidate.Roles.Select(r => r.Name).Order());

        var claims = await TokenAsync(w, w.Teacher.Id);
        Assert.Equal(w.Teacher.Id.ToString(), Claim(claims, ClaimTypes.NameIdentifier));
        Assert.Equal("novak", Claim(claims, ClaimTypes.Name));
        Assert.Equal(w.SuperAdmin.Id.ToString(), Claim(claims, SupportClaims.SuperAdminId));
        Assert.Equal(w.Teacher.Id.ToString(), Claim(claims, SupportClaims.AsUserId));
        Assert.Equal("učitel Jan Novák", Claim(claims, SupportClaims.As));
        Assert.Equal("Učitel", Claim(claims, "roleName"));
        Assert.Contains("Rodič", Claim(claims, "roles"));
        Assert.DoesNotContain("Administration", Claim(claims, "perms"));
    }

    [Fact]
    public async Task NoLongerNamedTheSessionEndsWithTheAdminAllowedItIsTheAdminAgain()
    {
        var w = await SeedAsync();
        Assert.IsType<OkObjectResult>((await NameOnTicketAsync(w, w.Teacher.Id, w.Teacher2.Id)).Result);
        var first = int.Parse(Claim(await TokenAsync(w, w.Teacher2.Id), SupportClaims.SessionId)!);
        Assert.IsType<OkObjectResult>((await NameOnTicketAsync(w, w.Teacher.Id)).Result);
        using (var db = SuperAdminDb())
        {
            var session = db.SupportSessions.Single(s => s.Id == first);
            Assert.NotNull(session.EndedAt);
            Assert.Equal(SupportAsPolicy.ChangedReason, session.EndReason);
            Assert.Equal("Řehoř Ředitel", session.EndedBy);
        }

        var second = int.Parse(Claim(await TokenAsync(w, w.Teacher.Id), SupportClaims.SessionId)!);
        await SchoolAllowsAsync(w, SupportAsScope.Admin);
        using (var db = SuperAdminDb())
        {
            var session = db.SupportSessions.Single(s => s.Id == second);
            Assert.Null(session.EndedAt);
            Assert.Null(session.AsUserId);
            Assert.Null(session.AsName);
        }
    }

    [Fact]
    public async Task SwitchingWithinTheSessionGivesANewTokenAndTheOldOneStopsWorking()
    {
        var w = await SeedAsync();
        Assert.IsType<OkObjectResult>((await NameOnTicketAsync(w, w.Student.Id, w.Teacher.Id)).Result);
        var claims = await TokenAsync(w, w.Teacher.Id);
        var sessionId = int.Parse(Claim(claims, SupportClaims.SessionId)!);

        using (var db = SchoolDb(w.School.Id))
        {
            var support = Support(db, claims);
            var users = Assert.IsAssignableFrom<IReadOnlyList<SupportAsUserDto>>((await support.GetSessionUsers()).Value);
            // The author (the admin) isn't named; the rest by name.
            Assert.Equal([w.Teacher.Id, w.Student.Id], users.Select(u => u.Id));
            Assert.IsType<BadRequestObjectResult>((await support.SwitchAs(new SupportAsRequest(w.Teacher2.Id))).Result);
            // Not as the admin of no one.
            Assert.IsType<BadRequestObjectResult>((await support.SwitchAs(new SupportAsRequest())).Result);
            var switched = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(
                (await support.SwitchAs(new SupportAsRequest(w.Teacher.Id, w.RoleOf(UserRole.Parent).Id))).Result).Value);
            var newClaims = new JwtSecurityTokenHandler().ReadJwtToken(switched.Token).Claims.ToList();
            Assert.Equal("rodič Jan Novák", Claim(newClaims, SupportClaims.As));
            Assert.Equal(sessionId.ToString(), Claim(newClaims, SupportClaims.SessionId));
        }

        // The old token (in the teacher's role) is refused; the page gets a new one.
        var guarded = Http(claims);
        using var guardDb = SuperAdminDb();
        var called = false;
        await new SupportSessionGuardMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(guarded, guardDb, clock);
        Assert.False(called);
        Assert.Equal(StatusCodes.Status401Unauthorized, guarded.Response.StatusCode);
    }

    [Fact]
    public async Task TheUserAndTheTicketsAuthorSeeTheSupportInTheirAccount()
    {
        var w = await SeedAsync();
        Assert.IsType<OkObjectResult>((await NameOnTicketAsync(w, w.Teacher.Id)).Result);
        await TokenAsync(w, w.Teacher.Id);

        using var db = SchoolDb(w.School.Id);
        ClaimsPrincipal Of(User user) =>
            new(new ClaimsIdentity([.. UserClaims(user.Id, user.ActiveRoleId), new Claim("schoolId", w.School.Id.ToString())], "Test"));
        var mine = Assert.Single((await SupportPresence.OfAsync(db, Of(w.Teacher), clock.GetUtcNow()))!);
        Assert.True(mine.AsMe);
        Assert.Equal("superadmin", mine.SuperAdminName);
        Assert.Null(mine.TicketTitle);
        var author = Assert.Single((await SupportPresence.OfAsync(db, Of(w.Admin), clock.GetUtcNow()))!);
        Assert.False(author.AsMe);
        Assert.Equal("učitel Jan Novák", author.As);
        Assert.Equal("Rozvrh", author.TicketTitle);
        Assert.Null(await SupportPresence.OfAsync(db, Of(w.Teacher2), clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(false, false, false, null)]
    [InlineData(true, false, false, StatusCodes.Status403Forbidden)]  // a message in the user's name
    [InlineData(false, true, false, StatusCodes.Status204NoContent)]  // read stays unread
    [InlineData(true, false, true, null)]                             // staff may write an omluvenka
    public void AsAUserNoMessagesNorOmluvenkyAndReadingChangesNothing(bool asUser, bool marksRead, bool staff, int? status)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "42"), new(ClaimTypes.Role, staff ? "Staff" : "Parent"),
            new(SupportClaims.SessionId, "7"),
        ];
        if (asUser)
        {
            claims.Add(new Claim(SupportClaims.AsUserId, "42"));
        }
        var metadata = new List<object>
        {
            marksRead ? new MarksReadAttribute() : new NotAsUserInSupportAttribute("posílat zprávy") { FamilyOnly = staff },
        };
        var context = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
            new ActionContext(Http(claims), new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor { EndpointMetadata = metadata }),
            []);

        new SupportModeScopeFilter().OnAuthorization(context);

        Assert.Equal(status, context.Result switch
        {
            ObjectResult r => r.StatusCode,
            StatusCodeResult r => r.StatusCode,
            _ => null,
        });
    }
}
