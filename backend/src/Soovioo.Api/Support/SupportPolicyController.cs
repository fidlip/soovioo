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
/// its admin (the built-in role, not one given Administrace) decides.</summary>
[Authorize]
[ApiController]
[AuditArea("Podpora")]
public class SupportPolicyController(
    AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier, IOptions<SupportOptions> options)
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
    /// can't let themselves in; the SuperAdmins hear of the change.</summary>
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
            await db.SaveChangesAsync();
            var superAdmins = await db.Users.IgnoreQueryFilters()
                .Where(u => u.IsSuperAdmin && u.SchoolId == null)
                .Select(u => u.Id)
                .ToListAsync();
            await notifier.NotifyAsync(
                school.Id, superAdmins, NotificationType.SupportTicket,
                $"{school.Name}: vstup podpory do školy — {Label(request.Access)}", null, "superadmin");
            await db.SaveChangesAsync();
        }
        return Ok(await DtoAsync(school));
    }

    private async Task<SchoolSupportDto> DtoAsync(School school) =>
        new(school.SupportAccess, options.Value.ApprovalMinutes, !InSupportMode() && await IsAdminAsync());

    private bool InSupportMode() => User.HasClaim(c => c.Type == SupportClaims.SessionId);

    private async Task<bool> IsAdminAsync() =>
        int.TryParse(User.FindFirstValue(PermissionAccess.RoleIdClaim), out var roleId)
        && await db.Roles.AnyAsync(r => r.Id == roleId && r.BuiltIn == UserRole.Admin);

    private static string Label(SupportAccess access) => access switch
    {
        SupportAccess.Always => "kdykoliv",
        SupportAccess.Never => "nikdy",
        _ => "po schválení",
    };
}

public record SetSupportAccessRequest(SupportAccess Access);

/// <param name="ApprovalMinutes">How long allowing entering for a ticket lasts.</param>
/// <param name="CanChange">The caller is the school's admin (and not the support inside it).</param>
public record SchoolSupportDto(SupportAccess Access, int ApprovalMinutes, bool CanChange);
