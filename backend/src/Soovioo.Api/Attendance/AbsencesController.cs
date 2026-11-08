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
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.AbsencesRead);

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
                a.Student!.Person!.FirstName + " " + a.Student.Person!.LastName,
                a.Date,
                a.Status,
                a.ExcuseReason,
                a.Periods))
            .ToListAsync();

        return Ok(absences);
    }

    [HttpPost]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<ActionResult<AbsenceDto>> AddAbsence(CreateAbsenceRequest request)
    {
        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Neznámý žák.");
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, student.Id))
        {
            return Forbid();
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
        await NotifyFamilyAsync(absence, student, NotificationType.Absence, NewTitle);
        await db.SaveChangesAsync();

        var dto = new AbsenceDto(
            absence.Id, absence.StudentId, student.Person!.FullName,
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

        if (!await User.ReachesStudentAsync(db, Permission.AbsencesExcuse, absence.StudentId))
        {
            return Forbid();
        }

        absence.ExcuseReason = request.Reason;
        absence.Status = AbsenceStatus.PendingExcuse;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/review")]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<ActionResult> ReviewAbsence(int id, ReviewAbsenceRequest request)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, absence.StudentId))
        {
            return Forbid();
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

    private const string NewTitle = "Zapsaná absence";

    private async Task NotifyFamilyAsync(Absence absence, StudentProfile student, NotificationType type, string title) =>
        await notifier.AddAsync(absence.SchoolId, Drafts(absence, student, type, title, await notifier.FamilyAsync(student.Id)));

    private static List<NotificationDraft> Drafts(
        Absence absence, StudentProfile student, NotificationType type, string title, IEnumerable<int> family)
    {
        var periods = absence.Periods.Count > 0 ? $", hodiny {string.Join(", ", absence.Periods)}" : "";
        return family
            .Select(userId => new NotificationDraft(
                userId, type, title, $"{student.Person!.FullName} — {SchoolFormat.Date(absence.Date)}{periods}",
                "absences", absence.Id, student.Id))
            .ToList();
    }

    /// <summary>Direct correction of a record (e.g. a teacher logged the
    /// wrong date) — separate from the excuse/review workflow above, which
    /// stays the normal path for actually processing an absence.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permission.AbsencesWrite)]
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
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, absence.StudentId)
            || !await User.ReachesStudentAsync(db, Permission.AbsencesWrite, student.Id))
        {
            return Forbid();
        }

        var periods = NormalizePeriods(request.Periods);
        if (periods is null)
        {
            return this.FieldError("periods", "Neplatné číslo hodiny.");
        }

        var changed = absence.StudentId != request.StudentId || absence.Date != request.Date || !absence.Periods.SequenceEqual(periods);
        absence.StudentId = request.StudentId;
        absence.Date = request.Date;
        absence.Periods = periods;
        if (changed)
        {
            var family = await notifier.FamilyAsync(student.Id);
            await notifier.ChangedAsync(absence.SchoolId, absence.Id, [NotificationType.Absence, NotificationType.Excuse], family,
                Drafts(absence, student, NotificationType.Absence, "Změna absence", family), NewTitle);
        }
        await db.SaveChangesAsync();

        var dto = new AbsenceDto(
            absence.Id, absence.StudentId, student.Person!.FullName,
            absence.Date, absence.Status, absence.ExcuseReason, absence.Periods);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<IActionResult> DeleteAbsence(int id)
    {
        var absence = await db.Absences.FindAsync(id);
        if (absence is null)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, absence.StudentId))
        {
            return Forbid();
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
