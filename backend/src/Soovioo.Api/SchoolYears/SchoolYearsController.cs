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
/// management founds a year and changes its days and periods.</summary>
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
        var daysOff = await db.SchoolDaysOff.AsNoTracking().ToListAsync();
        return Ok(periods.Years.Select(y => Dto(periods, y, daysOff)).ToList());
    }

    /// <summary>The okresy the jarní prázdniny go by, alphabetically.</summary>
    [HttpGet("districts")]
    public ActionResult<IReadOnlyList<string>> GetDistricts() => Ok(Districts.All);

    /// <summary>The jarní prázdniny of the <paramref name="district"/> in the
    /// school year starting in <paramref name="startYear"/>.</summary>
    [HttpGet("spring-holidays")]
    [RequirePermission(Permission.SchoolYear)]
    public ActionResult<SchoolDayOffRequest> GetSpringHolidays(int startYear, string district)
    {
        if (!Districts.IsKnown(district))
        {
            return this.FieldError("district", "Takový okres neznáme.");
        }
        var monday = Districts.SpringHolidays(startYear, district);
        return Ok(new SchoolDayOffRequest(null, monday, monday.AddDays(4), "Jarní prázdniny", DayOffKind.Holidays, []));
    }

    /// <summary>The year to found next — the one after the school's last, or
    /// the one running today — with MŠMT's dates, its two pololetí and its
    /// prázdniny and public holidays; the jarní prázdniny of the
    /// <paramref name="district"/>, by default the school's.</summary>
    [HttpGet("suggestion")]
    [RequirePermission(Permission.SchoolYear)]
    public async Task<ActionResult<SchoolYearSuggestionDto>> GetSuggestion(string? district = null)
    {
        district ??= await db.Schools.Where(s => s.Id == currentSchool.SchoolId).Select(s => s.District).FirstOrDefaultAsync();
        var last = await db.SchoolYears.OrderByDescending(y => y.To).FirstOrDefaultAsync();
        var startYear = last is null
            ? (db.Today.Month >= 9 ? db.Today.Year : db.Today.Year - 1)
            : last.To.AddDays(1).Year - (last.To.AddDays(1).Month >= 7 ? 0 : 1);
        var dates = MsmtCalendar.For(startYear);
        var year = SchoolYear.Create(currentSchool.SchoolId!.Value, startYear, dates);
        if (last is not null)
        {
            year.From = last.To.AddDays(1);
            if (year.TeachingStart < year.From)
            {
                year.TeachingStart = year.From;
            }
        }
        return Ok(new SchoolYearSuggestionDto(
            year.From,
            year.To,
            year.TeachingStart,
            year.TeachingEnd,
            [.. year.Periods.Select(p => new GradingPeriodRequest(null, p.Name, p.To, p.Report, p.IssuedOn))],
            dates.Official,
            [.. MsmtCalendar.DaysOff(startYear, year.TeachingStart, year.TeachingEnd, district)
                .Select(d => new SchoolDayOffRequest(null, d.From, d.To, d.Name, d.Kind, []))],
            district));
    }

    [HttpPost]
    [RequirePermission(Permission.SchoolYear)]
    [AuditAction("Založen školní rok")]
    public async Task<ActionResult<SchoolYearDto>> Create(SchoolYearRequest request)
    {
        if (await ValidateAsync(null, request) is { } invalid)
        {
            return invalid;
        }

        var year = new SchoolYear { SchoolId = currentSchool.SchoolId!.Value, Name = "" };
        await ApplyAsync(year, request);
        db.SchoolYears.Add(year);
        await db.SaveChangesAsync();
        return Ok(Dto(await db.GradingPeriodsAsync(), year, year.DaysOff));
    }

    /// <summary>The year's days and periods. A period that has report card
    /// grades, notes or a behaviour classification stays; the year keeps
    /// every grade, absence, excuse and behaviour record it holds.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permission.SchoolYear)]
    [AuditAction("Upraven školní rok")]
    public async Task<ActionResult<SchoolYearDto>> Update(int id, SchoolYearRequest request)
    {
        var year = await db.SchoolYears.Include(y => y.Periods).Include(y => y.DaysOff).FirstOrDefaultAsync(y => y.Id == id);
        if (year is null)
        {
            return NotFound();
        }
        if (await ValidateAsync(year, request) is { } invalid)
        {
            return invalid;
        }

        var removed = year.Periods
            .Where(p => !request.Periods.Any(r => r.Id == p.Id))
            .ToList();
        foreach (var period in removed)
        {
            if (await UsedAsync(period.Id))
            {
                return BadRequest($"Období {period.Name} už má známky na vysvědčení nebo klasifikaci chování, odebrat ho nejde.");
            }
        }
        var outside = await OutsideAsync(year.From, year.To, request.From, request.To);
        if (outside > 0)
        {
            return BadRequest(
                $"Mimo nové dny roku by zůstalo {outside} záznamů (známky, absence, omluvenky, chování). Školní rok je musí dál obsahovat.");
        }

        db.GradingPeriods.RemoveRange(removed);
        await ApplyAsync(year, request);
        await db.SaveChangesAsync();
        return Ok(Dto(await db.GradingPeriodsAsync(), year, year.DaysOff));
    }

    private async Task ApplyAsync(SchoolYear year, SchoolYearRequest request)
    {
        if (request.District is { } district)
        {
            var school = await db.Schools.FirstAsync(s => s.Id == year.SchoolId);
            school.District = district;
        }
        if (request.DaysOff is { } daysOff)
        {
            db.SchoolDaysOff.RemoveRange(year.DaysOff.Where(d => !daysOff.Any(r => r.Id == d.Id)));
            foreach (var item in daysOff)
            {
                var dayOff = year.DaysOff.FirstOrDefault(d => item.Id is { } id && d.Id == id);
                if (dayOff is null)
                {
                    dayOff = new SchoolDayOff { SchoolId = year.SchoolId, Name = "" };
                    year.DaysOff.Add(dayOff);
                }
                dayOff.From = item.From;
                dayOff.To = item.To;
                dayOff.Name = item.Name.Trim();
                dayOff.Kind = item.Kind;
                dayOff.ClassIds = [.. (item.ClassIds ?? []).Distinct().Order()];
            }
        }

        year.Name = SchoolYear.NameOf(request.From, request.To);
        year.From = request.From;
        year.To = request.To;
        year.TeachingStart = request.TeachingStart;
        year.TeachingEnd = request.TeachingEnd;
        foreach (var item in request.Periods)
        {
            var period = year.Periods.FirstOrDefault(p => item.Id is { } id && p.Id == id);
            if (period is null)
            {
                period = new GradingPeriod { SchoolId = year.SchoolId, Name = "" };
                year.Periods.Add(period);
            }
            period.Name = item.Name.Trim();
            period.To = item.To;
            period.Report = item.Report;
            period.IssuedOn = item.Report == PeriodReport.None ? null : item.IssuedOn;
        }
    }

    private async Task<ActionResult?> ValidateAsync(SchoolYear? year, SchoolYearRequest request)
    {
        if (request.To <= request.From)
        {
            return this.FieldError("to", "Školní rok musí končit po svém začátku.");
        }
        if (request.To >= request.From.AddYears(1))
        {
            return this.FieldError("to", "Školní rok trvá nejvýš rok.");
        }
        var overlapping = await db.SchoolYears
            .Where(y => y.Id != (year == null ? 0 : year.Id) && y.From <= request.To && request.From <= y.To)
            .Select(y => y.Name)
            .FirstOrDefaultAsync();
        if (overlapping is not null)
        {
            return this.FieldError("from", $"Překrývá se se školním rokem {overlapping}.");
        }
        if (request.TeachingStart < request.From || request.TeachingStart > request.To)
        {
            return this.FieldError("teachingStart", "Vyučování začíná ve školním roce.");
        }
        if (request.TeachingEnd <= request.TeachingStart || request.TeachingEnd > request.To)
        {
            return this.FieldError("teachingEnd", "Vyučování končí po svém začátku a ve školním roce.");
        }

        if (request.Periods.Count == 0)
        {
            return this.FieldError("periods", "Školní rok má aspoň jedno období.");
        }
        if (request.Periods.Where(p => p.Id is not null).Any(p => year?.Periods.Any(e => e.Id == p.Id) != true))
        {
            return BadRequest(GradingPeriods.UnknownPeriod);
        }
        var from = request.From;
        for (var i = 0; i < request.Periods.Count; i++)
        {
            var period = request.Periods[i];
            var last = i == request.Periods.Count - 1;
            if (string.IsNullOrWhiteSpace(period.Name))
            {
                return this.FieldError($"periods[{i}].name", "Vyplňte název období.");
            }
            if (request.Periods.Take(i).Any(p => p.Name.Trim() == period.Name.Trim()))
            {
                return this.FieldError($"periods[{i}].name", "Období se v roce jmenují různě.");
            }
            if (last && period.To != request.To)
            {
                return this.FieldError($"periods[{i}].to", "Poslední období končí s koncem školního roku.");
            }
            if (period.To < from || period.To > request.To || (!last && period.To == request.To))
            {
                return this.FieldError($"periods[{i}].to", i == 0
                    ? "Období končí ve školním roce a před koncem dalšího."
                    : "Období končí po předchozím a před koncem dalšího.");
            }
            if (period.Report == PeriodReport.ReportCard && period.IssuedOn is null)
            {
                return this.FieldError($"periods[{i}].issuedOn", "Vyplňte den vydání vysvědčení.");
            }
            if (period.Report != PeriodReport.None && period.IssuedOn is { } issued && (issued < from || issued > request.To))
            {
                return this.FieldError($"periods[{i}].issuedOn", "Vydává se v období nebo po něm, ve školním roce.");
            }
            from = period.To.AddDays(1);
        }

        if (request.District is { } district && !Districts.IsKnown(district))
        {
            return this.FieldError("district", "Takový okres neznáme.");
        }
        if (request.DaysOff is { } daysOff)
        {
            if (daysOff.Where(d => d.Id is not null).Any(d => year?.DaysOff.Any(e => e.Id == d.Id) != true))
            {
                return BadRequest("Takový volný den rok nemá.");
            }
            var classIds = daysOff.SelectMany(d => d.ClassIds ?? []).Distinct().ToList();
            if (await db.SchoolClasses.CountAsync(c => classIds.Contains(c.Id)) != classIds.Count)
            {
                return BadRequest("Taková třída není.");
            }
            for (var i = 0; i < daysOff.Count; i++)
            {
                var dayOff = daysOff[i];
                if (string.IsNullOrWhiteSpace(dayOff.Name))
                {
                    return this.FieldError($"daysOff[{i}].name", "Vyplňte název volna.");
                }
                if (dayOff.Name.Trim().Length > 100)
                {
                    return this.FieldError($"daysOff[{i}].name", "Název je nejvýš na 100 znaků.");
                }
                if (dayOff.To < dayOff.From)
                {
                    return this.FieldError($"daysOff[{i}].to", "Volno končí po svém začátku.");
                }
                if (dayOff.From < request.From || dayOff.To > request.To)
                {
                    return this.FieldError($"daysOff[{i}].from", "Volno je ve školním roce.");
                }
            }
        }
        return null;
    }

    private async Task<bool> UsedAsync(int periodId) =>
        await db.ReportCardGrades.AnyAsync(g => g.GradingPeriodId == periodId)
        || await db.ReportCardNotes.AnyAsync(n => n.GradingPeriodId == periodId)
        || await db.BehaviorClassifications.AnyAsync(c => c.GradingPeriodId == periodId);

    /// <summary>How many dated records of the old days the new ones leave
    /// out.</summary>
    private async Task<int> OutsideAsync(DateOnly oldFrom, DateOnly oldTo, DateOnly from, DateOnly to)
    {
        var grades = await db.Grades.CountAsync(g => g.GradedOn >= oldFrom && g.GradedOn <= oldTo && (g.GradedOn < from || g.GradedOn > to));
        var absences = await db.Absences.CountAsync(a => a.Date >= oldFrom && a.Date <= oldTo && (a.Date < from || a.Date > to));
        var behavior = await db.BehaviorRecords.CountAsync(r => r.Date >= oldFrom && r.Date <= oldTo && (r.Date < from || r.Date > to));
        var excuses = await db.Excuses.CountAsync(e => e.DateTo >= oldFrom && e.DateFrom <= oldTo
            && (e.DateFrom < from || e.DateTo > to));
        return grades + absences + behavior + excuses;
    }

    private SchoolYearDto Dto(GradingPeriods periods, SchoolYear year, List<SchoolDayOff> daysOff) => new(
        year.Id,
        year.Name,
        year.From,
        year.To,
        year.TeachingStart,
        year.TeachingEnd,
        year.From <= db.Today && db.Today <= year.To,
        [.. daysOff.Where(d => d.SchoolYearId == year.Id).OrderBy(d => d.From).ThenBy(d => d.Id)
            .Select(d => new SchoolDayOffDto(d.Id, d.From, d.To, d.Name, d.Kind, d.ClassIds))],
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
/// covers.</param>
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
    DateOnly TeachingEnd,
    bool Current,
    IReadOnlyList<SchoolDayOffDto> DaysOff,
    IReadOnlyList<GradingPeriodDto> Periods);

