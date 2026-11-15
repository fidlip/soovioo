using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Tests.SchoolYears;

public class GradingPeriodsTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    [Fact]
    public async Task APeriodStartsAfterThePreviousOneAndTheLastRunsToTheYearsEnd()
    {
        using var db = NewDb();
        db.SchoolYear(2026);

        var periods = await db.GradingPeriodsAsync();

        Assert.Collection(
            periods.Spans,
            first =>
            {
                Assert.Equal("1. pololetí", first.Period.Name);
                Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)), (first.From, first.To));
            },
            second =>
            {
                Assert.Equal("2. pololetí", second.Period.Name);
                Assert.Equal((new DateOnly(2027, 2, 1), new DateOnly(2027, 8, 31)), (second.From, second.To));
            });
    }

    [Theory]
    [InlineData(2026, 9, 1, "1. pololetí")]
    [InlineData(2027, 1, 31, "1. pololetí")]
    [InlineData(2027, 2, 1, "2. pololetí")]
    [InlineData(2027, 6, 30, "2. pololetí")]
    [InlineData(2027, 7, 15, "2. pololetí")]
    [InlineData(2027, 8, 31, "2. pololetí")]
    public async Task ADayBelongsToThePeriodCoveringIt(int year, int month, int day, string expected)
    {
        using var db = NewDb();
        db.SchoolYear(2026);

        var span = (await db.GradingPeriodsAsync()).On(new DateOnly(year, month, day));

        Assert.Equal(expected, span!.Period.Name);
        Assert.Equal("2026/27", span.Year.Name);
    }

    [Fact]
    public async Task ADayOutsideTheSchoolsYearsHasNoPeriod()
    {
        using var db = NewDb();
        db.SchoolYear(2026);
        db.SchoolYear(2026, schoolId: 2);
        db.SchoolYear(2027, schoolId: 2);

        var periods = await db.GradingPeriodsAsync();

        Assert.Null(periods.On(new DateOnly(2026, 8, 31)));
        Assert.Null(periods.On(new DateOnly(2027, 9, 1)));
        Assert.False(await db.HasSchoolYearOnAsync(new DateOnly(2027, 9, 1)));
        Assert.True(await db.HasSchoolYearOnAsync(new DateOnly(2027, 8, 31)));
    }

    [Fact]
    public async Task TheNextYearFollowsWithoutAGap()
    {
        using var db = NewDb();
        db.SchoolYear(2027);
        db.SchoolYear(2026);

        var periods = await db.GradingPeriodsAsync();

        Assert.Equal(["2026/27", "2027/28"], periods.Years.Select(y => y.Name));
        Assert.Equal("2027/28", periods.On(new DateOnly(2027, 9, 1))!.Year.Name);
        Assert.Equal("2027/28", periods.YearOn(new DateOnly(2028, 8, 31))!.Name);
    }

    [Fact]
    public async Task TheYearsLastReportCardIsTheVysvedceniTheOneBeforeItsVypis()
    {
        using var db = NewDb();
        var year = db.SchoolYear(2026);

        var periods = await db.GradingPeriodsAsync();
        var first = periods.Report(year.Periods[0].Id)!;
        var second = periods.Report(year.Periods[1].Id)!;

        Assert.Equal((1, false, new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)), (first.Number, first.Final, first.From, first.To));
        Assert.Equal((2, true, new DateOnly(2027, 2, 1), new DateOnly(2027, 8, 31)), (second.Number, second.Final, second.From, second.To));
        Assert.Equal(new DateOnly(2027, 6, 30), second.IssuedOn);
        Assert.Null(periods.Report(12345));
    }

    [Fact]
    public async Task AReportCardCoversThePeriodsSinceThePreviousOne()
    {
        using var db = NewDb();
        var year = db.SchoolYear(2026);
        year.Periods.Add(new GradingPeriod
        {
            SchoolId = SchoolId, Name = "1. čtvrtletí", To = new DateOnly(2026, 11, 15), Report = PeriodReport.Interim,
        });
        db.SaveChanges();

        var periods = await db.GradingPeriodsAsync();
        var firstHalf = periods.Reports[0];

        Assert.Equal(["1. čtvrtletí", "1. pololetí", "2. pololetí"], periods.Spans.Select(s => s.Period.Name));
        Assert.Equal(new DateOnly(2026, 11, 16), periods.Spans[1].From);
        Assert.Equal("1. pololetí", firstHalf.Period.Name);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31), 1), (firstHalf.From, firstHalf.To, firstHalf.Number));
    }

    [Fact]
    public async Task LessonsEndWithTheYearsLastPeriodAfterItWithTheNextYears()
    {
        using var db = NewDb();
        db.SchoolYear(2026);

        var periods = await db.GradingPeriodsAsync();
        Assert.Equal(new DateOnly(2027, 6, 30), periods.TeachingEnd(new DateOnly(2026, 11, 2)));
        Assert.Equal(new DateOnly(2027, 6, 30), periods.TeachingEnd(new DateOnly(2027, 6, 30)));
        Assert.Null(periods.TeachingEnd(new DateOnly(2027, 7, 1)));

        db.SchoolYear(2027);
        Assert.Equal(new DateOnly(2028, 6, 30), (await db.GradingPeriodsAsync()).TeachingEnd(new DateOnly(2027, 7, 1)));
    }
}
