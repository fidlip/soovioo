using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/school-classes")]
public class SchoolClassesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetSchoolClasses()
    {
        return Ok(await db.SchoolClasses.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost]
    public async Task<ActionResult<SchoolClass>> AddSchoolClass(CreateSchoolClassRequest request)
    {
        var schoolClass = new SchoolClass { Name = request.Name };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSchoolClasses), new { id = schoolClass.Id }, schoolClass);
    }
}

public record CreateSchoolClassRequest(string Name);
