using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Grades;

[Authorize]
[ApiController]
[Route("api/grades")]
public class GradesController(AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GradeDto>>> GetGrades()
    {
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);

        var query = db.Grades.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(g => allowedStudentIds.Contains(g.StudentId));
        }

        var grades = await query
            .OrderByDescending(g => g.GradedOn)
            .ThenBy(g => g.Student!.Person!.LastName)
            .ThenBy(g => g.Student!.Person!.FirstName)
            .ThenByDescending(g => g.CreatedAt)
            .Select(g => new GradeDto(
                g.Id,
                g.StudentId,
                g.Student!.Person!.FirstName + " " + g.Student.Person!.LastName,
                g.SubjectId,
                g.Subject!.Name,
                g.Value,
                g.Weight,
                g.Note,
                g.GradedOn,
                g.CreatedAt))
            .ToListAsync();

        return Ok(grades);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<GradeDto>> AddGrade(CreateGradeRequest request)
    {
        var studentExists = await db.Students.AnyAsync(s => s.Id == request.StudentId);
        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == request.SubjectId);
        if (!studentExists || !subjectExists)
        {
            return BadRequest("Neznámý žák nebo předmět.");
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
        await notifier.NotifyAsync(
            grade.SchoolId, await notifier.FamilyAsync(grade.StudentId), NotificationType.Grade,
            $"Nová známka — {subject!.Name}",
            $"{student!.Person!.FullName}: {grade.Value}" + (grade.Weight != 1 ? $" (váha {grade.Weight})" : ""),
            "grades", grade.Id, grade.StudentId);
        await db.SaveChangesAsync();
        var dto = new GradeDto(
            grade.Id,
            grade.StudentId,
            student!.Person!.FullName,
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
    [Authorize(Roles = "Teacher,Admin")]
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

        grade.StudentId = request.StudentId;
        grade.SubjectId = request.SubjectId;
        grade.Value = request.Value;
        grade.Weight = request.Weight;
        grade.Note = request.Note;
        if (request.GradedOn is { } gradedOn)
        {
            grade.GradedOn = gradedOn;
        }
        await db.SaveChangesAsync();

        var dto = new GradeDto(
            grade.Id, grade.StudentId, student.Person!.FullName,
            grade.SubjectId, subject.Name, grade.Value, grade.Weight, grade.Note, grade.GradedOn, grade.CreatedAt);
        return Ok(dto);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteGrade(int id)
    {
        var grade = await db.Grades.FindAsync(id);
        if (grade is null)
        {
            return NotFound();
        }

        db.Grades.Remove(grade);
        await db.SaveChangesAsync();
        return NoContent();
    }
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
    int SubjectId,
    string SubjectName,
    int Value,
    int Weight,
    string? Note,
    DateOnly GradedOn,
    DateTimeOffset CreatedAt);
