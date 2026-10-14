using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Attendance;

[ApiController]
[Route("api/absences")]
public class AbsencesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AbsenceDto>>> GetAbsences()
    {
        var absences = await db.Absences
            .OrderByDescending(a => a.Date)
            .Select(a => new AbsenceDto(
                a.Id,
                a.StudentId,
                a.Student!.FirstName + " " + a.Student.LastName,
                a.Date,
                a.Status,
                a.ExcuseReason))
            .ToListAsync();

        return Ok(absences);
    }

    [HttpPost]
    public async Task<ActionResult<AbsenceDto>> AddAbsence(CreateAbsenceRequest request)
    {
        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Unknown studentId.");
        }

        var absence = new Absence { StudentId = request.StudentId, Date = request.Date };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var dto = new AbsenceDto(
            absence.Id, absence.StudentId, $"{student.FirstName} {student.LastName}",
            absence.Date, absence.Status, absence.ExcuseReason);
        return CreatedAtAction(nameof(GetAbsences), new { id = absence.Id }, dto);
    }

    [HttpPost("{id}/excuse")]
    public async Task<ActionResult> SubmitExcuse(int id, SubmitExcuseRequest request)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }

        absence.ExcuseReason = request.Reason;
        absence.Status = AbsenceStatus.PendingExcuse;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/review")]
    public async Task<ActionResult> ReviewAbsence(int id, ReviewAbsenceRequest request)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }

        absence.Status = request.Approve ? AbsenceStatus.Excused : AbsenceStatus.Rejected;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateAbsenceRequest(int StudentId, DateOnly Date);
public record SubmitExcuseRequest(string Reason);
public record ReviewAbsenceRequest(bool Approve);

public record AbsenceDto(
    int Id,
    int StudentId,
    string StudentName,
    DateOnly Date,
    AbsenceStatus Status,
    string? ExcuseReason);
