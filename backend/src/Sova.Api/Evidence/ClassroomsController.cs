using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/classrooms")]
public class ClassroomsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Classroom>>> GetClassrooms()
    {
        return Ok(await db.Classrooms.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Classroom>> AddClassroom(CreateClassroomRequest request)
    {
        var classroom = new Classroom
        {
            Name = request.Name,
            Type = request.Type,
            Capacity = request.Capacity,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetClassrooms), new { id = classroom.Id }, classroom);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Classroom>> UpdateClassroom(int id, CreateClassroomRequest request)
    {
        var classroom = await db.Classrooms.FindAsync(id);
        if (classroom is null)
        {
            return NotFound();
        }

        classroom.Name = request.Name;
        classroom.Type = request.Type;
        classroom.Capacity = request.Capacity;
        await db.SaveChangesAsync();
        return Ok(classroom);
    }

    /// <summary>Rejected while any timetable slot or subject default still
    /// references the classroom — same "unlink first" rule as Subjects'
    /// own delete, see SubjectsController.DeleteSubject.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
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

        if (await db.Subjects.AnyAsync(s => s.DefaultClassroomId == id))
        {
            return BadRequest("Učebna je výchozí pro předmět — nelze smazat.");
        }

        db.Classrooms.Remove(classroom);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateClassroomRequest(string Name, string? Type = null, int? Capacity = null);
