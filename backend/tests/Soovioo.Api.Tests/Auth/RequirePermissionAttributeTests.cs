using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Auth;

/// <summary>The gate on the endpoints (<see cref="RequirePermissionAttribute"/>)
/// and what it asks: whose role the token names, still had, in which school.</summary>
public class RequirePermissionAttributeTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb(string? name = null, int schoolId = SchoolId, IServiceProvider? services = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString());
        if (services is not null)
        {
            options.UseApplicationServiceProvider(services);
        }
        return new AppDbContext(options.Options, new FakeCurrentSchoolContext(schoolId));
    }

    /// <summary>The filter's verdict for <paramref name="user"/> on an endpoint needing <paramref name="permission"/>.</summary>
    private static async Task<IActionResult?> GateAsync(AppDbContext db, ClaimsPrincipal user, Permission permission)
    {
        var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new RequirePermissionAttribute(permission).OnAuthorizationAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task LetsThroughWhoHasThePermissionInAnyScope()
    {
        using var db = NewDb();

        Assert.Null(await GateAsync(db, TestRoles.Principal(db, 7, UserRole.Teacher), Permission.GradesWrite));
        Assert.Null(await GateAsync(db, TestRoles.Principal(db, 8, UserRole.Parent), Permission.GradesRead));
    }

    [Theory]
    [InlineData(UserRole.Teacher, Permission.Timetable)]
    [InlineData(UserRole.Teacher, Permission.Administration)]
    [InlineData(UserRole.Teacher, Permission.MessagesBroadcastSchool)]
    [InlineData(UserRole.Parent, Permission.GradesWrite)]
    [InlineData(UserRole.Parent, Permission.MessagesBroadcastClass)]
    [InlineData(UserRole.Student, Permission.AbsencesExcuse)]
    [InlineData(UserRole.Admin, Permission.Administration)]
    public async Task ForbidsWhoLacksIt(UserRole role, Permission permission)
    {
        using var db = NewDb();

        var result = await GateAsync(db, TestRoles.Principal(db, 7, role), permission);

        Assert.Equal(role == UserRole.Admin, result is null);
        Assert.Equal(role != UserRole.Admin, result is ForbidResult);
    }

    [Fact]
    public async Task ATokenWithoutARoleGetsNothing()
    {
        using var db = NewDb();
        db.Role(UserRole.Admin);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "Staff")], "Test"));

        Assert.IsType<ForbidResult>(await GateAsync(db, user, Permission.GradesRead));
    }

    [Fact]
    public async Task ARoleTheAccountDoesntHaveCountsForNothing()
    {
        using var db = NewDb();
        TestRoles.Principal(db, 7, UserRole.Teacher);
        var adminRole = db.Role(UserRole.Admin);
        // A forged or stale token naming the Admin role for a teacher.
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "Staff"),
            new Claim(PermissionAccess.RoleIdClaim, adminRole.Id.ToString()),
        ], "Test"));

        Assert.IsType<ForbidResult>(await GateAsync(db, user, Permission.Administration));
    }

    [Fact]
    public async Task ARoleOfAnotherSchoolCountsForNothing()
    {
        var name = Guid.NewGuid().ToString();
        int otherAdminId;
        using (var seed = NewDb(name, schoolId: 2))
        {
            TestRoles.Principal(seed, 7, UserRole.Admin, schoolId: 2);
            otherAdminId = seed.Role(UserRole.Admin, 2).Id;
        }
        using var db = NewDb(name, schoolId: SchoolId);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "Staff"),
            new Claim(PermissionAccess.RoleIdClaim, otherAdminId.ToString()),
        ], "Test"));

        Assert.IsType<ForbidResult>(await GateAsync(db, user, Permission.Administration));
    }

    [Fact]
    public async Task SupportModeActsAsTheSchoolsAdminOnly()
    {
        using var db = NewDb();
        Claim[] SupportClaims(Role role) =>
        [
            new(ClaimTypes.NameIdentifier, "99"), new(ClaimTypes.Role, "Staff"),
            new(PermissionAccess.RoleIdClaim, role.Id.ToString()), new(Soovioo.Api.Support.SupportClaims.SessionId, "1"),
        ];

        Assert.Null(await GateAsync(db, new ClaimsPrincipal(new ClaimsIdentity(SupportClaims(db.Role(UserRole.Admin)), "Test")), Permission.Administration));
        Assert.IsType<ForbidResult>(await GateAsync(db, new ClaimsPrincipal(new ClaimsIdentity(SupportClaims(db.Role(UserRole.Teacher)), "Test")), Permission.GradesRead));
    }

    [Fact]
    public async Task SuperAdminAndAnonymousAreLeftToTheOtherGates()
    {
        using var db = NewDb();
        var superAdmin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SuperAdmin")], "Test"));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(await GateAsync(db, superAdmin, Permission.AuditLog));
        Assert.Null(await GateAsync(db, anonymous, Permission.AuditLog));
    }

    [Fact]
    public async Task ChangingARoleClearsTheCacheSoItAppliesToTheNextRequest()
    {
        var cache = new RolePermissionCache(TimeProvider.System);
        var services = new ServiceCollection().AddSingleton(cache).BuildServiceProvider();
        var name = Guid.NewGuid().ToString();
        using var db = NewDb(name, services: services);
        var token = TestRoles.Principal(db, 7, UserRole.Teacher);
        Assert.True(await new ClaimsPrincipal(token.Identities).HasPermissionAsync(db, Permission.GradesRead));

        var row = await db.RolePermissions.SingleAsync(p => p.RoleId == db.Role(UserRole.Teacher, SchoolId).Id && p.Permission == Permission.GradesRead);
        row.Scope = PermissionScope.None;
        await db.SaveChangesAsync();

        Assert.False(await new ClaimsPrincipal(token.Identities).HasPermissionAsync(db, Permission.GradesRead));
    }

    [Fact]
    public async Task TheCacheAnswersWithoutTheDatabaseUntilSomethingChanges()
    {
        var cache = new RolePermissionCache(TimeProvider.System);
        var services = new ServiceCollection().AddSingleton(cache).BuildServiceProvider();
        using var db = NewDb(services: services);
        var token = TestRoles.Principal(db, 7, UserRole.Teacher);
        await new ClaimsPrincipal(token.Identities).PermissionsAsync(db);

        var roleId = db.Role(UserRole.Teacher, SchoolId).Id;
        Assert.True(cache.TryGet((7, roleId, false), out var cached));
        Assert.Equal(PermissionScope.MyClasses, cached[Permission.GradesRead]);
    }

    [Fact]
    public void TheTokenCarriesKindRoleAndPermissions()
    {
        var role = new Role { Id = 5, Name = "Učitel", Kind = RoleKind.Staff, BuiltIn = UserRole.Teacher };
        var user = new User { Id = 7, Username = "ucitel", PasswordHash = "", SchoolId = SchoolId, ActiveRole = role };
        user.Roles.Add(new Role { Id = 6, Name = "Rodič", Kind = RoleKind.Parent, BuiltIn = UserRole.Parent });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(AuthTestServices.TokenFactory.CreateToken(
            user, new School { Name = "", Slug = "" },
            new Dictionary<Permission, PermissionScope> { [Permission.GradesRead] = PermissionScope.MyClasses }));
        string Claim(string type) => token.Claims.First(c => c.Type == type).Value;

        Assert.Equal("Staff", token.Claims.First(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.Equal("5", Claim("roleId"));
        Assert.Equal("Učitel", Claim("roleName"));
        Assert.Equal("GradesRead:MyClasses", Claim("perms"));
        Assert.Contains("\"name\":\"Rodič\"", Claim("roles"));
    }

    [Fact]
    public void ASuperAdminsTokenHasNoSchoolRole()
    {
        var user = new User { Id = 1, Username = "superadmin", PasswordHash = "", IsSuperAdmin = true };

        var token = new JwtSecurityTokenHandler().ReadJwtToken(AuthTestServices.TokenFactory.CreateToken(user));

        Assert.Equal("SuperAdmin", token.Claims.First(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, c => c.Type == "roleId" || c.Type == "perms");
    }

    [Fact]
    public async Task ThePupilsRoleFollowsTheSchoolTypeUnlessRenamed()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        await db.SaveChangesAsync();
        var pupils = db.Role(UserRole.Student);
        var controller = new MySchoolController(db, new FakeCurrentSchoolContext(SchoolId));

        await controller.SetType(new SetSchoolTypeRequest(SchoolType.Secondary));
        Assert.Equal("Student", pupils.Name);

        pupils.Name = "Žactvo";
        await db.SaveChangesAsync();
        await controller.SetType(new SetSchoolTypeRequest(SchoolType.Elementary));
        Assert.Equal("Žactvo", pupils.Name);
    }
}
