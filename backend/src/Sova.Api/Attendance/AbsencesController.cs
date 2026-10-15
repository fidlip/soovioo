using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Attendance;

[Authorize]
[ApiController]
[Route("api/absences")]
public class AbsencesController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AbsenceDto>>> GetAbsences()
    {
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);

        var query = db.Absences.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(a => allowedStudentIds.Contains(a.StudentId));
        }

        var absences = await query
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
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<AbsenceDto>> AddAbsence(CreateAbsenceRequest request)
    {
        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Unknown studentId.");
        }

        var absence = new Absence
        {
            StudentId = request.StudentId,
            Date = request.Date,
            SchoolId = currentSchool.SchoolId!.Value,
        };
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

        if (!await User.CanAccessStudentAsync(db, absence.StudentId))
        {
            return Forbid();
        }

        absence.ExcuseReason = request.Reason;
        absence.Status = AbsenceStatus.PendingExcuse;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/review")]
    [Authorize(Roles = "Teacher,Admin")]
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
