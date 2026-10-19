using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable;

/// <summary>Školní "zvonění" (čas od-do per číslo hodiny) — čtou všichni
/// přihlášení (je potřeba pro zobrazení rozvrhu), upravují jen Teacher/Admin,
/// stejně jako samotné hodiny v rozvrhu.</summary>
[Authorize]
[ApiController]
[Route("api/school-periods")]
public class SchoolPeriodsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolPeriodDto>>> GetPeriods()
    {
        var periods = await db.SchoolPeriods
            .OrderBy(p => p.PeriodNumber)
            .Select(p => new SchoolPeriodDto(p.PeriodNumber, p.StartTime, p.EndTime))
            .ToListAsync();

        return Ok(periods);
    }

    /// <summary>Upsert by period number — there's no separate "create" step
    /// from the UI, just "set period N's bell times".</summary>
    [HttpPut("{periodNumber:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<SchoolPeriodDto>> UpsertPeriod(int periodNumber, UpsertSchoolPeriodRequest request)
    {
        // 0 = "nultá hodina" (before the regular first period).
        if (periodNumber is < 0 or > 15)
        {
            return BadRequest("Číslo hodiny musí být 0–15.");
        }

        if (request.EndTime <= request.StartTime)
        {
            return BadRequest("Konec hodiny musí být po jejím začátku.");
        }

        var period = await db.SchoolPeriods.FirstOrDefaultAsync(p => p.PeriodNumber == periodNumber);
        if (period is null)
        {
            period = new SchoolPeriod { PeriodNumber = periodNumber, SchoolId = currentSchool.SchoolId!.Value };
            db.SchoolPeriods.Add(period);
        }

        period.StartTime = request.StartTime;
        period.EndTime = request.EndTime;
        await db.SaveChangesAsync();

        return Ok(new SchoolPeriodDto(period.PeriodNumber, period.StartTime, period.EndTime));
    }

    [HttpDelete("{periodNumber:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeletePeriod(int periodNumber)
    {
        var period = await db.SchoolPeriods.FirstOrDefaultAsync(p => p.PeriodNumber == periodNumber);
        if (period is null)
        {
            return NotFound();
        }

        db.SchoolPeriods.Remove(period);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record UpsertSchoolPeriodRequest(TimeOnly StartTime, TimeOnly EndTime);

public record SchoolPeriodDto(int PeriodNumber, TimeOnly StartTime, TimeOnly EndTime);
