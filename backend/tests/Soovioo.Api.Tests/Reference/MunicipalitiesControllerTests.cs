using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Reference;

namespace Soovioo.Api.Tests.Reference;

public class MunicipalitiesControllerTests
{
    [Fact]
    public async Task SearchIgnoresCaseAndDiacritics()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: true));
        db.Municipalities.AddRange(
            new Municipality { Code = "554791", Name = "Plzeň", DistrictCode = "CZ0323" },
            new Municipality { Code = "558851", Name = "Plzeň-sever", DistrictCode = "CZ0325" },
            new Municipality { Code = "554782", Name = "Praha", DistrictCode = "CZ0100" });
        await db.SaveChangesAsync();

        var result = await new MunicipalitiesController(db).SearchMunicipalities("PLZEN");

        var matches = Assert.IsAssignableFrom<IEnumerable<Municipality>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["Plzeň", "Plzeň-sever"], matches.Select(m => m.Name));
    }
}
