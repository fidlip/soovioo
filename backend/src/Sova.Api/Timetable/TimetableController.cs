using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Timetable;

[ApiController]
[Route("api/school-classes/{schoolClassId}/timetable")]
public class TimetableController(AppDbContext db) : ControllerBase
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
    public async Task<ActionResult<TimetableEntryDto>> AddEntry(int schoolClassId, CreateTimetableEntryRequest request)
    {
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
        };

        db.TimetableEntries.Add(entry);
        await db.SaveChangesAsync();

        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity);
        return CreatedAtAction(nameof(GetTimetable), new { schoolClassId }, dto);
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
