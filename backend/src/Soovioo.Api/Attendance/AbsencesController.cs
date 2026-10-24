using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Attendance;

[Authorize]
[ApiController]
[Route("api/absences")]
public class AbsencesController(AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier) : ControllerBase
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
                a.ExcuseReason,
                a.Periods))
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
            return BadRequest("Neznámý žák.");
        }

        var periods = NormalizePeriods(request.Periods);
        if (periods is null)
        {
            return this.FieldError("periods", "Neplatné číslo hodiny.");
        }

        var absence = new Absence
        {
            StudentId = request.StudentId,
            Date = request.Date,
            Periods = periods,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();
        await NotifyFamilyAsync(absence, student, NotificationType.Absence, "Zapsaná absence");
        await db.SaveChangesAsync();

        var dto = new AbsenceDto(
            absence.Id, absence.StudentId, $"{student.FirstName} {student.LastName}",
            absence.Date, absence.Status, absence.ExcuseReason, absence.Periods);
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

        // A Student can read their own absences (CanAccessStudentAsync would
        // allow it) but, per product decision, may never submit their own
        // excuse — that stays a Parent/Teacher/Admin action.
        if (User.IsInRole(nameof(UserRole.Student)) || !await User.CanAccessStudentAsync(db, absence.StudentId))
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
        if (await db.Students.FindAsync(absence.StudentId) is { } student)
        {
            await NotifyFamilyAsync(absence, student, NotificationType.Excuse,
                request.Approve ? "Omluvenka schválena" : "Omluvenka zamítnuta");
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task NotifyFamilyAsync(Absence absence, Student student, NotificationType type, string title)
    {
        var periods = absence.Periods.Count > 0 ? $", hodiny {string.Join(", ", absence.Periods)}" : "";
        await notifier.NotifyAsync(
            absence.SchoolId, await notifier.FamilyAsync(student.Id), type, title,
            $"{student.FirstName} {student.LastName} — {Notifier.Format(absence.Date)}{periods}",
            "absences", absence.Id, student.Id);
    }

    /// <summary>Direct correction of a record (e.g. a teacher logged the
    /// wrong date) — separate from the excuse/review workflow above, which
    /// stays the normal path for actually processing an absence.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<AbsenceDto>> UpdateAbsence(int id, CreateAbsenceRequest request)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }

        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Neznámý žák.");
        }

        var periods = NormalizePeriods(request.Periods);
        if (periods is null)
        {
            return this.FieldError("periods", "Neplatné číslo hodiny.");
        }

        absence.StudentId = request.StudentId;
        absence.Date = request.Date;
        absence.Periods = periods;
        await db.SaveChangesAsync();

        var dto = new AbsenceDto(
            absence.Id, absence.StudentId, $"{student.FirstName} {student.LastName}",
            absence.Date, absence.Status, absence.ExcuseReason, absence.Periods);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteAbsence(int id)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }

        db.Absences.Remove(absence);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sorted, distinct; null when a number is outside 0–15.</summary>
    private static List<int>? NormalizePeriods(List<int>? periods)
    {
        var result = (periods ?? []).Distinct().Order().ToList();
        return result.Any(p => p is < 0 or > 15) ? null : result;
    }
}

/// <summary>Periods: missed lesson numbers; null/empty = the whole day.</summary>
public record CreateAbsenceRequest(int StudentId, DateOnly Date, List<int>? Periods = null);
public record SubmitExcuseRequest(string Reason);
public record ReviewAbsenceRequest(bool Approve);

public record AbsenceDto(
    int Id,
    int StudentId,
    string StudentName,
    DateOnly Date,
    AbsenceStatus Status,
    string? ExcuseReason,
    IReadOnlyList<int> Periods);
