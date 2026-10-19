using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Evidence;

public class SchoolsControllerTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        // SuperAdmin context: SchoolsController is SuperAdmin-only, and its
        // queries (e.g. the duplicate-slug check) must see every school.
        return new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: true));
    }

    [Fact]
    public async Task GetSchoolsIncludesStudentAndUserCounts()
    {
        using var db = NewDb();
        var school = new School { Name = "Škola", Slug = "skola" };
        var otherSchool = new School { Name = "Jiná škola", Slug = "jina" };
        db.Schools.AddRange(school, otherSchool);
        await db.SaveChangesAsync();
        db.Users.AddRange(
            new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = school.Id },
            new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = school.Id });
        db.Students.AddRange(
            new Student { FirstName = "A", LastName = "A", SchoolId = school.Id },
            new Student { FirstName = "B", LastName = "B", SchoolId = otherSchool.Id });
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).GetSchools();

        var page = Assert.IsType<Soovioo.Api.Common.PagedResult<SchoolDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var dto = Assert.Single(page.Items, s => s.Id == school.Id);
        Assert.Equal(2, dto.UserCount);
        Assert.Equal(1, dto.StudentCount);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetSchoolsPaginatesResults()
    {
        using var db = NewDb();
        db.Schools.AddRange(
            new School { Name = "A", Slug = "a" },
            new School { Name = "B", Slug = "b" },
            new School { Name = "C", Slug = "c" });
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).GetSchools(page: 2, pageSize: 1);

        var page = Assert.IsType<Soovioo.Api.Common.PagedResult<SchoolDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(3, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("B", page.Items[0].Name);
    }

    [Fact]
    public async Task UpdateSchoolRenamesAndChangesSlug()
    {
        using var db = NewDb();
        var school = new School { Name = "Stará škola", Slug = "stara" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).UpdateSchool(
            school.Id, new UpdateSchoolRequest("Nová škola", "nova"));

        var dto = Assert.IsType<SchoolDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Nová škola", dto.Name);
        Assert.Equal("nova", dto.Slug);
    }

    [Fact]
    public async Task UpdateSchoolRejectsDuplicateSlug()
    {
        using var db = NewDb();
        var schoolA = new School { Name = "A", Slug = "a" };
        var schoolB = new School { Name = "B", Slug = "b" };
        db.Schools.AddRange(schoolA, schoolB);
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).UpdateSchool(schoolB.Id, new UpdateSchoolRequest("B", "a"));

        // Field-level error the web form can map onto its "slug" input.
        var problem = Assert.IsType<ValidationProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.True(problem.Errors.ContainsKey("slug"));
    }

    [Fact]
    public async Task DeleteSchoolRejectsWhenItHasUsers()
    {
        using var db = NewDb();
        var school = new School { Name = "Obsazená škola", Slug = "obsazena" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        db.Users.Add(new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = school.Id });
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).DeleteSchool(school.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Schools.FindAsync(school.Id));
    }

    [Fact]
    public async Task DeleteSchoolSucceedsWhenEmpty()
    {
        using var db = NewDb();
        var school = new School { Name = "Prázdná škola", Slug = "prazdna" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).DeleteSchool(school.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Schools.FindAsync(school.Id));
    }

    [Fact]
    public async Task UpdateSchoolSetsAndClearsMatrikaIds()
    {
        using var db = NewDb();
        var school = new School { Name = "A", Slug = "a", Izo = "1" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var result = await new SchoolsController(db).UpdateSchool(school.Id, new UpdateSchoolRequest("B", "b", RedIzo: "600000001"));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("B", school.Name);
        Assert.Null(school.Izo);
        Assert.Equal("600000001", school.RedIzo);
    }
}
