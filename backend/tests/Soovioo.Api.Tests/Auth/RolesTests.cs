using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Notifications;
using Soovioo.Api.Payments;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Auth;

public class RolesTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static RolesController Roles(AppDbContext db) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(db, 999, UserRole.Admin) };

    private static GradesController Grades(AppDbContext db, System.Security.Claims.ClaimsPrincipal user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)) { ControllerContext = TestRoles.Context(user) };

    /// <summary>5.A with Jan, 5.B with Eva; maths and Czech.</summary>
    private sealed record World(SchoolClass A, SchoolClass B, StudentProfile Jan, StudentProfile Eva, Subject Maths, Subject Czech);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var a = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        var b = new SchoolClass { Name = "5.B", SchoolId = SchoolId };
        var maths = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var czech = new Subject { Name = "Čeština", SchoolId = SchoolId };
        db.AddRange(a, b, maths, czech);
        await db.SaveChangesAsync();
        var jan = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = a.Id, SchoolId = SchoolId };
        var eva = new StudentProfile { Person = new Person { FirstName = "Eva", LastName = "Malá", SchoolId = SchoolId }, SchoolClassId = b.Id, SchoolId = SchoolId };
        db.Students.AddRange(jan, eva);
        await db.SaveChangesAsync();
        return new World(a, b, jan, eva, maths, czech);
    }

    [Fact]
    public void BuiltInRolesGetTheirDefaults()
    {
        using var db = NewDb();

        Assert.All(db.Role(UserRole.Admin).Permissions, p => Assert.Equal(PermissionScope.School, p.Scope));
        Assert.Equal(PermissionCatalog.All.Count, db.Role(UserRole.Parent).Permissions.Count);
        var teacher = db.Role(UserRole.Teacher).Permissions.ToDictionary(p => p.Permission, p => p.Scope);
        Assert.Equal(PermissionScope.MyClasses, teacher[Permission.GradesWrite]);
        Assert.Equal(PermissionScope.None, teacher[Permission.Timetable]);
        Assert.Equal(PermissionScope.ClassTeacher, teacher[Permission.Students]);
    }

    [Fact]
    public async Task AdminAlwaysHasAdministrationOthersNever()
    {
        using var db = NewDb();

        Assert.True(await TestRoles.Principal(db, 1, UserRole.Admin).HasPermissionAsync(db, Permission.Administration));
        Assert.False(await TestRoles.Principal(db, 2, UserRole.Teacher).HasPermissionAsync(db, Permission.Administration));
    }

    [Fact]
    public async Task ATeacherGradesOnlyTheSubjectsTheyTeachInTheirClasses()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        db.Teaches(7, w.A.Id, w.Maths.Id);
        var teacher = TestRoles.Principal(db, 7, UserRole.Teacher);

        Assert.IsType<CreatedAtActionResult>((await Grades(db, teacher).AddGrade(new(w.Jan.Id, w.Maths.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(db, teacher).AddGrade(new(w.Jan.Id, w.Czech.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(db, teacher).AddGrade(new(w.Eva.Id, w.Maths.Id, 1, 1, null))).Result);
    }

    [Fact]
    public async Task ARoleOfTheSchoolsOwnSeesOnlyWhatItWasGiven()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        db.Grades.AddRange(
            new Grade { StudentId = w.Jan.Id, SubjectId = w.Maths.Id, Value = 1, SchoolId = SchoolId },
            new Grade { StudentId = w.Eva.Id, SubjectId = w.Maths.Id, Value = 2, SchoolId = SchoolId });
        await db.SaveChangesAsync();
        var created = Assert.IsType<RoleDto>(Assert.IsType<CreatedAtActionResult>(
            (await Roles(db).AddRole(new CreateRoleRequest("Asistent pedagoga", RoleKind.Staff))).Result).Value);
        var permissions = PermissionCatalog.All.ToDictionary(i => i.Permission, _ => PermissionScope.None);
        permissions[Permission.GradesRead] = PermissionScope.MyClasses;
        Assert.IsType<OkObjectResult>((await Roles(db).UpdateRole(created.Id, new UpdateRoleRequest("Asistent pedagoga", permissions))).Result);
        var assistant = new User { Username = "asistent", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = await db.Roles.FindAsync(created.Id) };
        db.Users.Add(assistant);
        await db.SaveChangesAsync();
        db.Teaches(assistant.Id, w.A.Id, w.Maths.Id);
        var principal = TestRoles.PrincipalOf(assistant);

        var grades = Assert.IsType<PagedResult<GradeDto>>(
            Assert.IsType<OkObjectResult>((await Grades(db, principal).GetGrades()).Result).Value).Items;
        Assert.Equal([w.Jan.Id], grades.Select(g => g.StudentId));
        Assert.IsType<ForbidResult>((await Grades(db, principal).AddGrade(new(w.Jan.Id, w.Maths.Id, 1, 1, null))).Result);
    }

    [Fact]
    public async Task TakingTheRoleAwayAppliesToTheNextRequest()
    {
        using var db = NewDb();
        var principal = TestRoles.Principal(db, 7, UserRole.Teacher);
        var user = await db.Users.SingleAsync(u => u.Id == 7);
        user.Roles.Clear();
        user.ActiveRole = db.Role(UserRole.Parent);
        await db.SaveChangesAsync();

        // Same token, next request.
        var next = new System.Security.Claims.ClaimsPrincipal(principal.Identities);
        Assert.False(await next.HasPermissionAsync(db, Permission.GradesRead));
    }

    [Fact]
    public async Task AScopeTheKindCantHaveIsRefused()
    {
        using var db = NewDb();
        var parents = db.Role(UserRole.Parent);

        var result = await Roles(db).UpdateRole(parents.Id, new UpdateRoleRequest("Rodič",
            new Dictionary<Permission, PermissionScope> { [Permission.GradesWrite] = PermissionScope.Own }));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ACopyKeepsOnlyWhatItsKindAllows()
    {
        using var db = NewDb();

        var result = await Roles(db).AddRole(new CreateRoleRequest("Zákonný zástupce", RoleKind.Parent, db.Role(UserRole.Admin).Id));

        var role = Assert.IsType<RoleDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.All(role.Permissions, p => Assert.Equal(PermissionScope.None, p.Value));
    }

    [Fact]
    public async Task BuiltInRolesCantBeDeleted()
    {
        using var db = NewDb();

        Assert.IsType<BadRequestObjectResult>(await Roles(db).DeleteRole(db.Role(UserRole.Teacher).Id));
    }

    [Fact]
    public async Task DeletingARoleSomebodyHasMovesThemToAnotherOfTheSameKind()
    {
        using var db = NewDb();
        var created = Assert.IsType<RoleDto>(Assert.IsType<CreatedAtActionResult>(
            (await Roles(db).AddRole(new CreateRoleRequest("Vychovatel", RoleKind.Staff))).Result).Value);
        var educator = new User { Username = "vychovatel", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = await db.Roles.FindAsync(created.Id) };
        db.Users.Add(educator);
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await Roles(db).DeleteRole(created.Id));
        Assert.IsType<BadRequestObjectResult>(await Roles(db).DeleteRole(created.Id, db.Role(UserRole.Parent).Id));
        Assert.IsType<NoContentResult>(await Roles(db).DeleteRole(created.Id, db.Role(UserRole.Teacher).Id));

        var moved = await db.Users.SingleAsync(u => u.Id == educator.Id);
        Assert.Equal(UserRole.Teacher, moved.ActiveRole!.BuiltIn);
        Assert.Equal([UserRole.Teacher], moved.Roles.Select(r => r.BuiltIn));
        Assert.False(await db.Roles.AnyAsync(r => r.Id == created.Id));
    }

    [Fact]
    public async Task ATeacherCantBillTheWholeSchool()
    {
        using var db = NewDb();
        var controller = new PaymentsController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(db, 7, UserRole.Teacher),
        };

        var result = await controller.CreatePayment(new CreatePaymentRequest(
            "Výlet", null, new DateOnly(2026, 11, 1), PaymentAmountType.FixedPerStudent, 100, PaymentRecurrence.None, PaymentAudience.Everyone));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ATeacherChangesOnlyTheirOwnEvents()
    {
        using var db = NewDb();
        var @event = new Event
        {
            Title = "Ředitelské volno", SchoolId = SchoolId, CreatedByUserId = 999,
            StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync();
        var controller = new EventsController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = TestRoles.Context(db, 7, UserRole.Teacher),
        };

        Assert.IsType<ForbidResult>(await controller.DeleteEvent(@event.Id));
    }

    [Fact]
    public async Task AParentsRepresentativeWritesToTheirChildsClassOnly()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var rep = Assert.IsType<RoleDto>(Assert.IsType<CreatedAtActionResult>(
            (await Roles(db).AddRole(new CreateRoleRequest("Třídní důvěrník", RoleKind.Parent, db.Role(UserRole.Parent).Id))).Result).Value);
        var permissions = new Dictionary<Permission, PermissionScope>(rep.Permissions)
        {
            [Permission.MessagesBroadcastClass] = PermissionScope.Own,
        };
        Assert.IsType<OkObjectResult>((await Roles(db).UpdateRole(rep.Id, new UpdateRoleRequest(rep.Name, permissions))).Result);
        var parent = new User { Username = "duvernik", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = await db.Roles.FindAsync(rep.Id) };
        db.Users.Add(parent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, w.Jan.Id, SchoolId);
        await db.SaveChangesAsync();
        var controller = new Soovioo.Api.Messaging.MessageBroadcastController(
            db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(parent)),
        };

        var classes = Assert.IsAssignableFrom<IEnumerable<SchoolClass>>(
            Assert.IsType<OkObjectResult>((await controller.GetBroadcastableClasses()).Result).Value);
        Assert.Equal([w.A.Id], classes.Select(c => c.Id));
        Assert.IsType<ForbidResult>((await controller.BroadcastToClass(w.B.Id, new("Výlet", "Text"))).Result);
    }

    [Fact]
    public void TheCacheForgetsAfterAMinute()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
        var cache = new RolePermissionCache(clock);
        cache.Set((1, 2, false), new Dictionary<Permission, PermissionScope> { [Permission.GradesRead] = PermissionScope.Own });

        Assert.True(cache.TryGet((1, 2, false), out _));
        clock.Now = clock.Now.AddSeconds(61);
        Assert.False(cache.TryGet((1, 2, false), out _));
    }
}
