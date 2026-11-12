using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Grades;

[Authorize]
[ApiController]
[Route("api/grades")]
[AuditArea("Známky")]
public class GradesController(AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier) : ControllerBase
{
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<GradeDto>>> GetGrades(
        int? studentId = null, int? subjectId = null, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.GradesRead);

        var query = db.Grades.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(g => allowedStudentIds.Contains(g.StudentId));
        }
        if (studentId is not null)
        {
            query = query.Where(g => g.StudentId == studentId);
        }
        if (subjectId is not null)
        {
            query = query.Where(g => g.SubjectId == subjectId);
        }

        var total = await query.CountAsync();
        var grades = await query
            .OrderByDescending(g => g.GradedOn)
            .ThenBy(g => g.Student!.Person!.LastName)
            .ThenBy(g => g.Student!.Person!.FirstName)
            .ThenByDescending(g => g.CreatedAt)
            .ThenByDescending(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GradeDto(
                g.Id,
                g.StudentId,
                g.Student!.Person!.FirstName + " " + g.Student.Person!.LastName,
                g.Student.SchoolClass!.Name,
                g.SubjectId,
                g.Subject!.Name,
                g.Value,
                g.Weight,
                g.Note,
                g.GradedOn,
                g.CreatedAt))
            .ToListAsync();

        grades = await db.WithStudentLabelsAsync(User, grades, g => g.StudentId, (g, label) => g with { StudentLabel = label });
        return Ok(new PagedResult<GradeDto>(grades, total, page, pageSize));
    }

    /// <summary>One grade the caller sees — whether a notification's grade,
    /// maybe not on the list's first page, is still there.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<GradeDto>> GetGrade(int id)
    {
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.GradesRead);
        var query = db.Grades.Where(g => g.Id == id);
        if (allowedStudentIds is not null)
        {
            query = query.Where(g => allowedStudentIds.Contains(g.StudentId));
        }
        var grade = await query
            .Select(g => new GradeDto(
                g.Id,
                g.StudentId,
                g.Student!.Person!.FirstName + " " + g.Student.Person!.LastName,
                g.Student.SchoolClass!.Name,
                g.SubjectId,
                g.Subject!.Name,
                g.Value,
                g.Weight,
                g.Note,
                g.GradedOn,
                g.CreatedAt))
            .FirstOrDefaultAsync();
        return grade is null
            ? NotFound()
            : Ok(grade with { StudentLabel = (await db.StudentLabelsAsync(User, [grade.StudentId])).GetValueOrDefault(grade.StudentId) });
    }

    [HttpPost]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Zapsána známka")]
    public async Task<ActionResult<GradeDto>> AddGrade(CreateGradeRequest request)
    {
        var studentExists = await db.Students.AnyAsync(s => s.Id == request.StudentId);
        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == request.SubjectId);
        if (!studentExists || !subjectExists)
        {
            return BadRequest("Neznámý žák nebo předmět.");
        }
        if ((await db.CoveredSubjectIdsAsync()).Contains(request.SubjectId))
        {
            return BadRequest(CoveredSubject);
        }
        if (!await MayGradeAsync(request.StudentId, request.SubjectId))
        {
            return Forbid();
        }

        var grade = new Grade
        {
            StudentId = request.StudentId,
            SubjectId = request.SubjectId,
            Value = request.Value,
            Weight = request.Weight,
            Note = request.Note,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        if (request.GradedOn is { } gradedOn)
        {
            grade.GradedOn = gradedOn;
        }

        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var student = await db.Students.FindAsync(grade.StudentId);
        var subject = await db.Subjects.FindAsync(grade.SubjectId);
        var family = await notifier.FamilyAsync(grade.StudentId);
        await notifier.AddAsync(grade.SchoolId, Drafts(grade, await notifier.StudentNamesAsync(grade.StudentId, family), NewTitle(subject!)));
        await db.SaveChangesAsync();
        var dto = new GradeDto(
            grade.Id,
            grade.StudentId,
            student!.Person!.FullName,
            await ClassNameAsync(student),
            grade.SubjectId,
            subject!.Name,
            grade.Value,
            grade.Weight,
            grade.Note,
            grade.GradedOn,
            grade.CreatedAt);

        return CreatedAtAction(nameof(GetGrades), new { id = grade.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Upravena známka")]
    public async Task<ActionResult<GradeDto>> UpdateGrade(int id, CreateGradeRequest request)
    {
        var grade = await db.Grades.FindAsync(id);
        if (grade is null)
        {
            return NotFound();
        }

        var student = await db.Students.FindAsync(request.StudentId);
        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (student is null || subject is null)
        {
            return BadRequest("Neznámý žák nebo předmět.");
        }
        if (request.SubjectId != grade.SubjectId && (await db.CoveredSubjectIdsAsync()).Contains(request.SubjectId))
        {
            return BadRequest(CoveredSubject);
        }
        if (!await MayGradeAsync(grade.StudentId, grade.SubjectId) || !await MayGradeAsync(request.StudentId, request.SubjectId))
        {
            return Forbid();
        }

        var before = (grade.StudentId, grade.SubjectId, grade.Value, grade.Weight, grade.Note, grade.GradedOn);
        grade.StudentId = request.StudentId;
        grade.SubjectId = request.SubjectId;
        grade.Value = request.Value;
        grade.Weight = request.Weight;
        grade.Note = request.Note;
        if (request.GradedOn is { } gradedOn)
        {
            grade.GradedOn = gradedOn;
        }
        if (before != (grade.StudentId, grade.SubjectId, grade.Value, grade.Weight, grade.Note, grade.GradedOn))
        {
            var family = await notifier.FamilyAsync(grade.StudentId);
            await notifier.ChangedAsync(grade.SchoolId, grade.Id, [NotificationType.Grade], family,
                Drafts(grade, await notifier.StudentNamesAsync(grade.StudentId, family), $"Změna známky — {subject.Name}"), NewTitle(subject));
        }
        await db.SaveChangesAsync();

        var dto = new GradeDto(
            grade.Id, grade.StudentId, student.Person!.FullName, await ClassNameAsync(student),
            grade.SubjectId, subject.Name, grade.Value, grade.Weight, grade.Note, grade.GradedOn, grade.CreatedAt);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Smazána známka")]
    public async Task<IActionResult> DeleteGrade(int id)
    {
        var grade = await db.Grades.FindAsync(id);
        if (grade is null)
        {
            return NotFound();
        }
        if (!await MayGradeAsync(grade.StudentId, grade.SubjectId))
        {
            return Forbid();
        }

        db.Grades.Remove(grade);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string NewTitle(Subject subject) => $"Nová známka — {subject.Name}";

    /// <param name="family">Recipients with the pupil's name as each reads it.</param>
    private static List<NotificationDraft> Drafts(Grade grade, Dictionary<int, string> family, string title) =>
        family.Select(f => new NotificationDraft(
                f.Key, NotificationType.Grade, title,
                $"{f.Value}: {grade.Value}" + (grade.Weight != 1 ? $" (váha {grade.Weight})" : ""),
                "grades", grade.Id, grade.StudentId))
            .ToList();

    /// <summary>The pupil and the subject within the caller's
    /// <see cref="Permission.GradesWrite"/> (by default: a teacher grades
    /// what they teach the pupil — in the class or the pupil's group).</summary>
    private const string CoveredSubject = "Předmět se učí ve skupinách — známku dejte z předmětu skupiny.";

    private Task<string> ClassNameAsync(StudentProfile student) =>
        db.SchoolClasses.Where(c => c.Id == student.SchoolClassId).Select(c => c.Name).FirstAsync();

    private Task<bool> MayGradeAsync(int studentId, int subjectId) =>
        User.ReachesStudentSubjectAsync(db, Permission.GradesWrite, studentId, subjectId);
}

/// <param name="GradedOn">Omitted = today on create, unchanged on update.</param>
public record CreateGradeRequest(
    int StudentId,
    int SubjectId,
    int Value,
    int Weight,
    string? Note,
    DateOnly? GradedOn = null);

public record GradeDto(
    int Id,
    int StudentId,
    string StudentName,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    int Value,
    int Weight,
    string? Note,
    DateOnly GradedOn,
    DateTimeOffset CreatedAt,
    People.NameLabel? StudentLabel = null);
