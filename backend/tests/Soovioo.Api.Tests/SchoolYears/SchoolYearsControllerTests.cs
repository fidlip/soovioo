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

    private static GradingPeriodRequest Period(string name, DateOnly to, DateOnly? issuedOn, int? id = null) =>
        new(id, name, to, issuedOn is null ? PeriodReport.None : PeriodReport.ReportCard, issuedOn);

    private static SchoolYearRequest Request(int startYear) =>
        new(
            new DateOnly(startYear, 9, 1),
            new DateOnly(startYear + 1, 8, 31),
            new DateOnly(startYear, 9, 1),
            new DateOnly(startYear + 1, 6, 30),
            [
                Period("1. pololetí", new DateOnly(startYear + 1, 1, 31), new DateOnly(startYear + 1, 1, 28)),
                Period("2. pololetí", new DateOnly(startYear + 1, 8, 31), new DateOnly(startYear + 1, 6, 30)),
            ]);

    private static ValidationProblemDetails Problem<T>(ActionResult<T> result) =>
        Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    [Fact]
    public async Task ASchoolWithoutAYearIsOfferedTheOneRunningTodayWithMsmtsDatesAndTwoPololeti()
    {
        var suggestion = Ok(await Controller().GetSuggestion());

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), true), (suggestion.From, suggestion.To, suggestion.Official));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30)), (suggestion.TeachingStart, suggestion.TeachingEnd));
        Assert.Equal(
            [("1. pololetí", new DateOnly(2027, 1, 31), (DateOnly?)new DateOnly(2027, 1, 28)), ("2. pololetí", new DateOnly(2027, 8, 31), new DateOnly(2027, 6, 30))],
            suggestion.Periods.Select(p => (p.Name, p.To, p.IssuedOn)));

        db.Today = new DateOnly(2027, 8, 15);
        Assert.Equal(new DateOnly(2026, 9, 1), Ok(await Controller().GetSuggestion()).From);
    }

    [Fact]
    public async Task TheNextYearIsOfferedFromTheDayAfterTheSchoolsLast()
    {
        Ok(await Controller().Create(Request(2026) with { To = new DateOnly(2027, 8, 20), Periods =
            [
                Period("1. pololetí", new DateOnly(2027, 1, 31), new DateOnly(2027, 1, 28)),
                Period("2. pololetí", new DateOnly(2027, 8, 20), new DateOnly(2027, 6, 30)),
            ] }));

        var suggestion = Ok(await Controller().GetSuggestion());
        Assert.Equal((new DateOnly(2027, 8, 21), new DateOnly(2028, 8, 31)), (suggestion.From, suggestion.To));
        Assert.Equal(new DateOnly(2028, 1, 31), suggestion.Periods[0].IssuedOn);
    }

    [Fact]
    public async Task YearsMustNotOverlap()
    {
        Ok(await Controller().Create(Request(2026)));

        Assert.Equal(["from"], Problem(await Controller().Create(Request(2026))).Errors.Keys);
        Assert.Equal(["from"], Problem(await Controller().Create(Request(2027) with { From = new DateOnly(2027, 8, 31), To = new DateOnly(2028, 8, 30) })).Errors.Keys);
        Ok(await Controller().Create(Request(2028)));
        Ok(await Controller().Create(Request(2025)));

        Assert.Equal(["2025/26", "2026/27", "2028/29"], Ok(await Controller().GetSchoolYears()).Select(y => y.Name));
    }

    [Fact]
    public async Task AFoundedYearHasThePeriodsAsGiven()
    {
        var year = Ok(await Controller().Create(Request(2026)));

        Assert.Equal(("2026/27", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), true), (year.Name, year.From, year.To, year.Current));
        Assert.Equal(new DateOnly(2027, 6, 30), year.TeachingEnd);
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
    public async Task ASchoolDividesItsYearIntoTrimestersWithAnInterimAssessment()
    {
        var year = Ok(await Controller().Create(Request(2026) with { Periods =
            [
                Period("1. trimestr", new DateOnly(2026, 11, 30), new DateOnly(2026, 11, 27)),
                new(null, "2. trimestr", new DateOnly(2027, 2, 28), PeriodReport.Interim, null),
                Period("3. trimestr", new DateOnly(2027, 8, 31), new DateOnly(2027, 6, 30)),
            ] }));

        Assert.Equal(
            [
                ("1. trimestr", new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30), (int?)1, false),
                ("2. trimestr", new DateOnly(2026, 12, 1), new DateOnly(2027, 2, 28), null, false),
                ("3. trimestr", new DateOnly(2027, 3, 1), new DateOnly(2027, 8, 31), 2, true),
            ],
            year.Periods.Select(p => (p.Name, p.From, p.To, p.ReportNumber, p.Final)));
    }

    [Fact]
    public async Task TheYearAndItsPeriodsMustHoldTogether()
    {
        var cases = new (SchoolYearRequest Request, string Field)[]
        {
            (Request(2026) with { To = new DateOnly(2027, 9, 1) }, "to"),
            (Request(2026) with { TeachingStart = new DateOnly(2026, 8, 31) }, "teachingStart"),
            (Request(2026) with { TeachingEnd = new DateOnly(2027, 9, 1) }, "teachingEnd"),
            (Request(2026) with { Periods = [] }, "periods"),
            (Request(2026) with { Periods = [Period(" ", new DateOnly(2027, 8, 31), null)] }, "periods[0].name"),
            (Request(2026) with { Periods = [Period("A", new DateOnly(2027, 6, 30), null)] }, "periods[0].to"),
            (Request(2026) with { Periods = [Period("A", new DateOnly(2027, 2, 1), null), Period("B", new DateOnly(2027, 1, 31), null), Period("C", new DateOnly(2027, 8, 31), null)] }, "periods[1].to"),
            (Request(2026) with { Periods = [Period("A", new DateOnly(2027, 2, 1), null), Period("A", new DateOnly(2027, 8, 31), null)] }, "periods[1].name"),
            (Request(2026) with { Periods = [new(null, "A", new DateOnly(2027, 8, 31), PeriodReport.ReportCard, null)] }, "periods[0].issuedOn"),
            (Request(2026) with { Periods = [Period("A", new DateOnly(2027, 1, 31), new DateOnly(2027, 1, 28)), Period("B", new DateOnly(2027, 8, 31), new DateOnly(2027, 1, 30))] }, "periods[1].issuedOn"),
        };

        foreach (var (request, field) in cases)
        {
            Assert.Equal([field], Problem(await Controller().Create(request)).Errors.Keys);
        }
        Assert.Empty(db.SchoolYears);
    }

    [Fact]
    public async Task TheManagementChangesTheYearsDaysAndPeriods()
    {
        var year = Ok(await Controller().Create(Request(2026)));
        var (first, second) = (year.Periods[0], year.Periods[1]);

        var updated = Ok(await Controller().Update(year.Id, Request(2026) with
        {
            TeachingStart = new DateOnly(2026, 9, 2),
            Periods =
            [
                Period("1. čtvrtletí", new DateOnly(2026, 11, 15), null, first.Id),
                Period("1. pololetí", new DateOnly(2027, 1, 31), new DateOnly(2027, 1, 29)),
                Period("2. pololetí", new DateOnly(2027, 8, 31), new DateOnly(2027, 6, 25), second.Id),
            ],
        }));

        Assert.Equal(new DateOnly(2026, 9, 2), updated.TeachingStart);
        Assert.Equal(
            [("1. čtvrtletí", new DateOnly(2026, 11, 15)), ("1. pololetí", new DateOnly(2027, 1, 31)), ("2. pololetí", new DateOnly(2027, 8, 31))],
            updated.Periods.Select(p => (p.Name, p.To)));
        Assert.Equal([first.Id, second.Id], updated.Periods.Where(p => p.Id == first.Id || p.Id == second.Id).Select(p => p.Id));
        Assert.IsType<NotFoundResult>((await Controller().Update(999, Request(2026))).Result);
    }

    [Fact]
    public async Task APeriodWithReportCardGradesStays()
    {
        var year = Ok(await Controller().Create(Request(2026)));
        db.ReportCardGrades.Add(new() { SchoolId = SchoolId, StudentId = 1, SubjectId = 1, GradingPeriodId = year.Periods[0].Id, SetByName = "Učitel" });
        await db.SaveChangesAsync();

        var result = await Controller().Update(year.Id, Request(2026) with
        {
            Periods = [Period("Celý rok", new DateOnly(2027, 8, 31), new DateOnly(2027, 6, 30), year.Periods[1].Id)],
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(2, db.GradingPeriods.Count());
    }

    [Fact]
    public async Task TheYearKeepsItsRecords()
    {
        var year = Ok(await Controller().Create(Request(2026)));
        db.Absences.Add(new() { SchoolId = SchoolId, StudentId = 1, Date = new DateOnly(2027, 8, 20) });
        await db.SaveChangesAsync();
        var shorter = Request(2026) with
        {
            To = new DateOnly(2027, 8, 15),
            Periods =
            [
                Period("1. pololetí", new DateOnly(2027, 1, 31), new DateOnly(2027, 1, 28), year.Periods[0].Id),
                Period("2. pololetí", new DateOnly(2027, 8, 15), new DateOnly(2027, 6, 30), year.Periods[1].Id),
            ],
        };

        Assert.IsType<BadRequestObjectResult>((await Controller().Update(year.Id, shorter)).Result);
        Assert.Equal(new DateOnly(2027, 8, 31), db.SchoolYears.Single().To);
    }

    [Fact]
    public async Task AnotherSchoolsYearsStayItsOwn()
    {
        db.SchoolYear(2025, schoolId: 2);

        Assert.Empty(Ok(await Controller().GetSchoolYears()));
        Assert.Equal(new DateOnly(2026, 9, 1), Ok(await Controller().GetSuggestion()).From);
    }
}
