using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Support;

/// <summary>The school's choice whether the support may enter it
/// (<see cref="SupportAccess"/>) — who may see its people's data, so only
/// its admin (the built-in role, not one given Administrace) decides — and
/// whether it may enter also as the users a ticket names (<see cref="SupportAsPolicy"/>).</summary>
[Authorize]
[ApiController]
[AuditArea("Podpora")]
public class SupportPolicyController(
    AppDbContext db, ICurrentSchoolContext currentSchool, IOptions<SupportOptions> options,
    TimeProvider clock, IBackofficeTickets? tickets = null, SupportKeys? keys = null)
    : ControllerBase
{
    [HttpGet("api/my-school/support")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SchoolSupportDto>> GetSchoolSupport()
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        return Ok(await DtoAsync(school));
    }

    /// <summary>The school's admin, not in support mode — the SuperAdmin
    /// can't let themselves in. Never revokes the support's keys.</summary>
    [HttpPut("api/my-school/support-access")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Změněn přístup podpory do školy")]
    public async Task<ActionResult<SchoolSupportDto>> SetAccess(SetSupportAccessRequest request)
    {
        if (InSupportMode())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "V režimu podpory o přístupu podpory rozhoduje jen škola.");
        }
        if (!await IsAdminAsync())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vstup podpory do školy mění jen admin školy.");
        }
        if (!Enum.IsDefined(request.Access))
        {
            return BadRequest();
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        if (school.SupportAccess != request.Access)
        {
            school.SupportAccess = request.Access;
            if (request.Access == SupportAccess.Never && keys is not null)
            {
                var id = User.UserId();
                var me = await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == id);
                await keys.RevokeAllAsync(school.Id, me, People.PersonNames.Display(me));
            }
            await db.SaveChangesAsync();
        }
        return Ok(await DtoAsync(school));
    }

    /// <summary>Whether the support enters as the school's admin, or only as
    /// a user the ticket names ("Vstoupit jako…"). The support's sessions in
    /// the school as no longer allowed switch to the admin, or end.</summary>
    [HttpPut("api/my-school/support-as")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Změněno, za koho podpora do školy vstupuje")]
    public async Task<ActionResult<SchoolSupportDto>> SetSupportAs(SetSupportAsRequest request)
    {
        if (InSupportMode())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "V režimu podpory o přístupu podpory rozhoduje jen škola.");
        }
        if (!await IsAdminAsync())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vstup podpory do školy mění jen admin školy.");
        }
        if (!Enum.IsDefined(request.Scope))
        {
            return BadRequest();
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        if (school.SupportAs != request.Scope)
        {
            school.SupportAs = request.Scope;
            await SupportAsPolicy.ApplyToSessionsAsync(db, school, clock.GetUtcNow(), await MyNameAsync());
            await db.SaveChangesAsync();
        }
        return Ok(await DtoAsync(school));
    }

    /// <summary>The school's accounts with their roles — to name on a
    /// ticket whom the support may enter as.</summary>
    [HttpGet("api/my-school/support-as/users")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<IReadOnlyList<SupportAsUserDto>>> GetSupportAsUsers() =>
        await SupportAsPolicy.AccountsAsync(db, currentSchool.SchoolId!.Value);

    private async Task<SchoolSupportDto> DtoAsync(School school) =>
        new(school.SupportAccess, options.Value.ApprovalMinutes, !InSupportMode() && await IsAdminAsync(),
            school.SupportAs, tickets?.Connected == true);

    private bool InSupportMode() => User.InSupportMode();

    private async Task<string> MyNameAsync()
    {
        var id = User.UserId();
        var me = await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == id);
        return People.PersonNames.Display(me);
    }

    private async Task<bool> IsAdminAsync() =>
        int.TryParse(User.FindFirstValue(PermissionAccess.RoleIdClaim), out var roleId)
        && await db.Roles.AnyAsync(r => r.Id == roleId && r.BuiltIn == UserRole.Admin);
}

public record SetSupportAccessRequest(SupportAccess Access);

/// <param name="ApprovalMinutes">How long allowing entering for a ticket lasts.</param>
/// <param name="CanChange">The caller is the school's admin (and not the support inside it).</param>
/// <param name="SupportAs">Whether the support may enter also as the users a ticket names.</param>
/// <param name="Available">The instance has the support (is connected to a backoffice).</param>
public record SchoolSupportDto(
    SupportAccess Access, int ApprovalMinutes, bool CanChange, SupportAsScope SupportAs, bool Available);
