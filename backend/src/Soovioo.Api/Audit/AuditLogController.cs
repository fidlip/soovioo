using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Audit;

/// <summary>Agenda "Logy": the SuperAdmin reads every school's log (and the
/// SuperAdmin area's), a school admin only their own school's — support
/// mode included, flagged.</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AuditLogController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <param name="to">Exclusive.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetEntries(
        int? schoolId = null, int? schoolClassId = null, int? userId = null, string? area = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Scope(schoolId);
        if (schoolClassId is not null)
        {
            query = query.Where(e => e.SchoolClassId == schoolClassId);
        }
        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }
        if (!string.IsNullOrEmpty(area))
        {
            query = query.Where(e => e.Area == area);
        }
        // Instants, not dates: the client knows where the viewer's day starts.
        if (from is not null)
        {
            var start = from.Value.ToUniversalTime();
            query = query.Where(e => e.At >= start);
        }
        if (to is not null)
        {
            var end = to.Value.ToUniversalTime();
            query = query.Where(e => e.At < end);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogEntryDto(
                e.Id, e.At, e.SchoolId,
                db.Schools.Where(s => s.Id == e.SchoolId).Select(s => s.Name).FirstOrDefault(),
                e.UserId, e.Username, e.UserDisplayName, e.Role, e.SupportBy, e.Method, e.Path, e.Area,
                e.Description, e.StatusCode, e.RequestBody, e.Result, e.IpAddress))
            .ToListAsync();
        return Ok(new PagedResult<AuditLogEntryDto>(items, total, page, pageSize));
    }

    /// <summary>Choices for the filters — agendas, people that appear in the
    /// log, and (with a school) its classes.</summary>
    [HttpGet("filter-options")]
    public async Task<ActionResult<AuditLogFilterOptionsDto>> GetFilterOptions(int? schoolId = null)
    {
        var scope = Scope(schoolId);
        var users = (await scope
                .Where(e => e.UserId != null)
                .Select(e => new { Id = e.UserId!.Value, e.Username, e.UserDisplayName })
                .Distinct()
                .ToListAsync())
            .GroupBy(u => u.Id)
            .Select(g => g.First())
            .Select(u => new AuditLogOptionDto(u.Id,
                u.UserDisplayName is null || u.UserDisplayName == u.Username
                    ? u.Username ?? $"#{u.Id}"
                    : $"{u.UserDisplayName} ({u.Username})"))
            .OrderBy(u => u.Name)
            .ToList();

        var effectiveSchool = IsSuperAdmin ? schoolId : CurrentSchoolId;
        var classes = effectiveSchool is null
            ? []
            : await db.SchoolClasses.IgnoreQueryFilters()
                .Where(c => c.SchoolId == effectiveSchool)
                .OrderBy(c => c.Name)
                .Select(c => new AuditLogOptionDto(c.Id, c.Name))
                .ToListAsync();

        return Ok(new AuditLogFilterOptionsDto(
            AuditDescriptions.AllAreas.ToList(),
            users,
            classes));
    }

    /// <summary>A school admin is pinned to their own school whatever they
    /// ask for; a SuperAdmin sees everything, or one school.</summary>
    private IQueryable<AuditLogEntry> Scope(int? schoolId)
    {
        var query = db.AuditLogEntries.AsQueryable();
        if (!IsSuperAdmin)
        {
            var own = CurrentSchoolId;
            return query.Where(e => own != null && e.SchoolId == own);
        }
        return schoolId is null ? query : query.Where(e => e.SchoolId == schoolId);
    }

    private bool IsSuperAdmin => User.IsInRole(nameof(UserRole.SuperAdmin));

    private int? CurrentSchoolId => int.TryParse(User.FindFirstValue("schoolId"), out var id) ? id : null;
}

/// <param name="SupportBy">Made in support mode by this SuperAdmin.</param>
/// <param name="Result">Error message, or the new record's id.</param>
public record AuditLogEntryDto(
    long Id, DateTimeOffset At, int? SchoolId, string? SchoolName, int? UserId, string? Username,
    string? UserDisplayName, string? Role, string? SupportBy, string Method, string Path, string Area,
    string? Description, int StatusCode, string? RequestBody, string? Result, string? IpAddress);

public record AuditLogOptionDto(int Id, string Name);

public record AuditLogFilterOptionsDto(
    IReadOnlyList<string> Areas, IReadOnlyList<AuditLogOptionDto> Users, IReadOnlyList<AuditLogOptionDto> Classes);
