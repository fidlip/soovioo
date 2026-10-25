using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Homework;

[Authorize]
[ApiController]
[Route("api/homework")]
public class HomeworkController(
    AppDbContext db, ICurrentSchoolContext currentSchool, MessageAttachmentStorageService storage,
    Notifier notifier) : ControllerBase
{
    /// <summary>Unscoped, like GET /api/grades — Teacher/Admin see every
    /// homework item in the school (client-side filters by class/subject),
    /// Parent/Student only the ones actually assigned to an accessible
    /// student of theirs.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HomeworkDto>>> GetHomework()
    {
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);

        var query = db.Homework.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(h =>
                (h.Students.Count == 0 &&
                    db.Students.Any(s => s.SchoolClassId == h.SchoolClassId && allowedStudentIds.Contains(s.Id)))
                || h.Students.Any(hs => allowedStudentIds.Contains(hs.StudentId)));
        }

        var homework = await query
            .OrderByDescending(h => h.DueDate)
            .Select(h => new HomeworkDto(
                h.Id,
                h.SchoolClassId,
                h.SchoolClass!.Name,
                h.SubjectId,
                h.Subject!.Name,
                h.Subject!.Abbreviation,
                h.Text,
                h.DueDate,
                h.RequiresCompletion,
                h.CreatedByName,
                h.CreatedAt,
                h.Students.Select(s => s.StudentId).ToList(),
                h.Attachments.Select(a => new HomeworkAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
                h.Completions
                    .Where(c => allowedStudentIds == null || allowedStudentIds.Contains(c.StudentId))
                    .Select(c => c.StudentId)
                    .ToList()))
            .ToListAsync();

        return Ok(homework);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<HomeworkDto>> CreateHomework(CreateHomeworkRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Neznámý předmět.");
        }

        if (!User.IsInRole("Admin") &&
            !await db.TeachesClassInSubjectAsync(CurrentUserId(), request.SchoolClassId, request.SubjectId))
        {
            return Forbid();
        }

        var studentIds = request.StudentIds ?? [];
        if (studentIds.Count > 0)
        {
            var matchingCount = await db.Students
                .CountAsync(s => studentIds.Contains(s.Id) && s.SchoolClassId == request.SchoolClassId);
            if (matchingCount != studentIds.Distinct().Count())
            {
                return BadRequest("Všichni vybraní žáci musí patřit do zadané třídy.");
            }
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var homework = new Homework
        {
            SchoolClassId = request.SchoolClassId,
            SubjectId = request.SubjectId,
            Text = request.Text,
            DueDate = request.DueDate,
            RequiresCompletion = request.RequiresCompletion,
            CreatedByName = User.Identity?.Name ?? "Neznámý uživatel",
            SchoolId = schoolId,
            Students = studentIds.Select(id => new HomeworkStudent { StudentId = id, SchoolId = schoolId }).ToList(),
        };

        db.Homework.Add(homework);
        await db.SaveChangesAsync();

        var targetIds = studentIds.Count > 0
            ? studentIds
            : await db.Students.Where(s => s.SchoolClassId == request.SchoolClassId).Select(s => s.Id).ToListAsync();
        var families = await notifier.FamiliesAsync(targetIds);
        await notifier.AddAsync(schoolId, families
            .SelectMany(f => f.Value.Select(userId => new NotificationDraft(
                userId, NotificationType.Homework, $"Nový domácí úkol — {subject.Name}",
                Notifier.Preview($"{homework.Text} (do {SchoolFormat.Date(homework.DueDate)})"),
                "homework", homework.Id, f.Key)))
            .ToList());
        await db.SaveChangesAsync();

        var dto = new HomeworkDto(
            homework.Id, homework.SchoolClassId, schoolClass.Name, homework.SubjectId, subject.Name, subject.Abbreviation,
            homework.Text, homework.DueDate, homework.RequiresCompletion, homework.CreatedByName, homework.CreatedAt,
            studentIds, [], []);
        return CreatedAtAction(nameof(GetHomework), dto);
    }

    /// <summary>Same ownership rule as creating it, checked for both the
    /// current and the new class+subject. Moving it to another class drops
    /// completions and targets that don't belong there anymore.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<HomeworkDto>> UpdateHomework(int id, CreateHomeworkRequest request)
    {
        var homework = await db.Homework
            .Include(h => h.Students)
            .Include(h => h.Completions)
            .Include(h => h.Attachments)
            .FirstOrDefaultAsync(h => h.Id == id);
        if (homework is null)
        {
            return NotFound();
        }

        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Neznámý předmět.");
        }

        if (!User.IsInRole("Admin") &&
            (!await db.TeachesClassInSubjectAsync(CurrentUserId(), homework.SchoolClassId, homework.SubjectId) ||
             !await db.TeachesClassInSubjectAsync(CurrentUserId(), request.SchoolClassId, request.SubjectId)))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return this.FieldError("text", "Zadání je povinné.");
        }

        var studentIds = (request.StudentIds ?? []).Distinct().ToList();
        if (studentIds.Count > 0)
        {
            var matchingCount = await db.Students
                .CountAsync(s => studentIds.Contains(s.Id) && s.SchoolClassId == request.SchoolClassId);
            if (matchingCount != studentIds.Count)
            {
                return BadRequest("Všichni vybraní žáci musí patřit do zadané třídy.");
            }
        }

        homework.SchoolClassId = request.SchoolClassId;
        homework.SubjectId = request.SubjectId;
        homework.Text = request.Text;
        homework.DueDate = request.DueDate;
        homework.RequiresCompletion = request.RequiresCompletion;

        homework.Students.RemoveAll(hs => !studentIds.Contains(hs.StudentId));
        foreach (var studentId in studentIds.Where(sid => homework.Students.All(hs => hs.StudentId != sid)))
        {
            homework.Students.Add(new HomeworkStudent { StudentId = studentId, SchoolId = homework.SchoolId });
        }

        // Completions only make sense for students the homework still targets.
        var targetIds = studentIds.Count > 0
            ? studentIds
            : await db.Students.Where(s => s.SchoolClassId == request.SchoolClassId).Select(s => s.Id).ToListAsync();
        homework.Completions.RemoveAll(c => !targetIds.Contains(c.StudentId) || !request.RequiresCompletion);

        await db.SaveChangesAsync();

        return Ok(new HomeworkDto(
            homework.Id, homework.SchoolClassId, schoolClass.Name, homework.SubjectId, subject.Name, subject.Abbreviation,
            homework.Text, homework.DueDate, homework.RequiresCompletion, homework.CreatedByName, homework.CreatedAt,
            studentIds,
            homework.Attachments.Select(a => new HomeworkAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            homework.Completions.Select(c => c.StudentId).ToList()));
    }

    /// <summary>A separate step after CreateHomework, same reasoning as
    /// MessagesController.UploadAttachment — multipart/form-data stays out
    /// of the plain-JSON create request.</summary>
    [HttpPost("{id:int}/attachments")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<HomeworkAttachmentDto>> UploadAttachment(int id, IFormFile file)
    {
        var homework = await db.Homework.FindAsync(id);
        if (homework is null)
        {
            return NotFound();
        }

        if (file.Length == 0)
        {
            return BadRequest("Prázdný soubor.");
        }
        if (file.Length > MessageAttachmentRules.MaxSizeBytes)
        {
            return BadRequest("Soubor je příliš velký (max 10 MB).");
        }
        if (!MessageAttachmentRules.IsAllowedFileName(file.FileName))
        {
            return BadRequest("Nepovolený typ souboru.");
        }
        if (await MessageAttachmentRules.DemoLimitErrorAsync(db, currentSchool.SchoolId!.Value, file.Length) is { } demoLimit)
        {
            return BadRequest(demoLimit);
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var storagePath = await storage.SaveAsync(schoolId, file);

        var attachment = new HomeworkAttachment
        {
            HomeworkId = id,
            SchoolId = schoolId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storagePath,
        };
        db.HomeworkAttachments.Add(attachment);
        await db.SaveChangesAsync();

        return Ok(new HomeworkAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
    }

    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int id, int attachmentId)
    {
        if (!await User.CanAccessHomeworkAsync(db, id))
        {
            return Forbid();
        }

        var attachment = await db.HomeworkAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.HomeworkId == id);
        if (attachment is null)
        {
            return NotFound();
        }

        var fullPath = storage.GetFullPath(attachment.StoragePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, attachment.ContentType, attachment.FileName);
    }

    /// <summary>"žák/rodič" (roadmap wording) — unlike AbsencesController.
    /// SubmitExcuse, Student is deliberately NOT excluded here; marking
    /// your own homework done has no equivalent restriction.</summary>
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> MarkComplete(int id, MarkHomeworkCompleteRequest request)
    {
        if (!await User.CanAccessStudentAsync(db, request.StudentId))
        {
            return Forbid();
        }

        var homework = await db.Homework
            .Where(h => h.Id == id)
            .Select(h => new { h.SchoolClassId, h.RequiresCompletion, StudentIds = h.Students.Select(s => s.StudentId).ToList() })
            .FirstOrDefaultAsync();
        if (homework is null)
        {
            return NotFound();
        }
        if (!homework.RequiresCompletion)
        {
            return BadRequest("Tento úkol nevyžaduje potvrzení splnění.");
        }

        var isTargeted = homework.StudentIds.Count > 0
            ? homework.StudentIds.Contains(request.StudentId)
            : await db.Students.AnyAsync(s => s.Id == request.StudentId && s.SchoolClassId == homework.SchoolClassId);
        if (!isTargeted)
        {
            return BadRequest("Žák nemá tento úkol zadaný.");
        }

        if (await db.HomeworkCompletions.FindAsync(id, request.StudentId) is null)
        {
            db.HomeworkCompletions.Add(new HomeworkCompletion
            {
                HomeworkId = id,
                StudentId = request.StudentId,
                SchoolId = currentSchool.SchoolId!.Value,
            });
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>Same ownership rule as creating it — a Teacher may only
    /// delete homework for a class+subject they actually teach; Admin can
    /// delete anything.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteHomework(int id)
    {
        var homework = await db.Homework.FindAsync(id);
        if (homework is null)
        {
            return NotFound();
        }

        if (!User.IsInRole("Admin") &&
            !await db.TeachesClassInSubjectAsync(CurrentUserId(), homework.SchoolClassId, homework.SubjectId))
        {
            return Forbid();
        }

        var attachments = await db.HomeworkAttachments.Where(a => a.HomeworkId == id).ToListAsync();
        db.Homework.Remove(homework); // cascades to HomeworkStudent/HomeworkAttachment/HomeworkCompletion rows
        await db.SaveChangesAsync();

        foreach (var attachment in attachments)
        {
            storage.Delete(attachment.StoragePath);
        }

        return NoContent();
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record CreateHomeworkRequest(
    int SchoolClassId,
    int SubjectId,
    string Text,
    DateOnly DueDate,
    bool RequiresCompletion,
    List<int>? StudentIds = null);

public record MarkHomeworkCompleteRequest(int StudentId);

public record HomeworkAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes);

public record HomeworkDto(
    int Id,
    int SchoolClassId,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    string Text,
    DateOnly DueDate,
    bool RequiresCompletion,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    /// <summary>Empty means "whole class" — see Homework.Students.</summary>
    IReadOnlyList<int> TargetStudentIds,
    IReadOnlyList<HomeworkAttachmentDto> Attachments,
    /// <summary>Which of the (caller-visible) target students have marked
    /// it done — Teacher/Admin see everyone's, Parent/Student only their
    /// own linked student(s).</summary>
    IReadOnlyList<int> CompletedStudentIds);
