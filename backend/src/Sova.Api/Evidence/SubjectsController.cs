using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/subjects")]
public partial class SubjectsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorPattern();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetSubjects()
    {
        return Ok(await db.Subjects.OrderBy(s => s.Name).ToListAsync());
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Subject>> AddSubject(CreateSubjectRequest request)
    {
        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Color must be a \"#rrggbb\" hex string.");
        }

        if (request.DefaultClassroomId is not null && await db.Classrooms.FindAsync(request.DefaultClassroomId) is null)
        {
            return BadRequest("Unknown defaultClassroomId.");
        }

        var subject = new Subject
        {
            Name = request.Name,
            Abbreviation = request.Abbreviation,
            Color = request.Color,
            DefaultClassroomId = request.DefaultClassroomId,
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
        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Color must be a \"#rrggbb\" hex string.");
        }

        if (request.DefaultClassroomId is not null && await db.Classrooms.FindAsync(request.DefaultClassroomId) is null)
        {
            return BadRequest("Unknown defaultClassroomId.");
        }

        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        subject.Name = request.Name;
        subject.Abbreviation = request.Abbreviation;
        subject.Color = request.Color;
        subject.DefaultClassroomId = request.DefaultClassroomId;
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

public record CreateSubjectRequest(
    string Name, string? Abbreviation = null, string? Color = null, int? DefaultClassroomId = null);
