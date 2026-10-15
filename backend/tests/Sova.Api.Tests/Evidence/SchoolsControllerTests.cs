using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Tests.Evidence;

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

        Assert.IsType<BadRequestObjectResult>(result.Result);
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
}
