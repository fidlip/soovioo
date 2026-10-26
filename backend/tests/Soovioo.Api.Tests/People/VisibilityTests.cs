using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.People;

public class VisibilityTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static ControllerContext Context(int userId, string role) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)])),
        },
    };

    private static ClassDirectoryController Directory(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)))
        {
            ControllerContext = Context(user.Id, user.ActiveRole.ToString()),
        };

    private static MyProfileController Profile(AppDbContext db, User user) =>
        new(db, AuthTestServices.Sessions(db), AuthTestServices.RefreshTokens(db)) { ControllerContext = Context(user.Id, user.ActiveRole.ToString()) };

    private static MyChildrenController Children(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = Context(user.Id, user.ActiveRole.ToString()) };

    private static MySchoolController MySchool(AppDbContext db) => new(db, new FakeCurrentSchoolContext(SchoolId));

    private sealed record Fixture(
        School School, User Teacher, User Parent, User OtherParent, User Pupil, StudentProfile Child, StudentProfile Classmate);

    /// <summary>Class 1.A: teacher (class teacher, with phone/e-mail), the
    /// parent's child (with an own pupil account) and a classmate whose
    /// parent is another parent. <paramref name="demoRules"/>: the school
    /// shares what a demo does, otherwise nothing.</summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db, bool demoRules)
    {
        var school = new School { Id = SchoolId, Name = "Škola", Slug = "skola" };
        db.Schools.Add(school);
        if (demoRules)
        {
            db.VisibilityRules.AddRange(Visibility.DemoRules(SchoolId));
        }
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = UserRole.Teacher, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        var otherParent = new User { Username = "rodic2", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        var pupil = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.AddRange(teacher, parent, otherParent, pupil);
        await db.SaveChangesAsync();
        db.Persons.AddRange(
            new Person { UserId = teacher.Id, SchoolId = SchoolId, FirstName = "Tereza", LastName = "Učitelová", Phone = "111", Email = "u@s.cz" },
            new Person { UserId = parent.Id, SchoolId = SchoolId, FirstName = "Petr", LastName = "Rodič", Phone = "222" },
            new Person { UserId = otherParent.Id, SchoolId = SchoolId, FirstName = "Olga", LastName = "Jiná", Phone = "333", Email = "o@j.cz" });
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var child = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Rodič", UserId = pupil.Id, SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var classmate = new StudentProfile { Person = new Person { FirstName = "Eva", LastName = "Jiná", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(child, classmate);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.LinkChildAsync(otherParent.Id, classmate.Id, SchoolId);
        await db.SaveChangesAsync();
        return new Fixture(school, teacher, parent, otherParent, pupil, child, classmate);
    }

    private static async Task<int> PersonIdAsync(AppDbContext db, User user) =>
        (await db.Persons.FirstAsync(p => p.UserId == user.Id)).Id;

    private static void Choose(AppDbContext db, int personId, VisibilityOwner owner,
        VisibilityField field, VisibilityAudience audience, VisibilityScope scope) =>
        db.VisibilityChoices.Add(new VisibilityChoice
        {
            SchoolId = SchoolId, PersonId = personId,
            Owner = owner, Field = field, Audience = audience, Scope = scope,
        });

    private static async Task SetRuleAsync(AppDbContext db, VisibilityKey key, VisibilityScope min, VisibilityScope def, VisibilityScope max)
    {
        var rule = await db.VisibilityRules.FirstAsync(r => r.Owner == key.Owner && r.Field == key.Field && r.Audience == key.Audience);
        (rule.Min, rule.Default, rule.Max) = (min, def, max);
        await db.SaveChangesAsync();
    }

    private static async Task<ClassDirectoryDto> GetDirectoryAsync(AppDbContext db, User user, int? studentId = null) =>
        Assert.IsType<ClassDirectoryDto>(Assert.IsType<OkObjectResult>((await Directory(db, user).Get(studentId)).Result).Value);

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task NothingSharedGivesTeacherNamesOnly()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: false);

        var dto = await GetDirectoryAsync(db, fx.Parent);

        var teacher = Assert.Single(dto.Teachers);
        Assert.Equal("Tereza Učitelová", teacher.Name);
        Assert.Null(teacher.Phone);
        Assert.Null(teacher.Email);
        Assert.Null(dto.Classmates);
        Assert.Null(dto.Parents);
    }

    [Fact]
    public async Task SharedDataReachesParents()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);

        var dto = await GetDirectoryAsync(db, fx.Parent);

        Assert.Equal("111", Assert.Single(dto.Teachers).Phone);
        Assert.Equal(["Eva Jiná", "Jan Rodič"], dto.Classmates!.Select(c => c.Name));
        var other = Assert.Single(dto.Parents!);
        Assert.Equal("Olga Jiná", other.Name);
        Assert.Equal("o@j.cz", other.Email);
        Assert.Equal(["Eva Jiná"], other.Children);
    }

    [Fact]
    public async Task PupilGetsWhatIsSharedWithPupilsButNoParents()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        Choose(db, await PersonIdAsync(db, fx.Teacher), VisibilityOwner.Staff, VisibilityField.Phone,
            VisibilityAudience.Students, VisibilityScope.Nobody);
        await db.SaveChangesAsync();

        var dto = await GetDirectoryAsync(db, fx.Pupil);

        var teacher = Assert.Single(dto.Teachers);
        Assert.Null(teacher.Phone);
        Assert.Equal("u@s.cz", teacher.Email);
        Assert.Equal(2, dto.Classmates!.Count);
        Assert.Null(dto.Parents);
    }

    [Fact]
    public async Task ChoicesNarrowWhatOthersSee()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        Choose(db, await PersonIdAsync(db, fx.Teacher), VisibilityOwner.Staff, VisibilityField.Phone,
            VisibilityAudience.Parents, VisibilityScope.Nobody);
        Choose(db, fx.Classmate.PersonId, VisibilityOwner.Student, VisibilityField.Name,
            VisibilityAudience.Parents, VisibilityScope.Nobody);
        Choose(db, await PersonIdAsync(db, fx.OtherParent), VisibilityOwner.Parent, VisibilityField.Email,
            VisibilityAudience.Parents, VisibilityScope.Nobody);
        await db.SaveChangesAsync();

        var dto = await GetDirectoryAsync(db, fx.Parent);

        Assert.Null(Assert.Single(dto.Teachers).Phone);
        Assert.Equal(["Jan Rodič"], dto.Classmates!.Select(c => c.Name));
        var other = Assert.Single(dto.Parents!);
        Assert.Null(other.Email);
        Assert.Equal("333", other.Phone);
        Assert.Empty(other.Children); // Eva isn't on the list the caller got
        // The pupil audience is a separate setting.
        Assert.Equal(2, (await GetDirectoryAsync(db, fx.Pupil)).Classmates!.Count);
    }

    [Fact]
    public async Task TheSchoolsRangeOverridesChoices()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        var teacherPerson = await PersonIdAsync(db, fx.Teacher);
        Choose(db, teacherPerson, VisibilityOwner.Staff, VisibilityField.Phone, VisibilityAudience.Parents, VisibilityScope.Nobody);
        Choose(db, teacherPerson, VisibilityOwner.Staff, VisibilityField.Email, VisibilityAudience.Parents, VisibilityScope.School);
        await db.SaveChangesAsync();
        await SetRuleAsync(db, new(VisibilityOwner.Staff, VisibilityField.Phone, VisibilityAudience.Parents),
            VisibilityScope.MyClasses, VisibilityScope.MyClasses, VisibilityScope.School);
        await SetRuleAsync(db, new(VisibilityOwner.Staff, VisibilityField.Email, VisibilityAudience.Parents),
            VisibilityScope.Nobody, VisibilityScope.Nobody, VisibilityScope.Nobody);

        var teacher = Assert.Single((await GetDirectoryAsync(db, fx.Parent)).Teachers);

        Assert.Equal("111", teacher.Phone);
        Assert.Null(teacher.Email);
    }

    [Fact]
    public async Task WhoHidesTheirNameHidesEverything()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        var otherParent = await PersonIdAsync(db, fx.OtherParent);
        Choose(db, otherParent, VisibilityOwner.Parent, VisibilityField.Name, VisibilityAudience.Parents, VisibilityScope.Nobody);
        Choose(db, otherParent, VisibilityOwner.Parent, VisibilityField.Phone, VisibilityAudience.Parents, VisibilityScope.School);
        await db.SaveChangesAsync();

        Assert.Empty((await GetDirectoryAsync(db, fx.Parent)).Parents!);
        var phone = Ok(await Profile(db, fx.OtherParent).GetVisibility())
            .Single(s => s.Field == VisibilityField.Phone);
        Assert.Equal(VisibilityScope.Nobody, phone.Scope);
        Assert.True(phone.Custom);
    }

    [Fact]
    public async Task OwnChildStaysOnTheListEvenWhenHidden()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        Choose(db, fx.Child.PersonId, VisibilityOwner.Student, VisibilityField.Name, VisibilityAudience.Parents, VisibilityScope.Nobody);
        await db.SaveChangesAsync();

        Assert.Equal(2, (await GetDirectoryAsync(db, fx.Parent)).Classmates!.Count);
        Assert.Single((await GetDirectoryAsync(db, fx.OtherParent)).Classmates!);
    }

    [Fact]
    public async Task ForeignStudentIsNotFound()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);

        var result = await Directory(db, fx.Parent).Get(fx.Classmate.Id);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SchoolRulesListEverySettingAndStoreOnlyShared()
    {
        using var db = NewDb();
        await SeedAsync(db, demoRules: false);

        var dto = Ok(await MySchool(db).SetVisibility(new VisibilityRulesDto(
        [
            new(VisibilityOwner.Staff, VisibilityField.Phone, VisibilityAudience.Parents,
                VisibilityScope.Nobody, VisibilityScope.MyClasses, VisibilityScope.School),
            new(VisibilityOwner.Parent, VisibilityField.Name, VisibilityAudience.Parents,
                VisibilityScope.Nobody, VisibilityScope.Nobody, VisibilityScope.Nobody),
        ])));

        Assert.Equal(Visibility.Keys.Count, dto.Rules.Count);
        Assert.Equal(VisibilityScope.MyClasses, dto.Rules.Single(r => r.Owner == VisibilityOwner.Staff
            && r.Field == VisibilityField.Phone && r.Audience == VisibilityAudience.Parents).Default);
        Assert.Single(db.VisibilityRules);
    }

    [Theory]
    [InlineData(VisibilityOwner.Staff, VisibilityField.Name, VisibilityScope.Nobody, VisibilityScope.Nobody)]
    [InlineData(VisibilityOwner.Student, VisibilityField.Phone, VisibilityScope.Nobody, VisibilityScope.Nobody)]
    [InlineData(VisibilityOwner.Parent, VisibilityField.Name, VisibilityScope.School, VisibilityScope.MyClasses)]
    [InlineData(VisibilityOwner.Parent, VisibilityField.Name, (VisibilityScope)7, VisibilityScope.School)]
    public async Task SchoolRulesRejectUnknownSettingsAndBadRanges(
        VisibilityOwner owner, VisibilityField field, VisibilityScope def, VisibilityScope max)
    {
        using var db = NewDb();
        await SeedAsync(db, demoRules: false);

        var result = await MySchool(db).SetVisibility(new VisibilityRulesDto(
            [new(owner, field, VisibilityAudience.Parents, VisibilityScope.Nobody, def, max)]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ProfileOffersTheSettingsOfTheCallersRoles()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        fx.Teacher.Roles.Add(UserRole.Parent);
        await db.SaveChangesAsync();

        var teacher = Ok(await Profile(db, fx.Teacher).GetVisibility());
        Assert.Equal([VisibilityOwner.Staff, VisibilityOwner.Parent], teacher.Select(s => s.Owner).Distinct());
        Assert.All(teacher, s => Assert.True(s.Editable));

        var parent = Ok(await Profile(db, fx.Parent).GetVisibility());
        Assert.Equal([VisibilityOwner.Parent], parent.Select(s => s.Owner).Distinct());

        var pupil = Ok(await Profile(db, fx.Pupil).GetVisibility());
        Assert.Equal([VisibilityOwner.Student], pupil.Select(s => s.Owner).Distinct());
        Assert.All(pupil, s => Assert.False(s.Editable));
    }

    [Fact]
    public async Task OwnChoiceIsStoredOnlyWhileItDiffersFromTheDefault()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);

        var hidden = Ok(await Profile(db, fx.Parent).SetVisibility(new SetVisibilityRequest(
            VisibilityOwner.Parent, VisibilityField.Phone, VisibilityAudience.Parents, VisibilityScope.Nobody)));
        Assert.Equal(VisibilityScope.Nobody, hidden.Single(s => s.Field == VisibilityField.Phone).Scope);
        Assert.Single(db.VisibilityChoices);

        Ok(await Profile(db, fx.Parent).SetVisibility(new SetVisibilityRequest(
            VisibilityOwner.Parent, VisibilityField.Phone, VisibilityAudience.Parents, VisibilityScope.MyClasses)));
        Assert.Empty(db.VisibilityChoices);
    }

    [Theory]
    [InlineData(VisibilityOwner.Staff, VisibilityField.Phone, VisibilityScope.Nobody)] // not a teacher
    [InlineData(VisibilityOwner.Student, VisibilityField.Name, VisibilityScope.Nobody)] // Mé děti, not here
    [InlineData(VisibilityOwner.Parent, VisibilityField.Photo, (VisibilityScope)7)]
    public async Task OwnChoiceMustBeTheCallersAndValid(VisibilityOwner owner, VisibilityField field, VisibilityScope scope)
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);

        var result = await Profile(db, fx.Parent).SetVisibility(new SetVisibilityRequest(owner, field, VisibilityAudience.Parents, scope));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OwnChoiceStaysWithinTheSchoolsRange()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        await SetRuleAsync(db, new(VisibilityOwner.Parent, VisibilityField.Name, VisibilityAudience.Parents),
            VisibilityScope.MyClasses, VisibilityScope.MyClasses, VisibilityScope.MyClasses);

        foreach (var scope in new[] { VisibilityScope.Nobody, VisibilityScope.School })
        {
            var result = await Profile(db, fx.Parent).SetVisibility(new SetVisibilityRequest(
                VisibilityOwner.Parent, VisibilityField.Name, VisibilityAudience.Parents, scope));
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task ParentDecidesForOwnChildrenOnly()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        var request = new SetVisibilityRequest(
            VisibilityOwner.Student, VisibilityField.Name, VisibilityAudience.Students, VisibilityScope.Nobody);

        var children = Ok(await Children(db, fx.Parent).SetVisibility(fx.Child.Id, request));
        var child = Assert.Single(children);
        Assert.Equal("1.A", child.ClassName);
        Assert.Equal(VisibilityScope.Nobody, child.Visibility.Single(s =>
            s.Field == VisibilityField.Name && s.Audience == VisibilityAudience.Students).Scope);
        Assert.Equal(fx.Child.PersonId, Assert.Single(db.VisibilityChoices).PersonId);

        var foreign = await Children(db, fx.Parent).SetVisibility(fx.Classmate.Id, request);
        Assert.IsType<NotFoundResult>(foreign.Result);

        var notAStudentSetting = await Children(db, fx.Parent).SetVisibility(fx.Child.Id, request with { Owner = VisibilityOwner.Parent });
        Assert.IsType<BadRequestObjectResult>(notAStudentSetting.Result);
    }

    [Fact]
    public async Task PhotoSettingsAreOfferedOnlyWhileTheSchoolHasThosePhotosOn()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, demoRules: true);
        fx.Teacher.Roles.Add(UserRole.Parent);
        await db.SaveChangesAsync();
        var photo = new SetVisibilityRequest(
            VisibilityOwner.Student, VisibilityField.Photo, VisibilityAudience.Parents, VisibilityScope.MyClasses);

        var child = Assert.Single(Ok(await Children(db, fx.Parent).Get()));
        Assert.DoesNotContain(child.Visibility, s => s.Field == VisibilityField.Photo);
        Assert.IsType<BadRequestObjectResult>((await Children(db, fx.Parent).SetVisibility(fx.Child.Id, photo)).Result);
        var own = Ok(await Profile(db, fx.Teacher).GetVisibility());
        Assert.Contains(own, s => s.Owner == VisibilityOwner.Staff && s.Field == VisibilityField.Photo);
        Assert.DoesNotContain(own, s => s.Owner == VisibilityOwner.Parent && s.Field == VisibilityField.Photo);

        fx.School.StudentPhotos = true;
        fx.School.ParentPhotos = true;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await Children(db, fx.Parent).SetVisibility(fx.Child.Id, photo)).Result);
        Assert.Contains(Ok(await Profile(db, fx.Teacher).GetVisibility()),
            s => s.Owner == VisibilityOwner.Parent && s.Field == VisibilityField.Photo);
    }
}
