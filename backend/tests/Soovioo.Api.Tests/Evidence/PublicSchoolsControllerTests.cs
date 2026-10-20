using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Evidence;

public class PublicSchoolsControllerTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        // Anonymous request: no school context yet.
        return new AppDbContext(options, new FakeCurrentSchoolContext(null));
    }

    [Fact]
    public async Task GetSchoolReturnsNameAndTypeOfAnExistingSchool()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Name = "Gymnázium", Slug = "gym", Type = SchoolType.Secondary });
        await db.SaveChangesAsync();

        var result = await new PublicSchoolsController(db).GetSchool("gym");

        var dto = Assert.IsType<PublicSchoolDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Gymnázium", dto.Name);
        Assert.Equal(nameof(SchoolType.Secondary), dto.Type);
    }

    [Fact]
    public async Task GetSchoolReturnsNotFoundForAnUnknownSlug()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Name = "Škola", Slug = "skola" });
        await db.SaveChangesAsync();

        var result = await new PublicSchoolsController(db).GetSchool("neexistuje");

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
