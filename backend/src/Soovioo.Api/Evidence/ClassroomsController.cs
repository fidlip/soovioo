using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/classrooms")]
[AuditArea("Učebny")]
public class ClassroomsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Classroom>>> GetClassrooms()
    {
        return Ok(await db.Classrooms.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost]
    [RequirePermission(Permission.Classrooms)]
    [AuditAction("Vytvořena učebna")]
    public async Task<ActionResult<Classroom>> AddClassroom(CreateClassroomRequest request)
    {
        var classroom = new Classroom
        {
            Name = request.Name,
            ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim(),
            Type = request.Type,
            Capacity = request.Capacity,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetClassrooms), new { id = classroom.Id }, classroom);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Classrooms)]
    [AuditAction("Upravena učebna")]
    public async Task<ActionResult<Classroom>> UpdateClassroom(int id, CreateClassroomRequest request)
    {
        var classroom = await db.Classrooms.FindAsync(id);
        if (classroom is null)
        {
            return NotFound();
        }

        classroom.Name = request.Name;
        classroom.ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim();
        classroom.Type = request.Type;
        classroom.Capacity = request.Capacity;
        await db.SaveChangesAsync();
        return Ok(classroom);
    }

    /// <summary>Rejected while any timetable slot, subject default or class
    /// still references the classroom — same "unlink first" rule as Subjects'
    /// own delete, see SubjectsController.DeleteSubject.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.Classrooms)]
    [AuditAction("Smazána učebna")]
    public async Task<IActionResult> DeleteClassroom(int id)
    {
        var classroom = await db.Classrooms.FindAsync(id);
        if (classroom is null)
        {
            return NotFound();
        }

        if (await db.TimetableEntries.AnyAsync(e => e.ClassroomId == id))
        {
            return BadRequest("Učebna je použitá v rozvrhu — nelze smazat.");
        }

        if (await db.Subjects.AnyAsync(s => s.Classrooms.Any(c => c.ClassroomId == id)))
        {
            return BadRequest("Učebna je u předmětu — nelze smazat.");
        }

        if (await db.SchoolClasses.AnyAsync(c => c.HomeroomId == id))
        {
            return BadRequest("Učebna je kmenová učebna třídy — nelze smazat.");
        }

        db.Classrooms.Remove(classroom);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateClassroomRequest(
    string Name, ClassroomType? Type = null, int? Capacity = null, string? ShortName = null);
