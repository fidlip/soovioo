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
[Authorize]
[AllowSuperAdmin]
[RequirePermission(Permission.AuditLog)]
public class AuditLogController(AppDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int MaxActionOptions = 50;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetEntries(
        [FromQuery] AuditLogFilter filter, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Filtered(filter);
        if (!string.IsNullOrEmpty(filter.Action))
        {
            query = query.Where(e => e.Action == filter.Action);
        }
        else if (!string.IsNullOrWhiteSpace(filter.ActionSearch))
        {
            var search = filter.ActionSearch.Trim();
            query = query.Where(e =>
                (e.Description != null && TextSearch.Fold(e.Description).Contains(TextSearch.Fold(search)))
                || (e.Action != null && TextSearch.Fold(e.Action).Contains(TextSearch.Fold(search))));
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

    /// <summary>The actions (without their subject — "Vytvořen uživatel")
    /// that occur under the other active filters, optionally only those
    /// containing <paramref name="search"/>; for the "Akce" picker.</summary>
    [HttpGet("actions")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetActions([FromQuery] AuditLogFilter filter, string? search = null)
    {
        var query = Filtered(filter).Where(e => e.Action != null).Select(e => e.Action!);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => TextSearch.Fold(a).Contains(TextSearch.Fold(term)));
        }
        return Ok(await query.Distinct().OrderBy(a => a).Take(MaxActionOptions).ToListAsync());
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

    /// <summary>Everything but the action filter, which the entries and the
    /// action picker each apply their own way.</summary>
    private IQueryable<AuditLogEntry> Filtered(AuditLogFilter filter)
    {
        var query = Scope(filter.SchoolId);
        if (filter.SchoolClassId is not null)
        {
            query = query.Where(e => e.SchoolClassId == filter.SchoolClassId);
        }
        if (filter.UserId is not null)
        {
            query = query.Where(e => e.UserId == filter.UserId);
        }
        if (!string.IsNullOrEmpty(filter.Area))
        {
            query = query.Where(e => e.Area == filter.Area);
        }
        // Instants, not dates: the client knows where the viewer's day starts.
        if (filter.From is not null)
        {
            var start = filter.From.Value.ToUniversalTime();
            query = query.Where(e => e.At >= start);
        }
        if (filter.To is not null)
        {
            var end = filter.To.Value.ToUniversalTime();
            query = query.Where(e => e.At < end);
        }
        return query;
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

/// <param name="To">Exclusive.</param>
/// <param name="Action">Exact action, as picked from <c>GET actions</c>.</param>
/// <param name="ActionSearch">Typed text: description or action contains it
/// (case-insensitive). Ignored when <paramref name="Action"/> is set.</param>
public record AuditLogFilter(
    int? SchoolId = null, int? SchoolClassId = null, int? UserId = null, string? Area = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Action = null, string? ActionSearch = null);

public record AuditLogOptionDto(int Id, string Name);

public record AuditLogFilterOptionsDto(
    IReadOnlyList<string> Areas, IReadOnlyList<AuditLogOptionDto> Users, IReadOnlyList<AuditLogOptionDto> Classes);
