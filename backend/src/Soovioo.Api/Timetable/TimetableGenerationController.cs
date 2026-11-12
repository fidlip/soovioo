using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable;

/// <summary>Generating a draft version's timetable: started here, run by
/// <see cref="TimetableGenerationJob"/>, its result read back here.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions/{versionId:int}/generation")]
[AuditArea("Rozvrh")]
public class TimetableGenerationController(
    AppDbContext db, ICurrentSchoolContext currentSchool, IBackgroundJobClient jobs, TimeProvider clock) : ControllerBase
{
    /// <summary>A run unfinished long after its limit, or never started, was
    /// cut off (the server stopped) and failed.</summary>
    private bool IsStale(TimetableGeneration g)
    {
        var now = clock.GetUtcNow();
        return g.Status switch
        {
            TimetableGenerationStatus.Running => (g.StartedAt ?? g.CreatedAt) < now - TimetableGenerationJob.TimeLimit - TimeSpan.FromMinutes(5),
            TimetableGenerationStatus.Queued => g.CreatedAt < now - TimeSpan.FromHours(1),
            _ => false,
        };
    }

    /// <summary>The latest run; no content when there's none.</summary>
    [HttpGet]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableGenerationDto>> GetGeneration(int versionId)
    {
        var generation = await db.TimetableGenerations.Where(g => g.TimetableVersionId == versionId)
            .OrderByDescending(g => g.Id).FirstOrDefaultAsync();
        if (generation is null)
        {
            return NoContent();
        }
        if (IsStale(generation))
        {
            generation.Status = TimetableGenerationStatus.Failed;
            generation.FinishedAt = clock.GetUtcNow();
            generation.Messages = ["Generování se nedokončilo, spusťte ho znovu."];
            await db.SaveChangesAsync();
        }
        var dto = TimetableGenerationDto.Of(generation);
        return Ok(generation.Status == TimetableGenerationStatus.Queued ? await WithQueueAsync(dto) : dto);
    }

    /// <summary>The user saw how it ended: its notification is read.</summary>
    [HttpPost("seen")]
    [RequirePermission(Permission.Timetable)]
    [NoAudit]
    public async Task<IActionResult> Seen(int versionId)
    {
        var me = User.UserId();
        await db.Notifications
            .Where(n => n.UserId == me && n.Type == Notifications.NotificationType.Timetable
                && n.TargetId == versionId && n.ReadAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, clock.GetUtcNow()));
        return NoContent();
    }

    /// <summary>Runs of every school ahead of this one (one runs at a time),
    /// and how long they'll take judging by the recent ones.</summary>
    private async Task<TimetableGenerationDto> WithQueueAsync(TimetableGenerationDto dto)
    {
        var all = db.TimetableGenerations.IgnoreQueryFilters();
        var running = await all.Where(g => g.Status == TimetableGenerationStatus.Running)
            .Select(g => g.StartedAt ?? g.CreatedAt).ToListAsync();
        var queued = await all.CountAsync(g => g.Status == TimetableGenerationStatus.Queued && g.Id < dto.Id);
        var recent = await all.Where(g => g.Status == TimetableGenerationStatus.Done && g.StartedAt != null)
            .OrderByDescending(g => g.Id).Take(20)
            .Select(g => new { g.StartedAt, g.FinishedAt }).ToListAsync();
        var typical = recent.Count == 0
            ? TimeSpan.FromSeconds(90)
            : TimeSpan.FromSeconds(recent.Average(g => (g.FinishedAt!.Value - g.StartedAt!.Value).TotalSeconds));
        var now = clock.GetUtcNow();
        var wait = typical * queued + running.Aggregate(TimeSpan.Zero,
            (sum, started) => sum + (typical - (now - started) is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero));
        return dto with { QueueAhead = queued + running.Count, WaitSeconds = (int)wait.TotalSeconds };
    }

    [HttpPost]
    [RequirePermission(Permission.Timetable)]
    [AuditAction("Spuštěno generování rozvrhu")]
    public async Task<ActionResult<TimetableGenerationDto>> Generate(int versionId, GenerateTimetableRequest request)
    {
        var version = await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null)
        {
            return NotFound();
        }
        if (version.Status != TimetableVersionStatus.Draft)
        {
            return BadRequest("Generovat jde jen do konceptu.");
        }
        var unfinished = await db.TimetableGenerations.Where(g => g.TimetableVersionId == versionId
                && (g.Status == TimetableGenerationStatus.Queued || g.Status == TimetableGenerationStatus.Running))
            .ToListAsync();
        if (unfinished.Any(g => !IsStale(g)))
        {
            return Conflict("Generování této verze už běží.");
        }

        var generation = new TimetableGeneration
        {
            SchoolId = currentSchool.SchoolId!.Value,
            TimetableVersionId = versionId,
            Mode = request.Mode,
            Moves = request.Moves,
            Mismatches = request.Mismatches,
            Status = TimetableGenerationStatus.Queued,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = User.UserId(),
        };
        db.TimetableGenerations.Add(generation);
        await db.SaveChangesAsync();
        var schoolId = generation.SchoolId;
        var id = generation.Id;
        jobs.Enqueue<TimetableGenerationJob>(job => job.RunAsync(id, schoolId));
        return Accepted(TimetableGenerationDto.Of(generation));
    }
}

public record GenerateTimetableRequest(
    TimetableGenerationMode Mode,
    TimetableUpdateMoves Moves = TimetableUpdateMoves.Fewest,
    TimetableUpdateMismatches Mismatches = TimetableUpdateMismatches.FollowPlan);

public record TimetableGenerationDto(
    int Id,
    TimetableGenerationMode Mode,
    TimetableGenerationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Placed,
    IReadOnlyList<string> Messages,
    IReadOnlyList<string> Notes,
    TimetableGenerator.Progress? Progress)
{
    /// <summary>While queued: runs before this one, and the estimated wait.</summary>
    public int QueueAhead { get; init; }
    public int WaitSeconds { get; init; }

    public static TimetableGenerationDto Of(TimetableGeneration g) =>
        new(g.Id, g.Mode, g.Status, g.CreatedAt, g.StartedAt, g.FinishedAt, g.Placed, g.Messages, g.Notes, g.Progress);
}
