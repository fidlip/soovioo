using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable;

/// <summary>Versions of the school's timetable: a draft is prepared aside
/// (empty or as a copy), published to start on a date, and thrown away by
/// archiving. Which one applies on a day decides the date alone — see
/// <see cref="TimetableVersions"/>.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions")]
public class TimetableVersionsController(AppDbContext db, ICurrentSchoolContext currentSchool, TimeProvider clock)
    : ControllerBase
{
    private DateOnly Today => SchoolClock.Today(clock);

    /// <summary>Drafts and archived versions only for whoever may edit the
    /// timetable.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimetableVersionDto>>> GetVersions()
    {
        var all = await User.HasPermissionAsync(db, Permission.Timetable);
        return Ok(await db.TimetableVersions
            .Where(v => all || v.Status == TimetableVersionStatus.Published)
            .OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.Id)
            .Select(v => new TimetableVersionDto(
                v.Id, v.Name, v.ValidFrom, v.ValidTo, v.Status, v.Entries.Count, v.CreatedAt, v.PublishedAt))
            .ToListAsync());
    }

    /// <summary>A new draft, empty or with the lessons of
    /// <see cref="CreateTimetableVersionRequest.CopyFromVersionId"/>.</summary>
    [HttpPost]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> CreateVersion(CreateTimetableVersionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Zadejte název verze.");
        }

        var source = request.CopyFromVersionId is { } sourceId
            ? await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == sourceId)
            : null;
        if (request.CopyFromVersionId is not null && source is null)
        {
            return BadRequest("Neznámá verze rozvrhu.");
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var version = new TimetableVersion
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            ValidFrom = request.ValidFrom,
            Status = TimetableVersionStatus.Draft,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = User.UserId(),
            Entries = source?.Entries.Select(e => new TimetableEntry
            {
                SchoolId = schoolId,
                SchoolClassId = e.SchoolClassId,
                SubjectId = e.SubjectId,
                DayOfWeek = e.DayOfWeek,
                PeriodNumber = e.PeriodNumber,
                WeekParity = e.WeekParity,
                TeacherId = e.TeacherId,
                ClassroomId = e.ClassroomId,
            }).ToList() ?? [],
        };
        db.TimetableVersions.Add(version);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetVersions), null, ToDto(version));
    }

    /// <summary>The name always; the start only while the version doesn't
    /// apply yet.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> UpdateVersion(int id, UpdateTimetableVersionRequest request)
    {
        var version = await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Zadejte název verze.");
        }

        if (request.ValidFrom != version.ValidFrom)
        {
            if (version.Status == TimetableVersionStatus.Published)
            {
                if (version.ValidFrom <= Today)
                {
                    return BadRequest("Verzi, která už platí, nejde posunout začátek.");
                }
                if (await PublishErrorAsync(version.Id, request.ValidFrom) is { } error)
                {
                    return BadRequest(error);
                }
            }
            version.ValidFrom = request.ValidFrom;
        }
        version.Name = request.Name.Trim();
        await db.SaveChangesAsync();
        await db.RecomputeTimetableValidityAsync();
        await db.SaveChangesAsync();
        return Ok(ToDto(version));
    }

    /// <summary>From its <see cref="TimetableVersion.ValidFrom"/> on, the
    /// draft replaces the version valid until then.</summary>
    [HttpPost("{id:int}/publish")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> PublishVersion(int id)
    {
        var version = await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null)
        {
            return NotFound();
        }
        if (version.Status != TimetableVersionStatus.Draft)
        {
            return BadRequest("Zveřejnit jde jen koncept.");
        }
        if (await PublishErrorAsync(version.Id, version.ValidFrom) is { } error)
        {
            return BadRequest(error);
        }

        version.Status = TimetableVersionStatus.Published;
        version.PublishedAt = clock.GetUtcNow();
        version.PublishedByUserId = User.UserId();
        await db.SaveChangesAsync();
        await db.RecomputeTimetableValidityAsync();
        await db.SaveChangesAsync();
        return Ok(ToDto(version));
    }

    /// <summary>Throws the version away — a draft, or a published one that
    /// doesn't apply yet. One that applies or applied stays: past attendance
    /// refers to it.</summary>
    [HttpPost("{id:int}/archive")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> ArchiveVersion(int id)
    {
        var version = await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null)
        {
            return NotFound();
        }
        if (version.Status == TimetableVersionStatus.Archived)
        {
            return BadRequest("Verze už je archivovaná.");
        }
        if (version.Status == TimetableVersionStatus.Published && version.ValidFrom <= Today)
        {
            return BadRequest("Verzi, která už platí nebo platila, nejde archivovat.");
        }

        version.Status = TimetableVersionStatus.Archived;
        await db.SaveChangesAsync();
        await db.RecomputeTimetableValidityAsync();
        await db.SaveChangesAsync();
        return Ok(ToDto(version));
    }

    /// <summary>An archived version back among drafts.</summary>
    [HttpPost("{id:int}/restore")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> RestoreVersion(int id)
    {
        var version = await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null)
        {
            return NotFound();
        }
        if (version.Status != TimetableVersionStatus.Archived)
        {
            return BadRequest("Vrátit mezi koncepty jde jen archivovanou verzi.");
        }

        version.Status = TimetableVersionStatus.Draft;
        version.PublishedAt = null;
        version.PublishedByUserId = null;
        await db.SaveChangesAsync();
        return Ok(ToDto(version));
    }

    /// <summary>A version may start only after every published one that
    /// already applies (the past stays as it was), not before today unless
    /// none applies yet, and never on the same day as another.</summary>
    private async Task<string?> PublishErrorAsync(int versionId, DateOnly validFrom)
    {
        var published = await db.TimetableVersions
            .Where(v => v.Id != versionId && v.Status == TimetableVersionStatus.Published)
            .Select(v => v.ValidFrom)
            .ToListAsync();
        var started = published.Where(d => d <= Today).ToList();
        if (started.Count > 0 && validFrom < Today)
        {
            return "Platnost nové verze nemůže začít v minulosti.";
        }
        if (started.Count > 0 && validFrom <= started.Max())
        {
            return $"Platnost musí začít až po začátku platné verze ({SchoolFormat.Date(started.Max())}).";
        }
        if (published.Contains(validFrom))
        {
            return $"Od {SchoolFormat.Date(validFrom)} už platí jiná verze.";
        }
        return null;
    }

    private static TimetableVersionDto ToDto(TimetableVersion v) =>
        new(v.Id, v.Name, v.ValidFrom, v.ValidTo, v.Status, v.Entries.Count, v.CreatedAt, v.PublishedAt);
}

public record TimetableVersionDto(
    int Id,
    string Name,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    TimetableVersionStatus Status,
    int EntryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt);

public record CreateTimetableVersionRequest(string Name, DateOnly ValidFrom, int? CopyFromVersionId = null);

public record UpdateTimetableVersionRequest(string Name, DateOnly ValidFrom);
