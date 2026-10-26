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

public class SchoolDirectoryTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static SchoolDirectoryController Directory(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.ActiveRole.ToString()),
                    ])),
                },
            },
        };

    private sealed record Fixture(
        User TeacherA, User TeacherB, User Parent, User OtherParent, User Pupil,
        StudentProfile Child, StudentProfile Classmate, StudentProfile Stranger, SchoolClass ClassA, SchoolClass ClassB);

    /// <summary>The demo's sharing (names and contacts in "Moje třídy" by
    /// default, at most the whole school). 1.A: teacher A, the parent's child
    /// (own pupil account) and a classmate; 2.B: teacher B and a pupil whose
    /// parent is the other parent.</summary>
    private static async Task<Fixture> SeedAsync(AppDbContext db)
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        db.VisibilityRules.AddRange(Visibility.DemoRules(SchoolId));
        User NewUser(string name, UserRole role) => new() { Username = name, PasswordHash = "x", ActiveRole = role, SchoolId = SchoolId };
        var teacherA = NewUser("ucitelA", UserRole.Teacher);
        var teacherB = NewUser("ucitelB", UserRole.Teacher);
        var parent = NewUser("rodic", UserRole.Parent);
        var otherParent = NewUser("rodic2", UserRole.Parent);
        var pupil = NewUser("zak", UserRole.Student);
        db.Users.AddRange(teacherA, teacherB, parent, otherParent, pupil);
        await db.SaveChangesAsync();
        db.Persons.AddRange(
            new Person { UserId = teacherA.Id, SchoolId = SchoolId, FirstName = "Tereza", LastName = "Učitelová", Phone = "111" },
            new Person { UserId = teacherB.Id, SchoolId = SchoolId, FirstName = "Bohumil", LastName = "Šťastný", Phone = "222" },
            new Person { UserId = parent.Id, SchoolId = SchoolId, FirstName = "Petr", LastName = "Rodič" },
            new Person { UserId = otherParent.Id, SchoolId = SchoolId, FirstName = "Olga", LastName = "Jiná", Phone = "333" });
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacherA.Id };
        var classB = new SchoolClass { Name = "2.B", SchoolId = SchoolId, ClassTeacherId = teacherB.Id };
        db.SchoolClasses.AddRange(classA, classB);
        await db.SaveChangesAsync();
        var child = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Rodič", UserId = pupil.Id, SchoolId = SchoolId }, SchoolClassId = classA.Id, SchoolId = SchoolId };
        var classmate = new StudentProfile { Person = new Person { FirstName = "Eva", LastName = "Malá", SchoolId = SchoolId }, SchoolClassId = classA.Id, SchoolId = SchoolId };
        var stranger = new StudentProfile { Person = new Person { FirstName = "Adam", LastName = "Jiný", SchoolId = SchoolId }, SchoolClassId = classB.Id, SchoolId = SchoolId };
        db.Students.AddRange(child, classmate, stranger);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        await db.LinkChildAsync(otherParent.Id, stranger.Id, SchoolId);
        await db.SaveChangesAsync();
        return new Fixture(teacherA, teacherB, parent, otherParent, pupil, child, classmate, stranger, classA, classB);
    }

    private static async Task ShareWithSchoolAsync(AppDbContext db, int personId, VisibilityOwner owner, VisibilityField field)
    {
        db.VisibilityChoices.Add(new VisibilityChoice
        {
            SchoolId = SchoolId, PersonId = personId, Owner = owner, Field = field,
            Audience = VisibilityAudience.Parents, Scope = VisibilityScope.School,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> PersonIdAsync(AppDbContext db, User user) =>
        (await db.Persons.FirstAsync(p => p.UserId == user.Id)).Id;

    private static async Task<SchoolDirectoryDto> GetAsync(
        AppDbContext db, User user, string? search = null, SchoolPersonKind? kind = null, int? classId = null,
        int page = 1, int pageSize = 50) =>
        Assert.IsType<SchoolDirectoryDto>(Assert.IsType<OkObjectResult>(
            (await Directory(db, user).Get(search, kind, classId, page, pageSize)).Result).Value);

    [Fact]
    public async Task StaffAlwaysByNameContactsOnlyAsFarAsShared()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var staff = (await GetAsync(db, fx.Parent, kind: SchoolPersonKind.Staff)).People.Items;

        Assert.Equal(["Bohumil Šťastný", "Tereza Učitelová"], staff.Select(s => s.Name));
        Assert.Equal("111", staff.Single(s => s.Name == "Tereza Učitelová").Phone);
        Assert.Equal("třídní 1.A", staff.Single(s => s.Name == "Tereza Učitelová").Notes);
        // Teacher B doesn't teach the child's class and shares only within their classes.
        Assert.Null(staff.Single(s => s.Name == "Bohumil Šťastný").Phone);

        await ShareWithSchoolAsync(db, await PersonIdAsync(db, fx.TeacherB), VisibilityOwner.Staff, VisibilityField.Phone);
        var shared = (await GetAsync(db, fx.Parent, kind: SchoolPersonKind.Staff)).People.Items;
        Assert.Equal("222", shared.Single(s => s.Name == "Bohumil Šťastný").Phone);
    }

    [Fact]
    public async Task OtherClassesPupilsAndParentsOnlyWhenSharedWithTheSchool()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var before = (await GetAsync(db, fx.Parent)).People.Items;
        Assert.Equal(["Eva Malá", "Jan Rodič"], before.Where(p => p.Kind == SchoolPersonKind.Student).Select(p => p.Name).Order());
        Assert.DoesNotContain(before, p => p.Kind == SchoolPersonKind.Parent);

        // The other parent shares their name with the school, but not the child's: no child named.
        await ShareWithSchoolAsync(db, await PersonIdAsync(db, fx.OtherParent), VisibilityOwner.Parent, VisibilityField.Name);
        var parentOnly = (await GetAsync(db, fx.Parent, kind: SchoolPersonKind.Parent)).People.Items;
        var other = Assert.Single(parentOnly);
        Assert.Equal("Olga Jiná", other.Name);
        Assert.Empty(other.Children);
        Assert.Null(other.Phone);
        Assert.Null(other.UserId);
        // …and the class filter doesn't give the child's class away either.
        Assert.Empty((await GetAsync(db, fx.Parent, kind: SchoolPersonKind.Parent, classId: fx.ClassB.Id)).People.Items);

        await ShareWithSchoolAsync(db, fx.Stranger.PersonId, VisibilityOwner.Student, VisibilityField.Name);
        var after = (await GetAsync(db, fx.Parent, classId: fx.ClassB.Id)).People.Items;
        Assert.Equal(["Adam Jiný", "Bohumil Šťastný", "Olga Jiná"], after.Select(p => p.Name).Order());
        Assert.Equal(["Adam Jiný (2.B)"], after.Single(p => p.Kind == SchoolPersonKind.Parent).Children);
        Assert.Equal("2.B", after.Single(p => p.Kind == SchoolPersonKind.Student).Notes);
    }

    [Fact]
    public async Task PupilSeesNoParents()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        await ShareWithSchoolAsync(db, await PersonIdAsync(db, fx.OtherParent), VisibilityOwner.Parent, VisibilityField.Name);

        var dto = await GetAsync(db, fx.Pupil);

        Assert.False(dto.ShowsParents);
        Assert.DoesNotContain(dto.People.Items, p => p.Kind == SchoolPersonKind.Parent);
        Assert.Contains(dto.People.Items, p => p.Name == "Jan Rodič");
    }

    [Fact]
    public async Task SearchIgnoresCaseAndDiacriticsAndPagesTheResult()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var found = (await GetAsync(db, fx.Parent, search: " stastny ")).People;
        Assert.Equal("Bohumil Šťastný", Assert.Single(found.Items).Name);

        var firstPage = (await GetAsync(db, fx.Parent, pageSize: 2)).People;
        var secondPage = (await GetAsync(db, fx.Parent, page: 2, pageSize: 2)).People;
        Assert.Equal(4, firstPage.TotalCount);
        // Ordered by surname, Czech collation.
        Assert.Equal(["Eva Malá", "Jan Rodič"], firstPage.Items.Select(p => p.Name));
        Assert.Equal(["Bohumil Šťastný", "Tereza Učitelová"], secondPage.Items.Select(p => p.Name));
        Assert.Equal(["1.A", "2.B"], (await GetAsync(db, fx.Parent)).Classes.Select(c => c.Name));
    }
}
