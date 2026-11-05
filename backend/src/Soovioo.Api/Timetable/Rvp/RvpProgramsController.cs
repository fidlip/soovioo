using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Soovioo.Api.Timetable.Rvp;

[Authorize]
[ApiController]
[Route("api/rvp-programs")]
public class RvpProgramsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<RvpProgramOptionDto>> GetPrograms() =>
        Ok(RvpCatalog.Programs.Select(p => new RvpProgramOptionDto(p.Code, p.Name, p.Family)));
}

public record RvpProgramOptionDto(string Code, string Name, string Family);
