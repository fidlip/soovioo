using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

/// <summary>Self-service settings for the Admin's own school — currently just
/// the IZO/RED_IZO the matrika export needs. Separate from the SuperAdmin-only
/// <see cref="SchoolsController"/> so an Admin can fill these in without
/// needing SuperAdmin involvement.</summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/my-school")]
public class MySchoolController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchoolDto>> Get()
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        return Ok(new SchoolDto(school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo));
    }

    [HttpPut("matrika-ids")]
    public async Task<ActionResult<SchoolDto>> SetMatrikaIds(SetSchoolMatrikaIdsRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        school.Izo = request.Izo;
        school.RedIzo = request.RedIzo;
        await db.SaveChangesAsync();

        return Ok(new SchoolDto(school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo));
    }
}
