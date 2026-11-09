using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>The school's choice whether the support may enter it
/// (<see cref="SupportAccess"/>).</summary>
[Authorize]
[ApiController]
public class SupportPolicyController(
    AppDbContext db, ICurrentSchoolContext currentSchool, IOptions<SupportOptions> options) : ControllerBase
{
    [HttpGet("api/my-school/support")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SchoolSupportDto>> GetSchoolSupport()
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        return Ok(new SchoolSupportDto(school.SupportAccess, options.Value.ApprovalMinutes));
    }

    /// <summary>Not in support mode — the SuperAdmin can't let themselves in.</summary>
    [HttpPut("api/my-school/support-access")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SchoolSupportDto>> SetAccess(SetSupportAccessRequest request)
    {
        if (User.HasClaim(c => c.Type == SupportClaims.SessionId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "V režimu podpory o přístupu podpory rozhoduje jen škola.");
        }
        if (!Enum.IsDefined(request.Access))
        {
            return BadRequest();
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.SupportAccess = request.Access;
        await db.SaveChangesAsync();
        return Ok(new SchoolSupportDto(school.SupportAccess, options.Value.ApprovalMinutes));
    }
}

public record SetSupportAccessRequest(SupportAccess Access);

/// <param name="ApprovalMinutes">How long allowing entering for a ticket lasts.</param>
public record SchoolSupportDto(SupportAccess Access, int ApprovalMinutes);
