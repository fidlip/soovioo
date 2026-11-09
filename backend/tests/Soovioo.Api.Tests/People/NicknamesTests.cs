using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.People;

public class NicknamesTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static Nicknames Service(AppDbContext db) =>
        new(db, new Notifier(db, TimeProvider.System), new NameLabels(db));

    private static MyProfileController Profile(AppDbContext db, User user) =>
        new(db, AuthTestServices.Sessions(db), AuthTestServices.RefreshTokens(db)) { ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(user)) };

    private static MyChildrenController Children(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(user)) };

    private static StudentsController Students(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(user)) };

    private sealed record Fixture(
        User Admin, User ClassTeacher, User OtherTeacher, User Parent, User Pupil, User OrphanPupil,
        StudentProfile Child, StudentProfile Orphan, StudentProfile OtherClass);

    /// <summary>1.A (class teacher) with the parent's child Jan (own
    /// account) and Ota (own account, no parent in the app); 2.B with Eva,
    /// whom the class teacher of 1.A doesn't teach.</summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db, VisibilityScope teachers = VisibilityScope.MyClasses)
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        db.VisibilityRules.Add(new VisibilityRule
        {
            SchoolId = SchoolId, Owner = VisibilityOwner.Student, Field = VisibilityField.Nickname, Audience = VisibilityAudience.Teachers,
            Min = VisibilityScope.Nobody, Default = teachers, Max = VisibilityScope.School,
        });
        db.VisibilityRules.Add(new VisibilityRule
        {
            SchoolId = SchoolId, Owner = VisibilityOwner.Parent, Field = VisibilityField.Nickname, Audience = VisibilityAudience.Students,
            Min = VisibilityScope.Nobody, Default = VisibilityScope.MyClasses, Max = VisibilityScope.MyClasses,
        });
        User NewUser(string name, UserRole role) =>
            new() { Username = name, PasswordHash = "x", ActiveRole = db.Role(role, SchoolId), SchoolId = SchoolId };
        var admin = NewUser("admin", UserRole.Admin);
        var classTeacher = NewUser("tridni", UserRole.Teacher);
        var otherTeacher = NewUser("ucitel", UserRole.Teacher);
        var parent = NewUser("rodic", UserRole.Parent);
        var pupil = NewUser("zak", UserRole.Student);
        var orphanPupil = NewUser("ota", UserRole.Student);
        db.Users.AddRange(admin, classTeacher, otherTeacher, parent, pupil, orphanPupil);
        await db.SaveChangesAsync();
        db.Persons.Add(new Person { UserId = parent.Id, SchoolId = SchoolId, FirstName = "Petra", LastName = "Malá", Nickname = "Mamka" });
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = classTeacher.Id };
        var classB = new SchoolClass { Name = "2.B", SchoolId = SchoolId, ClassTeacherId = otherTeacher.Id };
        db.SchoolClasses.AddRange(classA, classB);
        await db.SaveChangesAsync();
        StudentProfile Pupil(string first, SchoolClass c, int? userId) => new()
        {
            Person = new Person { FirstName = first, LastName = "Malý", UserId = userId, SchoolId = SchoolId },
            SchoolClassId = c.Id, SchoolId = SchoolId,
        };
        var child = Pupil("Jakub", classA, pupil.Id);
        var orphan = Pupil("Ota", classA, orphanPupil.Id);
        var otherClass = Pupil("Eva", classB, null);
        db.Students.AddRange(child, orphan, otherClass);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.SaveChangesAsync();
        return new Fixture(admin, classTeacher, otherTeacher, parent, pupil, orphanPupil, child, orphan, otherClass);
    }

    private static async Task<Dictionary<int, NameLabel>> LabelsAsync(AppDbContext db, User viewer, params StudentProfile[] students) =>
        await db.StudentLabelsAsync(TestRoles.PrincipalOf(viewer), students.Select(s => s.Id));

    [Theory]
    [InlineData("  Kuba  ", "Kuba", null)]
    [InlineData("Malý   Kuba-2", "Malý Kuba-2", null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    [InlineData("Kuba!", null, "Přezdívka může obsahovat jen písmena, číslice, mezery a pomlčky.")]
    [InlineData("1234567890123456789012345678901", null, "Přezdívka může mít nejvýš 30 znaků.")]
    public void NormalizeTrimsAndChecks(string? input, string? value, string? error)
    {
        Assert.Equal((value, error), Nicknames.Normalize(input));
    }

    [Fact]
    public async Task APupilsProposalWaitsForTheParentAndTheOldOneStaysUntilThen()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.Nickname = "Kuba";
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>((await Profile(db, w.Pupil).SetNickname(new SetNicknameRequest("Kubík"), Service(db))).Result);
        await db.SaveChangesAsync();

        Assert.Equal("Kuba", w.Child.Person.Nickname);
        Assert.Equal("Kubík", w.Child.Person.NicknameProposal);
        var request = Assert.Single(await db.Notifications.Where(n => n.UserId == w.Parent.Id).ToListAsync());
        Assert.Equal(NotificationType.Nickname, request.Type);
        Assert.Equal("Kuba si chce nastavit přezdívku „Kubík“.", request.Body);
        Assert.Equal("my-children?student=" + w.Child.Id, NotificationLinks.For(request));
        Assert.Empty(await db.Notifications.Where(n => n.UserId == w.ClassTeacher.Id).ToListAsync());

        var children = Assert.IsType<OkObjectResult>((await Children(db, w.Parent).ApproveNickname(w.Child.Id, Service(db))).Result);
        var child = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MyChildDto>>(children.Value));
        Assert.Equal("Kubík", child.Nickname);
        Assert.Null(child.NicknameProposal);
        Assert.Empty(await db.Notifications.Where(n => n.Type == NotificationType.Nickname).ToListAsync());
    }

    [Fact]
    public async Task ARejectedProposalGoesAndRemovingOnesOwnAppliesRightAway()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.Nickname = "Kuba";
        w.Child.Person.NicknameProposal = "Kubík";
        await db.SaveChangesAsync();

        await Children(db, w.Parent).RejectNickname(w.Child.Id, Service(db));
        Assert.Equal("Kuba", w.Child.Person.Nickname);
        Assert.Null(w.Child.Person.NicknameProposal);

        await Profile(db, w.Pupil).SetNickname(new SetNicknameRequest(" "), Service(db));
        Assert.Null(w.Child.Person.Nickname);
    }

    [Fact]
    public async Task AParentSetsTheirChildsNicknameAndTheirOwnRightAway()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.NicknameProposal = "Kubík";
        await db.SaveChangesAsync();

        await Children(db, w.Parent).SetNickname(w.Child.Id, new SetNicknameRequest("Kuba"), Service(db));
        Assert.Equal("Kuba", w.Child.Person.Nickname);
        Assert.Null(w.Child.Person.NicknameProposal);

        var profile = Assert.IsType<MyProfileDto>(Assert.IsType<OkObjectResult>(
            (await Profile(db, w.Parent).SetNickname(new SetNicknameRequest("Máma"), Service(db))).Result).Value);
        Assert.Equal("Máma", profile.Nickname);
        Assert.True(profile.CanSetNickname);
    }

    [Fact]
    public async Task TeachersSetNoNicknameAndTheSchoolDecidesOnlyForPupilsWithoutParents()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        Assert.IsType<BadRequestObjectResult>((await Profile(db, w.ClassTeacher).SetNickname(new SetNicknameRequest("Učo"), Service(db))).Result);

        await Profile(db, w.OrphanPupil).SetNickname(new SetNicknameRequest("Otík"), Service(db));
        var request = Assert.Single(await db.Notifications.Where(n => n.Type == NotificationType.Nickname).ToListAsync());
        Assert.Equal(w.ClassTeacher.Id, request.UserId);
        Assert.Equal("admin?group=class&section=students", NotificationLinks.For(request));

        var listed = Assert.IsAssignableFrom<IEnumerable<StudentDto>>(
            Assert.IsType<OkObjectResult>((await Students(db, w.ClassTeacher).GetStudents()).Result).Value).ToList();
        Assert.Equal("Otík", listed.Single(s => s.Id == w.Orphan.Id).NicknameProposal);

        w.Child.Person!.NicknameProposal = "Kubík";
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await Students(db, w.ClassTeacher).ApproveNickname(w.Child.Id, Service(db)));
        Assert.IsType<ForbidResult>(await Students(db, w.OtherTeacher).ApproveNickname(w.Orphan.Id, Service(db)));

        Assert.IsType<NoContentResult>(await Students(db, w.ClassTeacher).ApproveNickname(w.Orphan.Id, Service(db)));
        Assert.Equal("Otík", w.Orphan.Person!.Nickname);
    }

    [Fact]
    public async Task TeachersSeeANicknameAsFarAsTheParentsLetItReach()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.Nickname = "Kuba";
        w.OtherClass.Person!.Nickname = "Evička";
        await db.SaveChangesAsync();

        var labels = await LabelsAsync(db, w.ClassTeacher, w.Child, w.OtherClass, w.Orphan);
        Assert.Equal(new NameLabel("Kuba", "Jakub Malý"), Assert.Single(labels).Value);
        Assert.Empty(await LabelsAsync(db, w.Admin, w.Child));

        db.VisibilityChoices.Add(new VisibilityChoice
        {
            SchoolId = SchoolId, PersonId = w.OtherClass.PersonId, Owner = VisibilityOwner.Student,
            Field = VisibilityField.Nickname, Audience = VisibilityAudience.Teachers, Scope = VisibilityScope.School,
        });
        db.VisibilityChoices.Add(new VisibilityChoice
        {
            SchoolId = SchoolId, PersonId = w.Child.PersonId, Owner = VisibilityOwner.Student,
            Field = VisibilityField.Nickname, Audience = VisibilityAudience.Teachers, Scope = VisibilityScope.Nobody,
        });
        await db.SaveChangesAsync();
        labels = await LabelsAsync(db, w.ClassTeacher, w.Child, w.OtherClass);
        Assert.Equal("Evička", Assert.Single(labels).Value.Primary);
    }

    [Fact]
    public async Task WhoTurnedNicknamesOffSeesNamesAndTheSchoolSetsTheTeachersDefault()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.Nickname = "Kuba";
        await db.SaveChangesAsync();

        var off = Assert.IsType<MyProfileDto>(Assert.IsType<OkObjectResult>(
            (await Profile(db, w.ClassTeacher).SetShowNicknames(new SetShowNicknamesRequest(false))).Result).Value);
        Assert.False(off.ShowNicknames);
        Assert.False(w.ClassTeacher.ShowStudentNicknames);
        Assert.Empty(await LabelsAsync(db, w.ClassTeacher, w.Child));

        (await db.Schools.FirstAsync()).StaffSeeStudentNicknames = false;
        await db.SaveChangesAsync();
        await Profile(db, w.ClassTeacher).SetShowNicknames(new SetShowNicknamesRequest(false));
        Assert.Null(w.ClassTeacher.ShowStudentNicknames);
        Assert.Empty(await LabelsAsync(db, w.ClassTeacher, w.Child));

        await Profile(db, w.Parent).SetShowNicknames(new SetShowNicknamesRequest(false));
        Assert.Equal(new NameLabel("Jakub", null), (await LabelsAsync(db, w.Parent, w.Child))[w.Child.Id]);
        Assert.IsType<BadRequestObjectResult>((await Profile(db, w.Admin).SetShowNicknames(new SetShowNicknamesRequest(false))).Result);
    }

    [Fact]
    public async Task AParentSeesTheirChildByNicknameOrFirstNameAndTheChildSeesThemByTheirs()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        Assert.Equal(new NameLabel("Jakub", null), (await LabelsAsync(db, w.Parent, w.Child, w.OtherClass)).Single().Value);
        w.Child.Person!.Nickname = "Kuba";
        await db.SaveChangesAsync();
        Assert.Equal(new NameLabel("Kuba", null), (await LabelsAsync(db, w.Parent, w.Child))[w.Child.Id]);

        var ofParent = await db.UserLabelsAsync(TestRoles.PrincipalOf(w.Pupil), [w.Parent.Id]);
        Assert.Equal(new NameLabel("Mamka", null), ofParent[w.Parent.Id]);
        Assert.Empty(await db.UserLabelsAsync(TestRoles.PrincipalOf(w.OrphanPupil), [w.Parent.Id]));
        Assert.Empty(await db.UserLabelsAsync(TestRoles.PrincipalOf(w.ClassTeacher), [w.Parent.Id]));
    }

    [Fact]
    public async Task NotificationsNameThePupilAsEachRecipientSeesThem()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        w.Child.Person!.Nickname = "Kuba";
        await db.SaveChangesAsync();

        var names = await new Notifier(db, TimeProvider.System).StudentNamesAsync(
            w.Child.Id, [w.Parent.Id, w.Pupil.Id, w.ClassTeacher.Id, w.OtherTeacher.Id, w.Admin.Id]);

        Assert.Equal("Kuba", names[w.Parent.Id]);
        Assert.Equal("Jakub Malý", names[w.Pupil.Id]);
        Assert.Equal("Kuba (Jakub Malý)", names[w.ClassTeacher.Id]);
        Assert.Equal("Jakub Malý", names[w.OtherTeacher.Id]);
        Assert.Equal("Jakub Malý", names[w.Admin.Id]);
    }

    [Fact]
    public async Task AParentsNicknameReachesOnlyTheirChildren()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var rules = Visibility.Keys
            .Select(k => new VisibilityRuleDto(k.Owner, k.Field, k.Audience, VisibilityScope.Nobody, VisibilityScope.Nobody, VisibilityScope.School))
            .ToList();

        var result = await new MySchoolController(db, new FakeCurrentSchoolContext(SchoolId))
            .SetVisibility(new VisibilityRulesDto(rules));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
