using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Support;

namespace Soovioo.Api.Tests.Audit;

public class AuditTests
{
    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private static ClaimsPrincipal Principal(int userId, string username, string role, int? schoolId, params Claim[] extra)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            .. extra,
        ];
        if (schoolId is not null)
        {
            claims.Add(new Claim("schoolId", schoolId.ToString()!));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static DefaultHttpContext Request(
        string method, string path, string? json = null, ClaimsPrincipal? user = null,
        string? controller = null, string? action = null)
    {
        var context = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        context.Request.Method = method;
        context.Request.Path = path;
        if (json is not null)
        {
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        }
        context.Response.Body = new MemoryStream();
        if (controller is not null)
        {
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(
                new ControllerActionDescriptor { ControllerName = controller, ActionName = action! }), "test"));
        }
        return context;
    }

    private static AuditMiddleware Middleware(RequestDelegate next) =>
        new(next, NullLogger<AuditMiddleware>.Instance);

    [Theory]
    [InlineData("POST", "/api/grades", 1)]
    [InlineData("DELETE", "/api/grades/3", 1)]
    [InlineData("GET", "/api/grades", 0)]
    [InlineData("POST", "/api/auth/refresh", 0)]
    [InlineData("POST", "/hangfire/x", 0)]
    public async Task LogsChangesOnlyNotReadsOrTokenRefreshes(string method, string path, int expected)
    {
        using var db = NewDb();

        await Middleware(_ => Task.CompletedTask).InvokeAsync(Request(method, path), db);

        Assert.Equal(expected, db.AuditLogEntries.Count());
    }

    [Fact]
    public async Task EntryNamesWhoAndWhatAndMasksPasswords()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, 1), SchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var context = Request("POST", "/api/users",
            """{"username":"knovy","password":"tajne123","firstName":"Karel","lastName":"Nový","nested":{"newPassword":"x"}}""",
            Principal(admin.Id, "admin", "Admin", 1), "Users", "AddUser");

        await Middleware(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("""{"id":42,"username":"knovy"}""");
        }).InvokeAsync(context, db);

        var entry = Assert.Single(db.AuditLogEntries);
        Assert.Equal("Vytvořen uživatel Karel Nový", entry.Description);
        Assert.Equal("Vytvořen uživatel", entry.Action);
        Assert.Equal("Uživatelé", entry.Area);
        Assert.Equal(1, entry.SchoolId);
        Assert.Equal(admin.Id, entry.UserId);
        Assert.Equal("ID 42", entry.Result);
        Assert.DoesNotContain("tajne123", entry.RequestBody);
        Assert.Contains("\"password\":\"***\"", entry.RequestBody);
        Assert.Contains("\"newPassword\":\"***\"", entry.RequestBody);
        Assert.Contains("Karel", entry.RequestBody);
        // The response still reaches the client.
        context.Response.Body.Position = 0;
        Assert.Contains("knovy", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task StudentInPayloadGivesTheClassAndTheNameAndErrorsKeepTheirMessage()
    {
        using var db = NewDb();
        db.Students.Add(new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = 1 }, Id = 5, SchoolClassId = 9, SchoolId = 1 });
        await db.SaveChangesAsync();
        var context = Request("POST", "/api/grades", """{"studentId":5,"value":1}""",
            Principal(2, "ucitel", "Teacher", 1), "Grades", "AddGrade");

        await Middleware(async ctx =>
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("""{"title":"One or more validation errors occurred.","errors":{"value":["Známka musí být 1–5."]}}""");
        }).InvokeAsync(context, db);

        var entry = Assert.Single(db.AuditLogEntries);
        Assert.Equal(9, entry.SchoolClassId);
        Assert.Equal("Zapsána známka – Jan Novák (neprovedeno)", entry.Description);
        Assert.Equal(400, entry.StatusCode);
        Assert.Equal("Známka musí být 1–5.", entry.Result);
    }

    [Fact]
    public async Task SupportModeIsFlaggedAndRouteIdNamesADeletion()
    {
        using var db = NewDb();
        var context = Request("DELETE", "/api/classrooms/4", user: Principal(1, "superadmin", "Admin", 3,
            new Claim(SupportClaims.SessionId, "7"), new Claim(SupportClaims.By, "superadmin")),
            controller: "Classrooms", action: "DeleteClassroom");
        context.Request.RouteValues["id"] = "4";

        await Middleware(ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; }).InvokeAsync(context, db);

        var entry = Assert.Single(db.AuditLogEntries);
        Assert.Equal(7, entry.SupportSessionId);
        Assert.Equal("superadmin", entry.SupportBy);
        Assert.Equal(3, entry.SchoolId);
        Assert.Equal("Smazána učebna #4", entry.Description);
    }

    [Fact]
    public async Task FailedLoginIsLoggedWithTheTriedUsernameAndWithoutThePassword()
    {
        using var db = NewDb();
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
        db.Schools.Add(new School { Id = 1, Name = "ZŠ", Slug = "demo" });
        var user = new User { Username = "rodic", PasswordHash = "", ActiveRole = db.Role(UserRole.Parent, 1), SchoolId = 1 };
        user.PasswordHash = hasher.HashPassword(user, "spravne");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var context = Request("POST", "/api/auth/login",
            """{"username":"rodic","password":"spatne","schoolSlug":"demo"}""", controller: "Auth", action: "Login");
        var controller = new AuthController(db, Soovioo.Api.Tests.Auth.AuthTestServices.Sessions(db), Soovioo.Api.Tests.Auth.AuthTestServices.RefreshTokens(db),
            Soovioo.Api.Tests.Auth.AuthTestServices.SupportSessions(db))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        await Middleware(async ctx =>
        {
            var result = await controller.Login(new LoginRequest("rodic", "spatne", "demo"));
            ctx.Response.StatusCode = result.Result is UnauthorizedResult ? 401 : 200;
        }).InvokeAsync(context, db);

        var entry = Assert.Single(db.AuditLogEntries);
        Assert.Equal("Neúspěšné přihlášení (rodic)", entry.Description);
        Assert.Equal("Neúspěšné přihlášení", entry.Action);
        Assert.Equal(user.Id, entry.UserId);
        Assert.Equal(1, entry.SchoolId);
        Assert.DoesNotContain("spatne", entry.RequestBody);
    }

    private static AuditLogController LogController(AppDbContext db, ClaimsPrincipal user) =>
        new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } } };

    private static AuditLogEntry Entry(
        int? schoolId, int userId = 1, string area = "Známky", string? action = null, string? description = null) =>
        new()
        {
            SchoolId = schoolId, UserId = userId, Username = $"u{userId}", Method = "POST", Path = "/api/x", Area = area,
            StatusCode = 200, Action = action, Description = description,
        };

    [Fact]
    public async Task ActionPickerOffersActionsUnderTheOtherFiltersAndEntriesMatchExactlyOrByContainedText()
    {
        using var db = NewDb();
        db.AuditLogEntries.AddRange(
            Entry(1, userId: 1, area: "Uživatelé", action: "Vytvořen uživatel", description: "Vytvořen uživatel Karel Nový"),
            Entry(1, userId: 1, area: "Uživatelé", action: "Vytvořen uživatel", description: "Vytvořen uživatel Eva Malá"),
            Entry(1, userId: 2, area: "Učebny", action: "Vytvořena učebna", description: "Vytvořena učebna 101"),
            Entry(1, userId: 2, area: "Učebny", action: "Smazána učebna", description: "Smazána učebna #4"),
            Entry(2, userId: 3, area: "Učebny", action: "Vytvořena učebna", description: "Vytvořena učebna jiná škola"));
        await db.SaveChangesAsync();
        var admin = LogController(db, Principal(5, "admin", "Admin", 1));

        async Task<IReadOnlyList<string>> Actions(AuditLogFilter filter, string? search) =>
            Assert.IsAssignableFrom<IReadOnlyList<string>>(Assert.IsType<OkObjectResult>(
                (await admin.GetActions(filter, search)).Result).Value);
        async Task<PagedResult<AuditLogEntryDto>> Entries(AuditLogFilter filter) =>
            Assert.IsType<PagedResult<AuditLogEntryDto>>(Assert.IsType<OkObjectResult>(
                (await admin.GetEntries(filter)).Result).Value);

        Assert.Equal(["Vytvořen uživatel", "Vytvořena učebna"], await Actions(new AuditLogFilter(), "vytv"));
        Assert.Equal(["Smazána učebna", "Vytvořena učebna"], await Actions(new AuditLogFilter(UserId: 2), null));

        Assert.Equal(2, (await Entries(new AuditLogFilter(Action: "Vytvořen uživatel"))).TotalCount);
        Assert.Equal(3, (await Entries(new AuditLogFilter(ActionSearch: "Vytv"))).TotalCount);
        // Typed text also finds the subject inside the description.
        Assert.Equal("Vytvořen uživatel Karel Nový",
            Assert.Single((await Entries(new AuditLogFilter(ActionSearch: "karel"))).Items).Description);
    }

    [Fact]
    public async Task SchoolAdminSeesOnlyTheirOwnSchoolWhateverTheyAskFor()
    {
        using var db = NewDb();
        db.AuditLogEntries.AddRange(Entry(1), Entry(1, area: "Platby"), Entry(2), Entry(null));
        await db.SaveChangesAsync();

        var adminView = Assert.IsType<PagedResult<AuditLogEntryDto>>(Assert.IsType<OkObjectResult>(
            (await LogController(db, Principal(5, "admin", "Admin", 1)).GetEntries(new AuditLogFilter(SchoolId: 2))).Result).Value);
        var superView = Assert.IsType<PagedResult<AuditLogEntryDto>>(Assert.IsType<OkObjectResult>(
            (await LogController(db, Principal(9, "superadmin", "SuperAdmin", null)).GetEntries(new AuditLogFilter())).Result).Value);
        var filtered = Assert.IsType<PagedResult<AuditLogEntryDto>>(Assert.IsType<OkObjectResult>(
            (await LogController(db, Principal(5, "admin", "Admin", 1)).GetEntries(new AuditLogFilter(Area: "Platby"))).Result).Value);

        Assert.Equal(2, adminView.TotalCount);
        Assert.All(adminView.Items, e => Assert.Equal(1, e.SchoolId));
        Assert.Equal(4, superView.TotalCount);
        Assert.Equal("Platby", Assert.Single(filtered.Items).Area);
    }

    [Fact]
    public async Task CleanupDeletesEntriesOlderThanAYear()
    {
        using var db = NewDb();
        var clock = new Soovioo.Api.Tests.Auth.FakeClock(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero));
        var old = Entry(1);
        old.At = clock.GetUtcNow().AddDays(-366);
        var recent = Entry(1);
        recent.At = clock.GetUtcNow().AddDays(-300);
        db.AuditLogEntries.AddRange(old, recent);
        await db.SaveChangesAsync();

        Assert.Equal(1, await new AuditLogCleanupJob(db, clock).RunAsync());

        Assert.Equal(recent.Id, Assert.Single(db.AuditLogEntries).Id);
    }
}