/// <param name="ClassIds">Empty = the whole school.</param>
public record SchoolDayOffDto(int Id, DateOnly From, DateOnly To, string Name, DayOffKind Kind, IReadOnlyList<int> ClassIds);

/// <param name="Id">An existing day off of the year; null for a new one.</param>
/// <param name="ClassIds">Empty or null = the whole school.</param>
public record SchoolDayOffRequest(int? Id, DateOnly From, DateOnly To, string Name, DayOffKind Kind, IReadOnlyList<int>? ClassIds);

/// <param name="Official">The dates are MŠMT's; otherwise an estimate.</param>
/// <param name="District">The okres of the jarní prázdniny in
/// <paramref name="DaysOff"/>; null without them.</param>
public record SchoolYearSuggestionDto(
    DateOnly From,
    DateOnly To,
    DateOnly TeachingStart,
    DateOnly TeachingEnd,
    IReadOnlyList<GradingPeriodRequest> Periods,
    bool Official,
    IReadOnlyList<SchoolDayOffRequest> DaysOff,
    string? District);

/// <param name="Id">An existing period of the year; null for a new one.</param>
/// <param name="To">The period's last day; the last period's is the
/// year's.</param>
public record GradingPeriodRequest(int? Id, string Name, DateOnly To, PeriodReport Report, DateOnly? IssuedOn);

/// <param name="Periods">In order; they follow one another from the year's
/// start.</param>
/// <param name="DaysOff">All the year's days off; null leaves them.</param>
/// <param name="District">Sets the school's okres; null leaves it.</param>
public record SchoolYearRequest(
    DateOnly From,
    DateOnly To,
    DateOnly TeachingStart,
    DateOnly TeachingEnd,
    IReadOnlyList<GradingPeriodRequest> Periods,
    IReadOnlyList<SchoolDayOffRequest>? DaysOff = null,
    string? District = null);
