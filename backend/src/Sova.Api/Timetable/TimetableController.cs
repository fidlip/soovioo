using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Timetable;

[Authorize]
[ApiController]
[Route("api/school-classes/{schoolClassId}/timetable")]
public class TimetableController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimetableEntryDto>>> GetTimetable(int schoolClassId)
    {
        var entries = await db.TimetableEntries
            .Where(e => e.SchoolClassId == schoolClassId)
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.PeriodNumber)
            .Select(e => new TimetableEntryDto(
                e.Id,
                e.SchoolClassId,
                e.SubjectId,
                e.Subject!.Name,
                e.DayOfWeek,
                e.PeriodNumber,
                e.WeekParity))
            .ToListAsync();

        return Ok(entries);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<TimetableEntryDto>> AddEntry(int schoolClassId, CreateTimetableEntryRequest request)
    {
        // Both lookups are already scoped to the caller's school by the
        // global query filter, so a cross-tenant id just 404s/400s here.
        var schoolClass = await db.SchoolClasses.FindAsync(schoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Unknown schoolClassId.");
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Unknown subjectId.");
        }

        var entry = new TimetableEntry
        {
            SchoolClassId = schoolClassId,
            SubjectId = request.SubjectId,
            DayOfWeek = request.DayOfWeek,
            PeriodNumber = request.PeriodNumber,
            WeekParity = request.WeekParity,
            SchoolId = currentSchool.SchoolId!.Value,
        };

        db.TimetableEntries.Add(entry);
        await db.SaveChangesAsync();

        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity);
        return CreatedAtAction(nameof(GetTimetable), new { schoolClassId }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<TimetableEntryDto>> UpdateEntry(
        int schoolClassId, int id, CreateTimetableEntryRequest request)
    {
        var entry = await db.TimetableEntries.FirstOrDefaultAsync(e => e.Id == id && e.SchoolClassId == schoolClassId);
        if (entry is null)
        {
            return NotFound();
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Unknown subjectId.");
        }

        entry.SubjectId = request.SubjectId;
        entry.DayOfWeek = request.DayOfWeek;
        entry.PeriodNumber = request.PeriodNumber;
        entry.WeekParity = request.WeekParity;
        await db.SaveChangesAsync();

        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteEntry(int schoolClassId, int id)
    {
        var entry = await db.TimetableEntries.FirstOrDefaultAsync(e => e.Id == id && e.SchoolClassId == schoolClassId);
        if (entry is null)
        {
            return NotFound();
        }

        db.TimetableEntries.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateTimetableEntryRequest(
    int SubjectId,
    DayOfWeek DayOfWeek,
    int PeriodNumber,
    WeekParity WeekParity);

public record TimetableEntryDto(
    int Id,
    int SchoolClassId,
    int SubjectId,
    string SubjectName,
    DayOfWeek DayOfWeek,
    int PeriodNumber,
    WeekParity WeekParity);
