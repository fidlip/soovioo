using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class SchoolPeriodsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    [Fact]
    public async Task UpsertPeriodCreatesWhenMissing()
    {
        using var db = NewDb();

        var result = await new SchoolPeriodsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpsertPeriod(1, new UpsertSchoolPeriodRequest(new TimeOnly(8, 0), new TimeOnly(8, 45)));

        var dto = Assert.IsType<SchoolPeriodDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.PeriodNumber);
        Assert.Equal(new TimeOnly(8, 0), dto.StartTime);
        Assert.Single(db.SchoolPeriods);
    }

    [Fact]
    public async Task UpsertPeriodUpdatesExistingInsteadOfDuplicating()
    {
        using var db = NewDb();
        db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = SchoolId, PeriodNumber = 1, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 45) });
        await db.SaveChangesAsync();

        var result = await new SchoolPeriodsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpsertPeriod(1, new UpsertSchoolPeriodRequest(new TimeOnly(9, 0), new TimeOnly(9, 45)));

        var dto = Assert.IsType<SchoolPeriodDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new TimeOnly(9, 0), dto.StartTime);
        Assert.Single(db.SchoolPeriods);
    }

    [Fact]
    public async Task UpsertPeriodRejectsEndTimeBeforeStartTime()
    {
        using var db = NewDb();

        var result = await new SchoolPeriodsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpsertPeriod(1, new UpsertSchoolPeriodRequest(new TimeOnly(9, 0), new TimeOnly(8, 0)));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.SchoolPeriods);
    }

    [Fact]
    public async Task DeletePeriodRemovesIt()
    {
        using var db = NewDb();
        db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = SchoolId, PeriodNumber = 1, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 45) });
        await db.SaveChangesAsync();

        var result = await new SchoolPeriodsController(db, new FakeCurrentSchoolContext(SchoolId)).DeletePeriod(1);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(db.SchoolPeriods);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(-1, false)]
    [InlineData(16, false)]
    public async Task UpsertPeriodAllowsZeroPeriodButNotOutOfRange(int periodNumber, bool ok)
    {
        using var db = NewDb();

        var result = await new SchoolPeriodsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpsertPeriod(periodNumber, new UpsertSchoolPeriodRequest(new TimeOnly(7, 0), new TimeOnly(7, 45)));

        if (ok)
        {
            Assert.IsType<OkObjectResult>(result.Result);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }
}
