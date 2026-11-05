using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable.Rvp;

[Authorize]
[ApiController]
[Route("api/rvp-programs")]
public class RvpProgramsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <summary>Those for the type of the caller's school.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RvpProgramOptionDto>>> GetPrograms()
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        return school is null
            ? NotFound()
            : Ok(RvpCatalog.For(school.Type).Select(p => new RvpProgramOptionDto(p.Code, p.Name, p.Family)));
    }
}

public record RvpProgramOptionDto(string Code, string Name, string Family);
