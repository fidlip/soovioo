using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable;

/// <summary>Generating a draft version's timetable: started here, run by
/// <see cref="TimetableGenerationJob"/>, its result read back here.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions/{versionId:int}/generation")]
public class TimetableGenerationController(
    AppDbContext db, ICurrentSchoolContext currentSchool, IBackgroundJobClient jobs, TimeProvider clock) : ControllerBase
{
    /// <summary>A run takes about a minute; one unfinished after this was cut
    /// off (the server stopped) and failed.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);

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
        if (generation.Status is TimetableGenerationStatus.Queued or TimetableGenerationStatus.Running
            && generation.CreatedAt < clock.GetUtcNow() - Stale)
        {
            generation.Status = TimetableGenerationStatus.Failed;
            generation.FinishedAt = clock.GetUtcNow();
            generation.Messages = ["Generování se nedokončilo, spusťte ho znovu."];
            await db.SaveChangesAsync();
        }
        return Ok(TimetableGenerationDto.Of(generation));
    }

    [HttpPost]
    [RequirePermission(Permission.Timetable)]
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
        var since = clock.GetUtcNow() - Stale;
        if (await db.TimetableGenerations.AnyAsync(g => g.TimetableVersionId == versionId
                && (g.Status == TimetableGenerationStatus.Queued || g.Status == TimetableGenerationStatus.Running)
                && g.CreatedAt > since))
        {
            return Conflict("Generování této verze už běží.");
        }

        var generation = new TimetableGeneration
        {
            SchoolId = currentSchool.SchoolId!.Value,
            TimetableVersionId = versionId,
            Mode = request.Mode,
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

public record GenerateTimetableRequest(TimetableGenerationMode Mode);

public record TimetableGenerationDto(
    int Id,
    TimetableGenerationMode Mode,
    TimetableGenerationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    int Placed,
    IReadOnlyList<string> Messages)
{
    public static TimetableGenerationDto Of(TimetableGeneration g) =>
        new(g.Id, g.Mode, g.Status, g.CreatedAt, g.FinishedAt, g.Placed, g.Messages);
}
