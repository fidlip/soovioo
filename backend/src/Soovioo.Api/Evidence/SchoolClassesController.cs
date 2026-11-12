using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable.Rvp;

namespace Soovioo.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/school-classes")]
[AuditArea("Třídy")]
public class SchoolClassesController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetSchoolClasses()
    {
        return Ok(await db.SchoolClasses.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost]
    [RequirePermission(Permission.Classes)]
    [AuditAction("Vytvořena třída")]
    public async Task<ActionResult<SchoolClass>> AddSchoolClass(CreateSchoolClassRequest request)
    {
        if (!await HomeroomExistsAsync(request.HomeroomId))
        {
            return BadRequest("Učebna neexistuje.");
        }
        if (await ProgramErrorAsync(request.RvpProgramCode) is { } programError)
        {
            return BadRequest(programError);
        }

        var schoolClass = new SchoolClass
        {
            Name = request.Name,
            Rocnik = request.Rocnik,
            ClassTeacherId = request.ClassTeacherId,
            TeachingLanguage = request.TeachingLanguage,
            HomeroomId = request.HomeroomId,
            RvpProgramCode = request.RvpProgramCode,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSchoolClasses), new { id = schoolClass.Id }, schoolClass);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Classes)]
    [AuditAction("Upravena třída")]
    public async Task<ActionResult<SchoolClass>> UpdateSchoolClass(int id, CreateSchoolClassRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(id);
        if (schoolClass is null)
        {
            return NotFound();
        }

        if (!await HomeroomExistsAsync(request.HomeroomId))
        {
            return BadRequest("Učebna neexistuje.");
        }
        if (await ProgramErrorAsync(request.RvpProgramCode) is { } programError)
        {
            return BadRequest(programError);
        }

        schoolClass.Name = request.Name;
        schoolClass.Rocnik = request.Rocnik;
        schoolClass.ClassTeacherId = request.ClassTeacherId;
        schoolClass.TeachingLanguage = request.TeachingLanguage;
        schoolClass.HomeroomId = request.HomeroomId;
        schoolClass.RvpProgramCode = request.RvpProgramCode;
        await db.SaveChangesAsync();
        return Ok(schoolClass);
    }

    /// <summary>Rejected while the class still has students — move them to
    /// another class first. Timetable entries for the class are removed
    /// along with it (they're just schedule slots, not records).</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.Classes)]
    [AuditAction("Smazána třída")]
    public async Task<IActionResult> DeleteSchoolClass(int id)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(id);
        if (schoolClass is null)
        {
            return NotFound();
        }

        if (await db.Students.AnyAsync(s => s.SchoolClassId == id))
        {
            return BadRequest("Třída má žáky — nelze smazat.");
        }

        db.TimetableEntries.RemoveRange(db.TimetableEntries.Where(e => e.SchoolClassId == id));
        db.SchoolClasses.Remove(schoolClass);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> ProgramErrorAsync(string? code)
    {
        if (code is null)
        {
            return null;
        }
        if (RvpCatalog.Find(code) is not { } program)
        {
            return "Neznámý vzdělávací program.";
        }
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        return program.SchoolType == school?.Type ? null : "Tento vzdělávací program není pro tento typ školy.";
    }

    private async Task<bool> HomeroomExistsAsync(int? id) =>
        id is null || await db.Classrooms.AnyAsync(c => c.Id == id);
}

public record CreateSchoolClassRequest(
    string Name, int? Rocnik = null, int? ClassTeacherId = null, string? TeachingLanguage = null, int? HomeroomId = null,
    string? RvpProgramCode = null);
