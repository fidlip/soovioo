using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Grades;

[ApiController]
[Route("api/grades")]
public class GradesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Grade>>> GetGrades()
    {
        var grades = await db.Grades
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();

        return Ok(grades);
    }

    [HttpPost]
    public async Task<ActionResult<Grade>> AddGrade(CreateGradeRequest request)
    {
        var grade = new Grade
        {
            StudentName = request.StudentName,
            Subject = request.Subject,
            Value = request.Value,
            Note = request.Note,
        };

        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGrades), new { id = grade.Id }, grade);
    }
}

public record CreateGradeRequest(string StudentName, string Subject, int Value, string? Note);
