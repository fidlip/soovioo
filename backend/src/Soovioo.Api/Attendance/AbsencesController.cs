using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Attendance;

[Authorize]
[ApiController]
[Route("api/absences")]
public class AbsencesController(
    AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier, TimeProvider clock,
    Messaging.MessageAttachmentStorageService storage) : ControllerBase
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
            .Select(AbsenceDto.Projection)
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

        // The family already excused the day: the teacher's periods go to that record.
        if (await db.Absences.FirstOrDefaultAsync(a => a.StudentId == student.Id && a.Date == request.Date && a.ExcuseId != null)
            is { } reported)
        {
            reported.Periods = periods;
            await db.SaveChangesAsync();
            return Ok(await DtoAsync(reported.Id));
        }

        var absence = new Absence
        {
            StudentId = request.StudentId,
            Date = request.Date,
            Periods = periods,
            SchoolId = currentSchool.SchoolId!.Value,
            CreatedById = User.UserId(),
        };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();
        await NotifyFamilyAsync(absence, student, NotificationType.Absence, NewTitle);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAbsences), new { id = absence.Id }, await DtoAsync(absence.Id));
    }

    /// <summary>A one-day omluvenka for the absence (what the mobile app
    /// still sends).</summary>
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
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
        {
            return this.FieldError("reason", "Vyplňte důvod (nejvýš 500 znaků).");
        }
        if (absence.ExcuseId is not null || absence.Status != AbsenceStatus.Unexcused)
        {
            return BadRequest("Absence už je omluvená.");
        }

        var student = (await db.Students.FindAsync(absence.StudentId))!;
        var excuse = new Excuse
        {
            SchoolId = absence.SchoolId,
            StudentId = absence.StudentId,
            CreatedById = User.UserId(),
            DateFrom = absence.Date,
            DateTo = absence.Date,
            Reason = request.Reason.Trim(),
        };
        var book = new ExcuseBook(db, notifier, clock);
        var (_, days) = await book.ApplyAsync(excuse, student, User.UserId());
        db.Excuses.Add(excuse);
        await db.SaveChangesAsync();
        await book.TellTeachersAsync(excuse, days, edited: false, User.UserId());
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Approves one day of an omluvenka — at the earliest that day —
    /// or rejects it with a reason.</summary>
    [HttpPost("{id}/review")]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<ActionResult> ReviewAbsence(int id, ReviewAbsenceRequest request)
    {
        var absence = await db.Absences.Include(a => a.Excuse).FirstOrDefaultAsync(a => a.Id == id);
        if (absence is null)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, absence.StudentId))
        {
            return Forbid();
        }
        if (ExcusesController.ReviewNoteError(this, request) is { } invalid)
        {
            return invalid;
        }
        if (absence.Excuse is not { } excuse)
        {
            return BadRequest("Absence nemá omluvenku ke schválení.");
        }

        var book = new ExcuseBook(db, notifier, clock);
        var decided = ExcuseBook.Decide([absence], request.Approve, request.Note?.Trim(), book.Today);
        if (decided.Count == 0)
        {
            return BadRequest(absence.Status == AbsenceStatus.Excused
                ? "Absence už je omluvená."
                : "Absenci jde omluvit nejdříve v její den.");
        }
        await book.TellFamilyAsync(
            excuse, decided, ExcuseBook.DecidedTitle(request.Approve), request.Note?.Trim(), User.UserId());
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The decided day waits again.</summary>
    [HttpPost("{id:int}/reopen")]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<ActionResult> ReopenAbsence(int id)
    {
        var absence = await db.Absences.Include(a => a.Excuse).FirstOrDefaultAsync(a => a.Id == id);
        if (absence is null)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, absence.StudentId))
        {
            return Forbid();
        }
        if (absence.Excuse is not { } excuse || ExcuseBook.Reopen([absence]).Count == 0)
        {
            return BadRequest("Absence nemá vyřízenou omluvenku.");
        }
        await new ExcuseBook(db, notifier, clock).TellFamilyAsync(
            excuse, [absence], "Omluvenka vrácena k posouzení", null, User.UserId());
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
            await notifier.ChangedAsync(absence.SchoolId, absence.Id, [NotificationType.Absence], family,
                Drafts(absence, student, NotificationType.Absence, "Změna absence", family), NewTitle);
        }
        await db.SaveChangesAsync();

        return Ok(await DtoAsync(absence.Id));
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
        if (absence.ExcuseId is { } excuseId)
        {
            await new ExcuseBook(db, notifier, clock).DropIfEmptyAsync(excuseId, storage);
        }
        return NoContent();
    }

    private async Task<AbsenceDto> DtoAsync(int id) =>
        await db.Absences.Where(a => a.Id == id).Select(AbsenceDto.Projection).FirstAsync();

    /// <summary>Sorted, distinct; null when a number is outside 0–15.</summary>
    public static List<int>? NormalizePeriods(List<int>? periods)
    {
        var result = (periods ?? []).Distinct().Order().ToList();
        return result.Any(p => p is < 0 or > 15) ? null : result;
    }
}

/// <summary>Periods: missed lesson numbers; null/empty = the whole day.</summary>
public record CreateAbsenceRequest(int StudentId, DateOnly Date, List<int>? Periods = null);
public record SubmitExcuseRequest(string Reason);
/// <summary>Note: why it's rejected — required then.</summary>
public record ReviewAbsenceRequest(bool Approve, string? Note = null);

public record AbsenceDto(
    int Id,
    int StudentId,
    string StudentName,
    DateOnly Date,
    AbsenceStatus Status,
    string? ExcuseReason,
    IReadOnlyList<int> Periods,
    int? ExcuseId = null,
    string? ReviewNote = null)
{
    public static readonly Expression<Func<Absence, AbsenceDto>> Projection = a => new AbsenceDto(
        a.Id,
        a.StudentId,
        a.Student!.Person!.FirstName + " " + a.Student.Person!.LastName,
        a.Date,
        a.Status,
        a.Excuse == null ? null : a.Excuse.Reason,
        a.Periods,
        a.ExcuseId,
        a.ReviewNote);
}
