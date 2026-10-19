using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Support;

public class SupportControllerTests
{
    // SuperAdmin requests bypass the tenant filters, same as in production.
    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private static SupportSessionService Sessions(AppDbContext db) => AuthTestServices.SupportSessions(db);

    private static SupportController As(AppDbContext db, User user) =>
        new(db, Sessions(db), AuthTestServices.RefreshTokens(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                    ])),
                },
            },
        };

    private static async Task<User> SuperAdminAsync(AppDbContext db, string username = "superadmin")
    {
        var user = new User { Username = username, PasswordHash = "x", Role = UserRole.SuperAdmin, SchoolId = null };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SupportCodeIsRedeemedOnceForAnAdminTokenAndTheCookieRefreshesOnlyThatSchool()
    {
        using var db = NewDb();
        var superAdmin = await SuperAdminAsync(db);
        var school = new School { Name = "ZŠ Test", Slug = "test" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var controller = As(db, superAdmin);
        var started = Assert.IsType<SupportSessionStartDto>(Assert.IsType<OkObjectResult>(
            (await controller.StartSupportSession(school.Id)).Result).Value);
        Assert.Equal("test", started.SchoolSlug);
        // Logged under the school, so its admin sees it too.
        var startAudit = Soovioo.Api.Audit.AuditContext.Of(controller.HttpContext);
        Assert.Equal(school.Id, startAudit.SchoolId);
        Assert.Equal(started.SessionId, startAudit.SupportSessionId);
        Assert.NotEqual(started.Code, Assert.Single(db.SupportSessions).HandoffCodeHash);

        // Redeemed on the school's page — anonymous there.
        var http = new DefaultHttpContext();
        var login = await Sessions(db).RedeemAsync(http, started.Code);
        Assert.NotNull(login);
        var redeemAudit = Soovioo.Api.Audit.AuditContext.Of(http);
        Assert.Equal(school.Id, redeemAudit.SchoolId);
        Assert.Equal(superAdmin.Id, redeemAudit.UserId);
        Assert.Equal("superadmin", redeemAudit.SupportBy);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login!.Token).Claims.ToList();
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(claims, c => c.Type == "schoolId" && c.Value == school.Id.ToString());
        Assert.Contains(claims, c => c.Type == SupportClaims.By && c.Value == "superadmin");
        var setCookie = http.Response.Headers.SetCookie.ToString();
        Assert.StartsWith(SupportSessionService.CookieName + "=", setCookie);
        var secret = setCookie.Split(';')[0].Split('=')[1];

        Assert.Null(await Sessions(db).RedeemAsync(new DefaultHttpContext(), started.Code));

        HttpRequest RefreshRequest()
        {
            var request = new DefaultHttpContext().Request;
            request.Headers.Cookie = $"{SupportSessionService.CookieName}={secret}";
            request.Headers[AuthSessionService.CsrfHeaderName] = "1";
            return request;
        }
        Assert.NotNull(await Sessions(db).RefreshAsync(RefreshRequest(), "test"));
        Assert.Null(await Sessions(db).RefreshAsync(RefreshRequest(), "jina-skola"));
        Assert.Null(await Sessions(db).RefreshAsync(RefreshRequest(), null));
    }

    [Fact]
    public async Task SuperAdminCanBeAddedButNotSelfOrLastDeleted()
    {
        using var db = NewDb();
        var me = await SuperAdminAsync(db);
        var controller = As(db, me);

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteSuperAdmin(me.Id));

        var added = Assert.IsType<SuperAdminDto>(Assert.IsType<OkObjectResult>(
            (await controller.AddSuperAdmin(new CreateSuperAdminRequest(
                "druhy", "heslo1234", "Jana", "Nová", "777 123 456", "jana@soovioo.cz"))).Result).Value);
        Assert.True(db.Users.Single(u => u.Id == added.Id).MustChangePassword);
        // A SuperAdmin is an ordinary account with a Person like everyone else.
        Assert.Equal("Jana Nová", added.Name);
        var person = db.Persons.Single(p => p.UserId == added.Id);
        Assert.Equal("jana@soovioo.cz", person.Email);

        var updated = Assert.IsType<SuperAdminDto>(Assert.IsType<OkObjectResult>(
            (await controller.UpdateSuperAdmin(added.Id, new UpdateSuperAdminRequest(
                "druha", FirstName: "Jana", LastName: "Malá"))).Result).Value);
        Assert.Equal("druha", updated.Username);
        Assert.Equal("Jana Malá", updated.Name);

        Assert.IsType<NoContentResult>(await controller.DeleteSuperAdmin(added.Id));
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task SupportSessionListsTheChangesFromTheAuditLogWithoutEnteringAndLeaving()
    {
        using var db = NewDb();
        var superAdmin = await SuperAdminAsync(db);
        var school = new School { Name = "ZŠ Test", Slug = "test" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var started = Assert.IsType<SupportSessionStartDto>(Assert.IsType<OkObjectResult>(
            (await As(db, superAdmin).StartSupportSession(school.Id)).Result).Value);
        db.AuditLogEntries.AddRange(
            new Soovioo.Api.Audit.AuditLogEntry
            {
                SupportSessionId = started.SessionId, Method = "POST", Path = "/api/grades", Area = "Známky",
                Description = "Zapsána známka", StatusCode = 200,
            },
            new Soovioo.Api.Audit.AuditLogEntry
            {
                SupportSessionId = started.SessionId, Method = "POST", Path = "/api/support-sessions/current/end",
                Area = "Podpora", StatusCode = 204,
            });
        await db.SaveChangesAsync();

        var sessions = Assert.IsAssignableFrom<IEnumerable<SupportSessionDto>>(Assert.IsType<OkObjectResult>(
            (await As(db, superAdmin).GetSupportSessions()).Result).Value);

        var action = Assert.Single(Assert.Single(sessions).Actions);
        Assert.Equal("Zapsána známka", action.Description);
    }

    [Fact]
    public async Task BuiltInSuperAdminCannotBeDeletedOrRenamedByAnotherSuperAdmin()
    {
        using var db = NewDb();
        var builtIn = await SuperAdminAsync(db, SupportController.BuiltInUsername);
        var other = await SuperAdminAsync(db, "druhy");
        var controller = As(db, other);

        var delete = await controller.DeleteSuperAdmin(builtIn.Id);
        var rename = await controller.UpdateSuperAdmin(builtIn.Id, new UpdateSuperAdminRequest("prejmenovany"));

        Assert.IsType<BadRequestObjectResult>(delete);
        Assert.IsType<BadRequestObjectResult>(rename.Result);
        Assert.Equal(SupportController.BuiltInUsername, db.Users.Single(u => u.Id == builtIn.Id).Username);
        // Editing its name/contacts is still fine.
        Assert.IsType<OkObjectResult>((await controller.UpdateSuperAdmin(
            builtIn.Id, new UpdateSuperAdminRequest(SupportController.BuiltInUsername, FirstName: "Hlavní"))).Result);
    }

    [Theory]
    [InlineData(null, false, true)]          // e.g. GET /api/students — [Authorize] only
    [InlineData("Teacher,Admin", false, true)]
    [InlineData("SuperAdmin", false, false)]
    [InlineData(null, true, false)]          // [AllowSuperAdmin]
    public void SuperAdminOnlyReachesEndpointsMeantForIt(string? roles, bool allowSuperAdmin, bool forbidden)
    {
        var metadata = new List<object> { new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Roles = roles } };
        if (allowSuperAdmin)
        {
            metadata.Add(new AllowSuperAdminAttribute());
        }
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "Test")),
        };
        var context = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
            new Microsoft.AspNetCore.Mvc.ActionContext(
                http, new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor { EndpointMetadata = metadata }),
            []);

        new SuperAdminScopeFilter().OnAuthorization(context);

        Assert.Equal(forbidden, context.Result is ForbidResult);
    }
}
