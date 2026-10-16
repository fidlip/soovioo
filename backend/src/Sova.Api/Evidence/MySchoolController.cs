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

        return Ok(await SchoolsController.ToDtoAsync(db, school));
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

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>Institutional address/contact/director — used on real
    /// documents, not transmitted to matrika itself.</summary>
    [HttpPut("details")]
    public async Task<ActionResult<SchoolDto>> SetDetails(SetSchoolDetailsRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        school.Street = request.Street;
        school.City = request.City;
        school.PostalCode = request.PostalCode;
        school.DirectorName = request.DirectorName;
        school.Email = request.Email;
        school.Phone = request.Phone;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }
}
