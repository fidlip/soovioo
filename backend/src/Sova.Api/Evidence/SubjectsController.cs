using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/subjects")]
public class SubjectsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetSubjects()
    {
        return Ok(await db.Subjects.OrderBy(s => s.Name).ToListAsync());
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Subject>> AddSubject(CreateSubjectRequest request)
    {
        var subject = new Subject
        {
            Name = request.Name,
            Abbreviation = request.Abbreviation,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSubjects), new { id = subject.Id }, subject);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Subject>> UpdateSubject(int id, CreateSubjectRequest request)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        subject.Name = request.Name;
        subject.Abbreviation = request.Abbreviation;
        await db.SaveChangesAsync();
        return Ok(subject);
    }

    /// <summary>Rejected while any grade or timetable slot still references
    /// the subject — a grade is a real academic record, never removed as a
    /// side effect of tidying up the subject list.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteSubject(int id)
    {
        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        if (await db.Grades.AnyAsync(g => g.SubjectId == id))
        {
            return BadRequest("Předmět má zapsané známky — nelze smazat.");
        }

        if (await db.TimetableEntries.AnyAsync(e => e.SubjectId == id))
        {
            return BadRequest("Předmět je v rozvrhu — nelze smazat.");
        }

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateSubjectRequest(string Name, string? Abbreviation = null);
