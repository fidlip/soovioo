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
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = Context(user.Id, user.Role.ToString()) };

    private static MyProfileController Profile(AppDbContext db, User user) =>
        new(db, AuthTestServices.Sessions(db), AuthTestServices.RefreshTokens(db)) { ControllerContext = Context(user.Id, user.Role.ToString()) };

    private static MySchoolController MySchool(AppDbContext db) => new(db, new FakeCurrentSchoolContext(SchoolId));

    private sealed record Fixture(
        School School, User Teacher, User Parent, User OtherParent, User Pupil, Student Child, Student Classmate);

    /// <summary>Class 1.A: teacher (class teacher, with phone/e-mail), the
    /// parent's child (with an own pupil account) and a classmate whose
    /// parent is another parent.</summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db, VisibilityOption shared, VisibilityOption optOut = VisibilityOption.None)
    {
        var school = new School { Id = SchoolId, Name = "Škola", Slug = "skola", SharedData = shared, OptOutAllowed = optOut };
        db.Schools.Add(school);
        var teacher = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        var otherParent = new User { Username = "rodic2", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        var pupil = new User { Username = "zak", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.AddRange(teacher, parent, otherParent, pupil);
        await db.SaveChangesAsync();
        db.Persons.AddRange(
            new Person { UserId = teacher.Id, SchoolId = SchoolId, FirstName = "Tereza", LastName = "Učitelová", Phone = "111", Email = "u@s.cz" },
            new Person { UserId = parent.Id, SchoolId = SchoolId, FirstName = "Petr", LastName = "Rodič", Phone = "222" },
            new Person { UserId = otherParent.Id, SchoolId = SchoolId, FirstName = "Olga", LastName = "Jiná", Phone = "333", Email = "o@j.cz" });
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var child = new Student { FirstName = "Jan", LastName = "Rodič", SchoolClassId = schoolClass.Id, SchoolId = SchoolId, UserId = pupil.Id };
        var classmate = new Student { FirstName = "Eva", LastName = "Jiná", SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(child, classmate);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.LinkChildAsync(otherParent.Id, classmate.Id, SchoolId);
        await db.SaveChangesAsync();
        return new Fixture(school, teacher, parent, otherParent, pupil, child, classmate);
    }

    private static async Task<ClassDirectoryDto> GetDirectoryAsync(AppDbContext db, User user, int? studentId = null) =>
        Assert.IsType<ClassDirectoryDto>(Assert.IsType<OkObjectResult>((await Directory(db, user).Get(studentId)).Result).Value);

    [Fact]
    public async Task NothingSharedGivesTeacherNamesOnly()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, VisibilityOption.None);

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
        var fx = await SeedAsync(db, Visibility.All);

        var dto = await GetDirectoryAsync(db, fx.Parent);

        Assert.Equal("111", Assert.Single(dto.Teachers).Phone);
        Assert.Equal(["Eva Jiná", "Jan Rodič"], dto.Classmates!.Select(c => c.Name));
        var other = Assert.Single(dto.Parents!);
        Assert.Equal("Olga Jiná", other.Name);
        Assert.Equal("o@j.cz", other.Email);
        Assert.Equal(["Eva Jiná"], other.Children);
    }

    [Fact]
    public async Task PupilGetsClassListButNoContactsOrParents()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, Visibility.All);

        var dto = await GetDirectoryAsync(db, fx.Pupil);

        Assert.Null(Assert.Single(dto.Teachers).Phone);
        Assert.Equal(2, dto.Classmates!.Count);
        Assert.Null(dto.Parents);
    }

    [Fact]
    public async Task ParentsOfClassmatesNameNoChildrenWithoutClassList()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, VisibilityOption.ParentContacts);

        var dto = await GetDirectoryAsync(db, fx.Parent);

        Assert.Null(dto.Classmates);
        Assert.Empty(Assert.Single(dto.Parents!).Children);
    }

    [Fact]
    public async Task OptOutsCountOnlyWhileTheSchoolAllowsThem()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, Visibility.All);
        (await db.Persons.FirstAsync(p => p.UserId == fx.Teacher.Id)).HiddenData = VisibilityOption.TeacherContacts;
        (await db.Persons.FirstAsync(p => p.UserId == fx.OtherParent.Id)).HiddenData = VisibilityOption.ParentContacts;
        fx.Classmate.HiddenFromClassmates = true;
        await db.SaveChangesAsync();

        var notAllowed = await GetDirectoryAsync(db, fx.Parent);
        Assert.NotNull(Assert.Single(notAllowed.Teachers).Phone);
        Assert.Equal(2, notAllowed.Classmates!.Count);
        Assert.Single(notAllowed.Parents!);

        fx.School.OptOutAllowed = Visibility.OptOutCapable;
        await db.SaveChangesAsync();
        var allowed = await GetDirectoryAsync(db, fx.Parent);
        Assert.Null(Assert.Single(allowed.Teachers).Phone);
        Assert.Equal(["Jan Rodič"], allowed.Classmates!.Select(c => c.Name));
        Assert.Empty(allowed.Parents!);
    }

    [Fact]
    public async Task OwnChildStaysOnTheListEvenWhenHidden()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, Visibility.All, Visibility.OptOutCapable);
        fx.Child.HiddenFromClassmates = true;
        await db.SaveChangesAsync();

        Assert.Equal(2, (await GetDirectoryAsync(db, fx.Parent)).Classmates!.Count);
        Assert.Single((await GetDirectoryAsync(db, fx.OtherParent)).Classmates!);
    }

    [Fact]
    public async Task ForeignStudentIsNotFound()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, Visibility.All);

        var result = await Directory(db, fx.Parent).Get(fx.Classmate.Id);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SetVisibilityDropsOptOutsOfUnsharedOptionsAndPupilPhotos()
    {
        using var db = NewDb();
        await SeedAsync(db, VisibilityOption.None);

        var result = await MySchool(db).SetVisibility(new VisibilitySettingsDto(
            ["TeacherContacts", "StudentPhotos"], ["TeacherContacts", "StudentPhotos", "Classmates"]));

        var dto = Assert.IsType<VisibilitySettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["StudentPhotos", "TeacherContacts"], dto.Shared);
        Assert.Equal(["TeacherContacts"], dto.OptOutAllowed);
    }

    [Theory]
    [InlineData("Nonsense")]
    [InlineData("8")]
    [InlineData("TeacherContacts, Classmates")]
    public async Task SetVisibilityRejectsUnknownOptions(string option)
    {
        using var db = NewDb();
        await SeedAsync(db, VisibilityOption.None);

        var result = await MySchool(db).SetVisibility(new VisibilitySettingsDto([option], []));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task PrivacyOffersOnlyWhatTheSchoolLeavesToIndividuals()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, VisibilityOption.TeacherContacts | VisibilityOption.ParentContacts | VisibilityOption.Classmates,
            VisibilityOption.TeacherContacts | VisibilityOption.Classmates);

        var teacher = Assert.IsType<PrivacyDto>(Assert.IsType<OkObjectResult>((await Profile(db, fx.Teacher).GetPrivacy()).Result).Value);
        Assert.Equal(["TeacherContacts"], teacher.Options.Select(o => o.Option));
        Assert.Empty(teacher.Students);

        var parent = Assert.IsType<PrivacyDto>(Assert.IsType<OkObjectResult>((await Profile(db, fx.Parent).GetPrivacy()).Result).Value);
        Assert.Empty(parent.Options); // ParentContacts shared, but not left to individuals
        Assert.Equal([fx.Child.Id], parent.Students.Select(s => s.StudentId));

        var pupil = Assert.IsType<PrivacyDto>(Assert.IsType<OkObjectResult>((await Profile(db, fx.Pupil).GetPrivacy()).Result).Value);
        Assert.Equal([fx.Child.Id], pupil.Students.Select(s => s.StudentId));
    }

    [Fact]
    public async Task UpdatePrivacyHidesOwnDataOnly()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, Visibility.All, Visibility.OptOutCapable);

        var ok = await Profile(db, fx.Parent).UpdatePrivacy(new PrivacyDto(
            [new PrivacyOptionDto("ParentContacts", true)], [new StudentPrivacyDto(fx.Child.Id, "", true)]));
        Assert.IsType<OkObjectResult>(ok.Result);
        Assert.Equal(VisibilityOption.ParentContacts, (await db.Persons.FirstAsync(p => p.UserId == fx.Parent.Id)).HiddenData);
        Assert.True((await db.Students.FindAsync(fx.Child.Id))!.HiddenFromClassmates);

        var teacherOption = await Profile(db, fx.Parent).UpdatePrivacy(new PrivacyDto(
            [new PrivacyOptionDto("TeacherContacts", true)], []));
        Assert.IsType<BadRequestObjectResult>(teacherOption.Result);

        var foreignChild = await Profile(db, fx.Parent).UpdatePrivacy(new PrivacyDto(
            [], [new StudentPrivacyDto(fx.Classmate.Id, "", true)]));
        Assert.IsType<BadRequestObjectResult>(foreignChild.Result);
        Assert.False((await db.Students.FindAsync(fx.Classmate.Id))!.HiddenFromClassmates);
    }
}
