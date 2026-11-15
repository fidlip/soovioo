using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Tests.SchoolYears;

public class SchoolYearsControllerTests : IDisposable
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId)) { Today = new DateOnly(2026, 11, 2) };

    public void Dispose() => db.Dispose();

    private SchoolYearsController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 1, UserRole.Admin, SchoolId)),
        };

    private static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private static CreateSchoolYearRequest Request(int startYear) =>
        new(startYear, new DateOnly(startYear, 9, 1), new DateOnly(startYear + 1, 1, 28), new DateOnly(startYear + 1, 6, 30));

    [Fact]
    public async Task ASchoolWithoutAYearIsOfferedTheOneRunningTodayWithMsmtsDates()
    {
        var suggestion = Ok(await Controller().GetSuggestion());

        Assert.Equal((2026, "2026/27", true), (suggestion.StartYear, suggestion.Name, suggestion.Official));
        Assert.Equal(new DateOnly(2027, 1, 28), suggestion.FirstHalfIssuedOn);

        db.Today = new DateOnly(2027, 8, 15);
        Assert.Equal(2026, Ok(await Controller().GetSuggestion()).StartYear);
    }

    [Fact]
    public async Task TheNextYearFollowsTheSchoolsLast()
    {
        Ok(await Controller().Create(Request(2026)));

        var suggestion = Ok(await Controller().GetSuggestion());
        Assert.Equal((2027, new DateOnly(2028, 1, 31)), (suggestion.StartYear, suggestion.FirstHalfIssuedOn));
        Assert.IsType<BadRequestObjectResult>((await Controller().Create(Request(2028))).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller().Create(Request(2026))).Result);
        Ok(await Controller().Create(Request(2027)));

        Assert.Equal(["2026/27", "2027/28"], Ok(await Controller().GetSchoolYears()).Select(y => y.Name));
    }

    [Fact]
    public async Task AFoundedYearHasItsTwoPololeti()
    {
        var year = Ok(await Controller().Create(Request(2026)));

        Assert.Equal(("2026/27", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), true), (year.Name, year.From, year.To, year.Current));
        Assert.Collection(
            year.Periods,
            first =>
            {
                Assert.Equal(("1. pololetí", PeriodReport.ReportCard, 1, false), (first.Name, first.Report, first.ReportNumber, first.Final));
                Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31)), (first.From, first.To));
                Assert.Equal(new DateOnly(2027, 1, 28), first.IssuedOn);
            },
            second =>
            {
                Assert.Equal(("2. pololetí", PeriodReport.ReportCard, 2, true), (second.Name, second.Report, second.ReportNumber, second.Final));
                Assert.Equal((new DateOnly(2027, 2, 1), new DateOnly(2027, 8, 31)), (second.From, second.To));
                Assert.Equal(new DateOnly(2027, 6, 30), second.IssuedOn);
            });
    }

    [Fact]
    public async Task TheDatesMustLieInTheirMonths()
    {
        var late = Request(2026) with { TeachingStart = new DateOnly(2026, 10, 1) };
        var early = Request(2026) with { FirstHalfIssuedOn = new DateOnly(2026, 12, 20) };
        var july = Request(2026) with { SecondHalfIssuedOn = new DateOnly(2027, 7, 1) };

        foreach (var (request, field) in new[] { (late, "teachingStart"), (early, "firstHalfIssuedOn"), (july, "secondHalfIssuedOn") })
        {
            var problem = Assert.IsType<ValidationProblemDetails>(
                Assert.IsType<BadRequestObjectResult>((await Controller().Create(request)).Result).Value);
            Assert.Equal([field], problem.Errors.Keys);
        }
        Assert.Empty(db.SchoolYears);
    }

    [Fact]
    public async Task TheManagementMovesTheYearsDates()
    {
        var year = Ok(await Controller().Create(Request(2026)));

        var updated = Ok(await Controller().Update(
            year.Id, new(new DateOnly(2026, 9, 2), new DateOnly(2027, 1, 29), new DateOnly(2027, 6, 25))));

        Assert.Equal(new DateOnly(2026, 9, 2), updated.TeachingStart);
        Assert.Equal([new DateOnly(2027, 1, 29), new DateOnly(2027, 6, 25)], updated.Periods.Select(p => p.IssuedOn!.Value));
        Assert.Equal([new DateOnly(2027, 1, 31), new DateOnly(2027, 8, 31)], updated.Periods.Select(p => p.To));
        Assert.IsType<BadRequestObjectResult>((await Controller().Update(
            year.Id, new(new DateOnly(2026, 9, 2), new DateOnly(2027, 2, 1), new DateOnly(2027, 6, 25)))).Result);
        Assert.IsType<NotFoundResult>((await Controller().Update(
            999, new(new DateOnly(2026, 9, 2), new DateOnly(2027, 1, 29), new DateOnly(2027, 6, 25)))).Result);
    }

    [Fact]
    public async Task AnotherSchoolsYearsStayItsOwn()
    {
        db.SchoolYear(2025, schoolId: 2);

        Assert.Empty(Ok(await Controller().GetSchoolYears()));
        Assert.Equal(2026, Ok(await Controller().GetSuggestion()).StartYear);
    }
}
