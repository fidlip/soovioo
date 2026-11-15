using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.SchoolYears;

/// <summary>The school's years and their periods. Everyone signed in reads
/// them (the report card's periods, whether today has a year); the
/// management founds a year and sets its dates.</summary>
[Authorize]
[ApiController]
[Route("api/school-years")]
[AuditArea("Školní rok")]
public class SchoolYearsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SchoolYearDto>>> GetSchoolYears()
    {
        var periods = await db.GradingPeriodsAsync();
        return Ok(periods.Years.Select(y => Dto(periods, y)).ToList());
    }

    /// <summary>The year to found next — the one after the school's last, or
    /// the one running today — with the dates to start from.</summary>
    [HttpGet("suggestion")]
    [RequirePermission(Permission.SchoolYear)]
    public async Task<ActionResult<SchoolYearSuggestionDto>> GetSuggestion()
    {
        var startYear = await NextStartYearAsync();
        var dates = MsmtCalendar.For(startYear);
        return Ok(new SchoolYearSuggestionDto(
            startYear, SchoolYear.NameOf(startYear), dates.TeachingStart, dates.FirstHalfIssuedOn, dates.SecondHalfIssuedOn, dates.Official));
    }

    [HttpPost]
    [RequirePermission(Permission.SchoolYear)]
    [AuditAction("Založen školní rok")]
    public async Task<ActionResult<SchoolYearDto>> Create(CreateSchoolYearRequest request)
    {
        if (request.StartYear != await NextStartYearAsync())
        {
            return BadRequest("Školní roky musí jít po sobě.");
        }
        var dates = new SchoolYearDates(request.TeachingStart, request.FirstHalfIssuedOn, request.SecondHalfIssuedOn, false);
        if (Validate(request.StartYear, dates) is { } invalid)
        {
            return invalid;
        }

        var year = SchoolYear.Create(currentSchool.SchoolId!.Value, request.StartYear, dates);
        db.SchoolYears.Add(year);
        await db.SaveChangesAsync();
        return Ok(Dto(await db.GradingPeriodsAsync(), year));
    }

    /// <summary>The year's dates; its periods' ends are the law's.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permission.SchoolYear)]
    [AuditAction("Upraveny termíny školního roku")]
    public async Task<ActionResult<SchoolYearDto>> Update(int id, UpdateSchoolYearRequest request)
    {
        var year = await db.SchoolYears.Include(y => y.Periods).FirstOrDefaultAsync(y => y.Id == id);
        if (year is null)
        {
            return NotFound();
        }
        var reports = year.Periods.Where(p => p.Report == PeriodReport.ReportCard).OrderBy(p => p.To).ToList();
        if (reports.Count != 2)
        {
            return BadRequest("Školní rok nemá dvě pololetí.");
        }
        var dates = new SchoolYearDates(request.TeachingStart, request.FirstHalfIssuedOn, request.SecondHalfIssuedOn, false);
        if (Validate(year.From.Year, dates) is { } invalid)
        {
            return invalid;
        }

        year.TeachingStart = request.TeachingStart;
        reports[0].IssuedOn = request.FirstHalfIssuedOn;
        reports[1].IssuedOn = request.SecondHalfIssuedOn;
        await db.SaveChangesAsync();
        return Ok(Dto(await db.GradingPeriodsAsync(), year));
    }

    private async Task<int> NextStartYearAsync()
    {
        var last = await db.SchoolYears.OrderByDescending(y => y.From).Select(y => (DateOnly?)y.From).FirstOrDefaultAsync();
        return last?.Year + 1 ?? (db.Today.Month >= 9 ? db.Today.Year : db.Today.Year - 1);
    }

    private ActionResult? Validate(int startYear, SchoolYearDates dates)
    {
        if (dates.TeachingStart.Year != startYear || dates.TeachingStart.Month != 9)
        {
            return this.FieldError("teachingStart", $"Vyučování začíná v září {startYear}.");
        }
        if (dates.FirstHalfIssuedOn.Year != startYear + 1 || dates.FirstHalfIssuedOn.Month != 1)
        {
            return this.FieldError("firstHalfIssuedOn", $"Výpis z vysvědčení se vydává v lednu {startYear + 1}.");
        }
        if (dates.SecondHalfIssuedOn.Year != startYear + 1 || dates.SecondHalfIssuedOn.Month != 6)
        {
            return this.FieldError("secondHalfIssuedOn", $"Vysvědčení se vydává v červnu {startYear + 1}.");
        }
        return null;
    }

    private SchoolYearDto Dto(GradingPeriods periods, SchoolYear year) => new(
        year.Id,
        year.Name,
        year.From,
        year.To,
        year.TeachingStart,
        year.From <= db.Today && db.Today <= year.To,
        [.. periods.Spans
            .Where(s => s.Year.Id == year.Id)
            .Select(s =>
            {
                var report = periods.Report(s.Period.Id);
                return new GradingPeriodDto(
                    s.Period.Id, s.Period.Name, s.From, s.To, s.Period.Report, s.Period.IssuedOn, report?.Number, report?.Final ?? false);
            })]);
}

/// <param name="From">With <paramref name="To"/>: the days the period
/// covers — the year's last one to the year's end.</param>
/// <param name="ReportNumber">Which of the year's report cards it ends with,
/// from 1; null without one.</param>
/// <param name="Final">Its report card is the year's vysvědčení, not a
/// výpis.</param>
public record GradingPeriodDto(
    int Id, string Name, DateOnly From, DateOnly To, PeriodReport Report, DateOnly? IssuedOn, int? ReportNumber, bool Final);

/// <param name="Current">Today is in it.</param>
public record SchoolYearDto(
    int Id,
    string Name,
    DateOnly From,
    DateOnly To,
    DateOnly TeachingStart,
    bool Current,
    IReadOnlyList<GradingPeriodDto> Periods);

/// <param name="Official">The dates are MŠMT's; otherwise an estimate.</param>
public record SchoolYearSuggestionDto(
    int StartYear,
    string Name,
    DateOnly TeachingStart,
    DateOnly FirstHalfIssuedOn,
    DateOnly SecondHalfIssuedOn,
    bool Official);

public record CreateSchoolYearRequest(
    int StartYear, DateOnly TeachingStart, DateOnly FirstHalfIssuedOn, DateOnly SecondHalfIssuedOn);

public record UpdateSchoolYearRequest(DateOnly TeachingStart, DateOnly FirstHalfIssuedOn, DateOnly SecondHalfIssuedOn);
