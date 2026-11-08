using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Attendance;

/// <summary>Omluvenky. The family writes them and may change or delete them
/// while days of them wait; a class teacher may write one too (for a family
/// that called) — its days up to today are approved at once.</summary>
[Authorize]
[ApiController]
[Route("api/excuses")]
public class ExcusesController(
    AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier, TimeProvider clock,
    MessageAttachmentStorageService storage) : ControllerBase
{
    private ExcuseBook Book => new(db, notifier, clock);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExcuseDto>>> GetExcuses()
    {
        var query = db.Excuses.AsQueryable();
        if (await User.StudentIdsAsync(db, Permission.AbsencesRead) is { } ids)
        {
            query = query.Where(e => ids.Contains(e.StudentId));
        }
        return Ok(await DtosAsync(query));
    }

    [HttpPost]
    public async Task<ActionResult<ExcuseResultDto>> CreateExcuse(ExcuseRequest request)
    {
        if (await db.Students.FindAsync(request.StudentId) is not { } student)
        {
            return BadRequest("Neznámý žák.");
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesExcuse, student.Id))
        {
            return Forbid();
        }
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var excuse = new Excuse
        {
            SchoolId = currentSchool.SchoolId!.Value,
            StudentId = student.Id,
            CreatedById = User.UserId(),
        };
        Set(excuse, request);
        var book = Book;
        var (skipped, days) = await book.ApplyAsync(excuse, student, User.UserId());
        if (days.Count == 0)
        {
            return this.FieldError("dateFrom", skipped.Count == 0
                ? "V tomto období není žádný školní den."
                : "Žádný z dnů nejde omluvit — všechny už jsou vyřízené nebo omluvené.");
        }
        db.Excuses.Add(excuse);
        await db.SaveChangesAsync();
        if (await IsFamilyAsync())
        {
            await book.TellTeachersAsync(excuse, days, edited: false, User.UserId());
        }
        else
        {
            var decided = ExcuseBook.Decide(
                await db.Absences.Where(a => a.ExcuseId == excuse.Id).ToListAsync(), true, null, book.Today);
            await book.TellFamilyAsync(excuse, decided, true, null, User.UserId());
        }
        await db.SaveChangesAsync();
        return Ok(new ExcuseResultDto(excuse.Id, skipped));
    }

    /// <summary>Changes the days still waiting; the decided ones stay.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ExcuseResultDto>> UpdateExcuse(int id, ExcuseRequest request)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse)
        {
            return NotFound();
        }
        if (await MayChangeAsync(excuse) is { } denied)
        {
            return denied;
        }
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var student = (await db.Students.FindAsync(excuse.StudentId))!;
        Set(excuse, request);
        var book = Book;
        var (skipped, days) = await book.ApplyAsync(excuse, student, User.UserId());
        await db.SaveChangesAsync();
        if (await book.DropIfEmptyAsync(excuse.Id, storage))
        {
            return Ok(new ExcuseResultDto(null, skipped));
        }
        await book.TellTeachersAsync(excuse, days, edited: true, User.UserId());
        await db.SaveChangesAsync();
        return Ok(new ExcuseResultDto(excuse.Id, skipped));
    }

    /// <summary>Takes back the days still waiting; with no day left the
    /// omluvenka goes.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteExcuse(int id)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse)
        {
            return NotFound();
        }
        if (await MayChangeAsync(excuse) is { } denied)
        {
            return denied;
        }

        var book = Book;
        var family = await book.FamilyAsync(excuse.StudentId);
        foreach (var absence in await db.Absences
                     .Where(a => a.ExcuseId == excuse.Id && a.Status == AbsenceStatus.PendingExcuse).ToListAsync())
        {
            book.Withdraw(absence, family);
        }
        await db.SaveChangesAsync();
        await book.DropIfEmptyAsync(excuse.Id, storage);
        return NoContent();
    }

    /// <summary>Approves the waiting days up to today, or rejects all waiting
    /// days — with a reason the family sees.</summary>
    [HttpPost("{id:int}/review")]
    [RequirePermission(Permission.AbsencesWrite)]
    public async Task<ActionResult> ReviewExcuse(int id, ReviewAbsenceRequest request)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesWrite, excuse.StudentId))
        {
            return Forbid();
        }
        if (ReviewNoteError(this, request) is { } invalid)
        {
            return invalid;
        }

        var book = Book;
        var days = await db.Absences.Where(a => a.ExcuseId == excuse.Id).ToListAsync();
        var decided = ExcuseBook.Decide(days, request.Approve, request.Note?.Trim(), book.Today);
        if (decided.Count == 0)
        {
            return BadRequest(request.Approve && days.Any(a => a.Status == AbsenceStatus.PendingExcuse)
                ? "Omluvenku jde schválit nejdříve v den absence."
                : "Omluvenka nemá nic ke schválení.");
        }
        await book.TellFamilyAsync(excuse, decided, request.Approve, request.Note?.Trim(), User.UserId());
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/attachments")]
    public async Task<ActionResult<ExcuseAttachmentDto>> UploadAttachment(int id, IFormFile file)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse)
        {
            return NotFound();
        }
        if (await MayAttachAsync(excuse) is { } denied)
        {
            return denied;
        }
        if (file.Length == 0)
        {
            return BadRequest("Prázdný soubor.");
        }
        if (file.Length > MessageAttachmentRules.MaxSizeBytes)
        {
            return BadRequest("Soubor je příliš velký (max 10 MB).");
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
        {
            return BadRequest("Přiložit jde PDF nebo obrázek.");
        }
        if (await MessageAttachmentRules.DemoLimitErrorAsync(db, excuse.SchoolId, file.Length) is { } demoLimit)
        {
            return BadRequest(demoLimit);
        }

        var attachment = new ExcuseAttachment
        {
            ExcuseId = excuse.Id,
            SchoolId = excuse.SchoolId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = await storage.SaveAsync(excuse.SchoolId, file),
        };
        db.ExcuseAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return Ok(new ExcuseAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DeleteAttachment(int id, int attachmentId)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse
            || await db.ExcuseAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.ExcuseId == id) is not { } attachment)
        {
            return NotFound();
        }
        if (await MayAttachAsync(excuse) is { } denied)
        {
            return denied;
        }
        db.ExcuseAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        storage.Delete(attachment.StoragePath);
        return NoContent();
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int id, int attachmentId)
    {
        if (await db.Excuses.FindAsync(id) is not { } excuse
            || await db.ExcuseAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.ExcuseId == id) is not { } attachment)
        {
            return NotFound();
        }
        if (!await User.ReachesStudentAsync(db, Permission.AbsencesRead, excuse.StudentId))
        {
            return Forbid();
        }
        var fullPath = storage.GetFullPath(attachment.StoragePath);
        return System.IO.File.Exists(fullPath)
            ? PhysicalFile(fullPath, attachment.ContentType, attachment.FileName)
            : NotFound();
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif",
    };

    /// <summary>A rejection needs a reason.</summary>
    public static ActionResult? ReviewNoteError(ControllerBase controller, ReviewAbsenceRequest request)
    {
        if (!request.Approve && string.IsNullOrWhiteSpace(request.Note))
        {
            return controller.FieldError("note", "Napište, proč omluvenku zamítáte.");
        }
        return request.Note?.Trim().Length > 500
            ? controller.FieldError("note", "Zdůvodnění může mít nejvýš 500 znaků.")
            : null;
    }

    private async Task<bool> IsFamilyAsync() =>
        await User.ScopeAsync(db, Permission.AbsencesExcuse) == PermissionScope.Own;

    /// <summary>The family changes or deletes an omluvenka while days of it wait.</summary>
    private async Task<ActionResult?> MayChangeAsync(Excuse excuse)
    {
        if (!await IsFamilyAsync() || !await User.ReachesStudentAsync(db, Permission.AbsencesExcuse, excuse.StudentId))
        {
            return Forbid();
        }
        return await db.Absences.AnyAsync(a => a.ExcuseId == excuse.Id && a.Status == AbsenceStatus.PendingExcuse)
            ? null
            : BadRequest("Omluvenka je vyřízená, už ji nejde změnit.");
    }

    /// <summary>Files go with an omluvenka the user may change — or one they wrote.</summary>
    private async Task<ActionResult?> MayAttachAsync(Excuse excuse) =>
        excuse.CreatedById == User.UserId() && await User.ReachesStudentAsync(db, Permission.AbsencesExcuse, excuse.StudentId)
            ? null
            : await MayChangeAsync(excuse);

    private ActionResult? Validate(ExcuseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return this.FieldError("reason", "Vyplňte důvod.");
        }
        if (request.Reason.Trim().Length > 500)
        {
            return this.FieldError("reason", "Důvod může mít nejvýš 500 znaků.");
        }
        if (request.DateTo < request.DateFrom)
        {
            return this.FieldError("dateTo", "Konec je před začátkem.");
        }
        if (request.DateTo.DayNumber - request.DateFrom.DayNumber > ExcuseBook.MaxDays)
        {
            return this.FieldError("dateTo", "Omluvenka může být nejvýš na tři měsíce.");
        }
        if (AbsencesController.NormalizePeriods(request.Periods) is null)
        {
            return this.FieldError("periods", "Neplatné číslo hodiny.");
        }
        return null;
    }

    private static void Set(Excuse excuse, ExcuseRequest request)
    {
        excuse.DateFrom = request.DateFrom;
        excuse.DateTo = request.DateTo;
        excuse.Reason = request.Reason.Trim();
        excuse.Periods = request.DateFrom == request.DateTo ? AbsencesController.NormalizePeriods(request.Periods)! : [];
    }

    public static async Task<List<ExcuseDto>> DtosAsync(IQueryable<Excuse> query)
    {
        var rows = await query
            .OrderByDescending(e => e.DateTo).ThenByDescending(e => e.Id)
            .Select(e => new
            {
                e.Id, e.StudentId, e.Student!.Person!.FirstName, e.Student.Person.LastName, e.DateFrom, e.DateTo, e.Periods,
                e.Reason, e.CreatedAt, e.CreatedById,
                Author = e.CreatedBy == null ? null : e.CreatedBy.Person,
                AuthorUsername = e.CreatedBy == null ? null : e.CreatedBy.Username,
                Days = e.Absences.OrderBy(a => a.Date)
                    .Select(a => new ExcuseDayDto(a.Id, a.Date, a.Status, a.ReviewNote, a.Periods)).ToList(),
                Attachments = e.Attachments.OrderBy(a => a.Id)
                    .Select(a => new ExcuseAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            })
            .ToListAsync();
        return [.. rows.Select(r => new ExcuseDto(
            r.Id, r.StudentId, $"{r.FirstName} {r.LastName}", r.DateFrom, r.DateTo, r.Periods, r.Reason,
            ExcuseBook.StatusOf([.. r.Days.Select(d => d.Status)]), r.Days, r.Attachments,
            r.AuthorUsername is null ? null : PersonNames.Display(r.Author, r.AuthorUsername), r.CreatedById, r.CreatedAt))];
    }
}

/// <summary>Periods: of a one-day omluvenka; null/empty = the whole day.</summary>
public record ExcuseRequest(int StudentId, DateOnly DateFrom, DateOnly DateTo, string Reason, List<int>? Periods = null);

/// <summary>ExcuseId: null when no day of it is left.</summary>
public record ExcuseResultDto(int? ExcuseId, IReadOnlyList<SkippedDayDto> Skipped);

/// <summary>A day left out of an omluvenka and the state of what's already there.</summary>
public record SkippedDayDto(DateOnly Date, AbsenceStatus Status);

public record ExcuseDayDto(int AbsenceId, DateOnly Date, AbsenceStatus Status, string? ReviewNote, IReadOnlyList<int> Periods);

public record ExcuseAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes);

public record ExcuseDto(
    int Id,
    int StudentId,
    string StudentName,
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<int> Periods,
    string Reason,
    ExcuseStatus Status,
    IReadOnlyList<ExcuseDayDto> Days,
    IReadOnlyList<ExcuseAttachmentDto> Attachments,
    string? CreatedByName,
    int? CreatedById,
    DateTimeOffset CreatedAt);
