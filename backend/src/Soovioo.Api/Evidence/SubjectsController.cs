using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Evidence;

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
        var subjects = await db.Subjects.OrderBy(s => s.Name).ToListAsync();
        var covered = await db.CoveredSubjectIdsAsync();
        subjects.ForEach(s => s.CoveredByDivisions = covered.Contains(s.Id));
        return Ok(subjects);
    }

    [HttpPost]
    [RequirePermission(Permission.Subjects)]
    public async Task<ActionResult<Subject>> AddSubject(CreateSubjectRequest request)
    {
        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Barva musí být ve formátu #rrggbb.");
        }

        if (await ClassroomErrorAsync(request) is { } error)
        {
            return BadRequest(error);
        }

        var subject = new Subject
        {
            Name = request.Name,
            Abbreviation = request.Abbreviation,
            Color = request.Color,
            Classrooms = Classrooms(request),
            DoubleLessons = request.DoubleLessons,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSubjects), new { id = subject.Id }, subject);
    }

    private async Task<string?> ClassroomErrorAsync(CreateSubjectRequest request)
    {
        var items = request.Classrooms ?? [];
        if (items.Any(c => (c.ClassroomId is null) == (c.Type is null)))
        {
            return "U předmětu je buď učebna, nebo typ učebny.";
        }
        if (items.Distinct().Count() != items.Count)
        {
            return "Učebna je u předmětu dvakrát.";
        }
        var ids = items.Where(c => c.ClassroomId is not null).Select(c => c.ClassroomId!.Value).ToList();
        return await db.Classrooms.CountAsync(r => ids.Contains(r.Id)) != ids.Count ? "Neznámá učebna." : null;
    }

    private static List<SubjectClassroom> Classrooms(CreateSubjectRequest request) =>
        [.. (request.Classrooms ?? []).Select((c, i) => new SubjectClassroom { Position = i, ClassroomId = c.ClassroomId, Type = c.Type })];

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Subjects)]
    public async Task<ActionResult<Subject>> UpdateSubject(int id, CreateSubjectRequest request)
    {
        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Barva musí být ve formátu #rrggbb.");
        }

        if (await ClassroomErrorAsync(request) is { } error)
        {
            return BadRequest(error);
        }

        var subject = await db.Subjects.FindAsync(id);
        if (subject is null)
        {
            return NotFound();
        }

        subject.Name = request.Name;
        subject.Abbreviation = request.Abbreviation;
        subject.Color = request.Color;
        subject.Classrooms = Classrooms(request);
        subject.DoubleLessons = request.DoubleLessons;
        await db.SaveChangesAsync();
        return Ok(subject);
    }

    /// <summary>Rejected while any grade or timetable slot still references
    /// the subject — a grade is a real academic record, never removed as a
    /// side effect of tidying up the subject list.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.Subjects)]
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

        if (await db.StudentGroups.AnyAsync(g => g.SubjectId == id))
        {
            return BadRequest("Předmět má skupina žáků — nelze smazat.");
        }

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateSubjectRequest(
    string Name, string? Abbreviation = null, string? Color = null, IReadOnlyList<SubjectClassroomRequest>? Classrooms = null,
    bool DoubleLessons = false);

/// <summary>A particular classroom or a type, not both.</summary>
public record SubjectClassroomRequest(int? ClassroomId = null, ClassroomType? Type = null);
