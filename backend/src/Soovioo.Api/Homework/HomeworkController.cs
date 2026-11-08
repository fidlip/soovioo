using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Homework;

[Authorize]
[ApiController]
[Route("api/homework")]
public class HomeworkController(
    AppDbContext db, ICurrentSchoolContext currentSchool, MessageAttachmentStorageService storage,
    Notifier notifier) : ControllerBase
{
    /// <summary>Staff see their classes' and groups' homework (client-side
    /// filters by class/subject), Parent/Student only the ones actually
    /// assigned to an accessible student of theirs.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HomeworkDto>>> GetHomework()
    {
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.HomeworkRead);

        var rows = await (await User.VisibleHomeworkAsync(db))
            .OrderByDescending(h => h.DueDate)
            .Select(h => new
            {
                h.Id,
                h.SchoolClassId,
                ClassName = h.SchoolClass == null ? null : h.SchoolClass.Name,
                h.StudentGroupId,
                GroupName = h.StudentGroup == null ? null : h.StudentGroup.Name,
                GroupClasses = h.StudentGroup == null
                    ? null
                    : h.StudentGroup.Classes.OrderBy(c => c.SchoolClass!.Name).Select(c => new { c.SchoolClassId, c.SchoolClass!.Name }).ToList(),
                h.SubjectId,
                SubjectName = h.Subject!.Name,
                h.Subject!.Abbreviation,
                h.Text,
                h.DueDate,
                h.RequiresCompletion,
                h.CreatedByName,
                h.CreatedAt,
                StudentIds = h.Students.Select(s => s.StudentId).ToList(),
                GroupStudentIds = h.StudentGroup == null
                    ? null
                    : h.StudentGroup.Members
                        .Where(m => allowedStudentIds == null || allowedStudentIds.Contains(m.StudentId))
                        .Select(m => m.StudentId)
                        .ToList(),
                Attachments = h.Attachments.Select(a => new HomeworkAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
                Completed = h.Completions
                    .Where(c => allowedStudentIds == null || allowedStudentIds.Contains(c.StudentId))
                    .Select(c => c.StudentId)
                    .ToList(),
            })
            .ToListAsync();

        return Ok(rows.Select(h => new HomeworkDto(
            h.Id,
            h.SchoolClassId,
            h.GroupClasses is { } classes ? GroupLabel(h.GroupName!, classes.Select(c => c.Name)) : h.ClassName!,
            h.SubjectId,
            h.SubjectName,
            h.Abbreviation,
            h.Text,
            h.DueDate,
            h.RequiresCompletion,
            h.CreatedByName,
            h.CreatedAt,
            h.StudentIds,
            h.Attachments,
            h.Completed,
            h.StudentGroupId,
            h.GroupClasses?.Select(c => c.SchoolClassId).ToList() ?? [h.SchoolClassId!.Value],
            h.GroupStudentIds)));
    }

    /// <summary>"Nj (7.A, 7.B)" — as the teacher's timetable names a group's lesson.</summary>
    private static string GroupLabel(string group, IEnumerable<string> classes) => $"{group} ({string.Join(", ", classes)})";

    /// <summary>The class (and its listed pupils) or the group (and its
    /// listed members) the request is for; an error otherwise.</summary>
    private async Task<(Target? Target, string? Error)> TargetAsync(CreateHomeworkRequest request)
    {
        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return (null, "Neznámý předmět.");
        }
        if ((await db.CoveredSubjectIdsAsync()).Contains(subject.Id))
        {
            return (null, "Předmět se učí ve skupinách — úkol zadejte z předmětu skupiny.");
        }
        var studentIds = (request.StudentIds ?? []).Distinct().ToList();
        if (request.StudentGroupId is { } groupId)
        {
            var group = await db.StudentGroups.Include(g => g.Classes).ThenInclude(c => c.SchoolClass)
                .FirstOrDefaultAsync(g => g.Id == groupId);
            if (group is null)
            {
                return (null, "Neznámá skupina.");
            }
            if (group.SubjectId != subject.Id)
            {
                return (null, "Skupina má jiný předmět.");
            }
            if (studentIds.Count > 0
                && await db.StudentGroupMembers.CountAsync(m => m.StudentGroupId == groupId && studentIds.Contains(m.StudentId)) != studentIds.Count)
            {
                return (null, "Všichni vybraní žáci musí patřit do skupiny.");
            }
            var label = GroupLabel(group.Name, group.Classes.Select(c => c.SchoolClass!.Name).Order());
            return (new Target(null, group.Id, label, [.. group.Classes.Select(c => c.SchoolClassId)], subject, studentIds), null);
        }

        var schoolClass = request.SchoolClassId is { } classId ? await db.SchoolClasses.FindAsync(classId) : null;
        if (schoolClass is null)
        {
            return (null, "Neznámá třída.");
        }
        if (studentIds.Count > 0
            && await db.Students.CountAsync(s => studentIds.Contains(s.Id) && s.SchoolClassId == schoolClass.Id) != studentIds.Count)
        {
            return (null, "Všichni vybraní žáci musí patřit do zadané třídy.");
        }
        return (new Target(schoolClass.Id, null, schoolClass.Name, [schoolClass.Id], subject, studentIds), null);
    }

    private sealed record Target(
        int? SchoolClassId, int? StudentGroupId, string Label, List<int> ClassIds, Subject Subject, List<int> StudentIds);

    [HttpPost]
    [RequirePermission(Permission.HomeworkWrite)]
    public async Task<ActionResult<HomeworkDto>> CreateHomework(CreateHomeworkRequest request)
    {
        var (target, error) = await TargetAsync(request);
        if (target is null)
        {
            return BadRequest(error);
        }
        if (!await User.MayWriteHomeworkAsync(db, target.SchoolClassId, target.StudentGroupId, target.Subject.Id))
        {
            return Forbid();
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var homework = new Homework
        {
            SchoolClassId = target.SchoolClassId,
            StudentGroupId = target.StudentGroupId,
            SubjectId = target.Subject.Id,
            Text = request.Text,
            DueDate = request.DueDate,
            RequiresCompletion = request.RequiresCompletion,
            CreatedByName = User.Identity?.Name ?? "Neznámý uživatel",
            SchoolId = schoolId,
            Students = target.StudentIds.Select(id => new HomeworkStudent { StudentId = id, SchoolId = schoolId }).ToList(),
        };

        db.Homework.Add(homework);
        await db.SaveChangesAsync();

        var targetIds = await db.TargetStudentIdsAsync(target.SchoolClassId, target.StudentGroupId, target.StudentIds);
        await notifier.AddAsync(schoolId, Drafts(homework, await notifier.FamiliesAsync(targetIds), NewTitle(target.Subject)));
        await db.SaveChangesAsync();

        var dto = new HomeworkDto(
            homework.Id, homework.SchoolClassId, target.Label, homework.SubjectId, target.Subject.Name, target.Subject.Abbreviation,
            homework.Text, homework.DueDate, homework.RequiresCompletion, homework.CreatedByName, homework.CreatedAt,
            target.StudentIds, [], [], homework.StudentGroupId, target.ClassIds);
        return CreatedAtAction(nameof(GetHomework), dto);
    }

    /// <summary>Same ownership rule as creating it, checked for both the
    /// current and the new class / group. Moving it elsewhere drops
    /// completions and targets that don't belong there anymore.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permission.HomeworkWrite)]
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

        var (target, error) = await TargetAsync(request);
        if (target is null)
        {
            return BadRequest(error);
        }

        if (!await User.MayWriteHomeworkAsync(db, homework.SchoolClassId, homework.StudentGroupId, homework.SubjectId)
            || !await User.MayWriteHomeworkAsync(db, target.SchoolClassId, target.StudentGroupId, target.Subject.Id))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return this.FieldError("text", "Zadání je povinné.");
        }

        var studentIds = target.StudentIds;
        var before = (homework.SchoolClassId, homework.StudentGroupId, homework.SubjectId, homework.Text, homework.DueDate,
            homework.RequiresCompletion, string.Join(",", homework.Students.Select(hs => hs.StudentId).Order()));
        homework.SchoolClassId = target.SchoolClassId;
        homework.StudentGroupId = target.StudentGroupId;
        homework.SubjectId = target.Subject.Id;
        homework.Text = request.Text;
        homework.DueDate = request.DueDate;
        homework.RequiresCompletion = request.RequiresCompletion;

        homework.Students.RemoveAll(hs => !studentIds.Contains(hs.StudentId));
        foreach (var studentId in studentIds.Where(sid => homework.Students.All(hs => hs.StudentId != sid)))
        {
            homework.Students.Add(new HomeworkStudent { StudentId = studentId, SchoolId = homework.SchoolId });
        }

        // Completions only make sense for students the homework still targets.
        var targetIds = await db.TargetStudentIdsAsync(target.SchoolClassId, target.StudentGroupId, studentIds);
        homework.Completions.RemoveAll(c => !targetIds.Contains(c.StudentId) || !request.RequiresCompletion);

        if (before != (homework.SchoolClassId, homework.StudentGroupId, homework.SubjectId, homework.Text, homework.DueDate,
                homework.RequiresCompletion, string.Join(",", studentIds.Order())))
        {
            var families = await notifier.FamiliesAsync(targetIds);
            await notifier.ChangedAsync(homework.SchoolId, homework.Id, [NotificationType.Homework],
                families.Values.SelectMany(f => f).ToList(),
                Drafts(homework, families, $"Změna úkolu — {target.Subject.Name}"), NewTitle(target.Subject));
        }
        await db.SaveChangesAsync();

        return Ok(new HomeworkDto(
            homework.Id, homework.SchoolClassId, target.Label, homework.SubjectId, target.Subject.Name, target.Subject.Abbreviation,
            homework.Text, homework.DueDate, homework.RequiresCompletion, homework.CreatedByName, homework.CreatedAt,
            studentIds,
            homework.Attachments.Select(a => new HomeworkAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            homework.Completions.Select(c => c.StudentId).ToList(),
            homework.StudentGroupId, target.ClassIds));
    }

    private static string NewTitle(Subject subject) => $"Nový domácí úkol — {subject.Name}";

    private static List<NotificationDraft> Drafts(Homework homework, Dictionary<int, List<int>> families, string title) =>
        families
            .SelectMany(f => f.Value.Select(userId => new NotificationDraft(
                userId, NotificationType.Homework, title,
                Notifier.Preview($"{homework.Text} (do {SchoolFormat.Date(homework.DueDate)})"),
                "homework", homework.Id, f.Key)))
            .ToList();

    /// <summary>A separate step after CreateHomework, same reasoning as
    /// MessagesController.UploadAttachment — multipart/form-data stays out
    /// of the plain-JSON create request.</summary>
    [HttpPost("{id:int}/attachments")]
    [RequirePermission(Permission.HomeworkWrite)]
    public async Task<ActionResult<HomeworkAttachmentDto>> UploadAttachment(int id, IFormFile file)
    {
        var homework = await db.Homework.FindAsync(id);
        if (homework is null)
        {
            return NotFound();
        }
        if (!await User.MayWriteHomeworkAsync(db, homework.SchoolClassId, homework.StudentGroupId, homework.SubjectId))
        {
            return Forbid();
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

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> MarkComplete(int id, MarkHomeworkCompleteRequest request)
    {
        if (!await User.ReachesStudentAsync(db, Permission.HomeworkComplete, request.StudentId))
        {
            return Forbid();
        }

        var homework = await db.Homework
            .Where(h => h.Id == id)
            .Select(h => new { h.SchoolClassId, h.StudentGroupId, h.RequiresCompletion, StudentIds = h.Students.Select(s => s.StudentId).ToList() })
            .FirstOrDefaultAsync();
        if (homework is null)
        {
            return NotFound();
        }
        if (!homework.RequiresCompletion)
        {
            return BadRequest("Tento úkol nevyžaduje potvrzení splnění.");
        }

        if (!(await db.TargetStudentIdsAsync(homework.SchoolClassId, homework.StudentGroupId, homework.StudentIds)).Contains(request.StudentId))
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

    /// <summary>Same ownership rule as creating it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.HomeworkWrite)]
    public async Task<IActionResult> DeleteHomework(int id)
    {
        var homework = await db.Homework.FindAsync(id);
        if (homework is null)
        {
            return NotFound();
        }

        if (!await User.MayWriteHomeworkAsync(db, homework.SchoolClassId, homework.StudentGroupId, homework.SubjectId))
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
}

/// <param name="StudentGroupId">Homework of a group instead of a class
/// (<paramref name="SchoolClassId"/> is then left out); the subject is the
/// group's.</param>
public record CreateHomeworkRequest(
    int? SchoolClassId,
    int SubjectId,
    string Text,
    DateOnly DueDate,
    bool RequiresCompletion,
    List<int>? StudentIds = null,
    int? StudentGroupId = null);

public record MarkHomeworkCompleteRequest(int StudentId);

public record HomeworkAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes);

/// <param name="SchoolClassId">Null for homework of a group.</param>
/// <param name="SchoolClassName">The class, or the group as "Nj (7.A, 7.B)".</param>
/// <param name="SchoolClassIds">The class, or the group's classes.</param>
public record HomeworkDto(
    int Id,
    int? SchoolClassId,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    string Text,
    DateOnly DueDate,
    bool RequiresCompletion,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    /// <summary>Empty means "whole class / group" — see Homework.Students.</summary>
    IReadOnlyList<int> TargetStudentIds,
    IReadOnlyList<HomeworkAttachmentDto> Attachments,
    /// <summary>Which of the (caller-visible) target students have marked
    /// it done — Teacher/Admin see everyone's, Parent/Student only their
    /// own linked student(s).</summary>
    IReadOnlyList<int> CompletedStudentIds,
    int? StudentGroupId,
    IReadOnlyList<int> SchoolClassIds,
    /// <summary>The (caller-visible) pupils of the group now; null for a class's homework.</summary>
    IReadOnlyList<int>? GroupStudentIds = null);
