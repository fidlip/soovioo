using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Grades;

[Authorize]
[ApiController]
[Route("api/grades")]
public class GradesController(AppDbContext db) : ControllerBase
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
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new GradeDto(
                g.Id,
                g.StudentId,
                g.Student!.FirstName + " " + g.Student.LastName,
                g.SubjectId,
                g.Subject!.Name,
                g.Value,
                g.Weight,
                g.Note,
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
            return BadRequest("Unknown studentId or subjectId.");
        }

        var grade = new Grade
        {
            StudentId = request.StudentId,
            SubjectId = request.SubjectId,
            Value = request.Value,
            Weight = request.Weight,
            Note = request.Note,
        };

        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var student = await db.Students.FindAsync(grade.StudentId);
        var subject = await db.Subjects.FindAsync(grade.SubjectId);
        var dto = new GradeDto(
            grade.Id,
            grade.StudentId,
            $"{student!.FirstName} {student.LastName}",
            grade.SubjectId,
            subject!.Name,
            grade.Value,
            grade.Weight,
            grade.Note,
            grade.CreatedAt);

        return CreatedAtAction(nameof(GetGrades), new { id = grade.Id }, dto);
    }
}

public record CreateGradeRequest(int StudentId, int SubjectId, int Value, int Weight, string? Note);

public record GradeDto(
    int Id,
    int StudentId,
    string StudentName,
    int SubjectId,
    string SubjectName,
    int Value,
    int Weight,
    string? Note,
    DateTimeOffset CreatedAt);
