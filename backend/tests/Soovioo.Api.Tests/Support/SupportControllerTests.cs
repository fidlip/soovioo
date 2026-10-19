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

    private static SupportController As(AppDbContext db, User user) =>
        new(db, AuthTestServices.TokenFactory)
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
    public async Task SupportSessionTokenActsAsAdminOfTheSchoolAndIsLogged()
    {
        using var db = NewDb();
        var superAdmin = await SuperAdminAsync(db);
        var school = new School { Name = "ZŠ Test", Slug = "test" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var result = await As(db, superAdmin).StartSupportSession(school.Id);

        var dto = Assert.IsType<SupportSessionStartDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("test", dto.SchoolSlug);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(dto.Token).Claims.ToList();
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(claims, c => c.Type == "schoolId" && c.Value == school.Id.ToString());
        Assert.Contains(claims, c => c.Type == SupportClaims.By && c.Value == "superadmin");
        var session = Assert.Single(db.SupportSessions);
        Assert.Equal(school.Id, session.SchoolId);
        Assert.Equal(dto.SessionId, session.Id);
    }

    [Fact]
    public async Task SuperAdminCanBeAddedButNotSelfOrLastDeleted()
    {
        using var db = NewDb();
        var me = await SuperAdminAsync(db);
        var controller = As(db, me);

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteSuperAdmin(me.Id));

        var added = Assert.IsType<SuperAdminDto>(Assert.IsType<OkObjectResult>(
            (await controller.AddSuperAdmin(new CreateSuperAdminRequest("druhy", "heslo1234"))).Result).Value);
        Assert.True(db.Users.Single(u => u.Id == added.Id).MustChangePassword);

        Assert.IsType<NoContentResult>(await controller.DeleteSuperAdmin(added.Id));
        Assert.Single(db.Users);
    }

    [Theory]
    [InlineData("POST", "/api/grades", 1)]
    [InlineData("GET", "/api/grades", 0)]
    [InlineData("POST", "/api/support-sessions/current/end", 0)]
    public async Task AuditMiddlewareLogsOnlyChangesMadeInSupportMode(string method, string path, int expected)
    {
        using var db = NewDb();
        var middleware = new SupportAuditMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SupportClaims.SessionId, "7")], "Test")),
        };
        context.Request.Method = method;
        context.Request.Path = path;

        await middleware.InvokeAsync(context, db);

        Assert.Equal(expected, db.SupportActions.Count());
    }
}
