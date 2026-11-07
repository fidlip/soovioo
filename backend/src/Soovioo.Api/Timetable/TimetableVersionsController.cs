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
    public const int MaxNotifyDaysBefore = 30;

    private DateOnly Today => SchoolClock.Today(clock);

    /// <summary>Drafts and archived versions only for whoever may edit the
    /// timetable or see its versions.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimetableVersionDto>>> GetVersions()
    {
        var all = await User.SeesAllTimetableVersionsAsync(db);
        return Ok(await db.TimetableVersions
            .Where(v => all || v.Status == TimetableVersionStatus.Published)
            .OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.Id)
            .Select(v => new TimetableVersionDto(
                v.Id, v.Name, v.ValidFrom, v.ValidTo, v.Status, v.Entries.Count, v.CreatedAt, v.PublishedAt))
            .ToListAsync());
    }

    /// <summary>Classrooms taken at a slot — for a new lesson's first free
    /// one; a lesson only in the other week doesn't count.</summary>
    [HttpGet("{id:int}/busy-classrooms")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<IEnumerable<int>>> GetBusyClassrooms(
        int id, DayOfWeek day, int period, WeekParity weekParity = WeekParity.Every) =>
        Ok(await db.TimetableEntries
            .Where(e => e.TimetableVersionId == id && e.DayOfWeek == day && e.PeriodNumber == period
                && e.ClassroomId != null
                && (weekParity == WeekParity.Every || e.WeekParity == WeekParity.Every || e.WeekParity == weekParity))
            .Select(e => e.ClassroomId!.Value).Distinct().ToListAsync());

    /// <summary>What's wrong with the timetable — before publishing it.</summary>
    [HttpGet("{id:int}/problems")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<IEnumerable<string>>> GetProblems(int id)
    {
        var version = await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == id);
        return version is null ? NotFound() : Ok(await TimetableCheck.ProblemsAsync(db, version));
    }

    /// <summary>A new draft, empty or with the lessons, učební plán, groups
    /// and přidělení výuky of <see cref="CreateTimetableVersionRequest.CopyFromVersionId"/>.</summary>
    [HttpPost]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> CreateVersion(CreateTimetableVersionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Zadejte název verze.");
        }

        var source = request.CopyFromVersionId is { } sourceId ? await WithContentAsync(sourceId) : null;
        if (request.CopyFromVersionId is not null && source is null)
        {
            return BadRequest("Neznámá verze rozvrhu.");
        }

        var version = NewDraft(request.Name.Trim(), request.ValidFrom, source);
        db.TimetableVersions.Add(version);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetVersions), null, ToDto(version));
    }

    /// <summary>The draft of a change to the version that applies, from a day
    /// on: the one already waiting for that day, otherwise a new copy.</summary>
    [HttpPost("{id:int}/edit-from-date")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> EditFromDate(int id, EditFromDateRequest request)
    {
        var source = await WithContentAsync(id);
        if (source is null)
        {
            return NotFound();
        }
        if (source.Status != TimetableVersionStatus.Published || source.ValidFrom > Today || source.ValidTo < Today)
        {
            return BadRequest("Od data jde upravit jen platná verze.");
        }
        if (await PublishErrorAsync(versionId: 0, request.ValidFrom) is { } error)
        {
            return BadRequest(error);
        }

        var version = await db.TimetableVersions.Include(v => v.Entries)
            .Where(v => v.Status == TimetableVersionStatus.Draft && v.ValidFrom == request.ValidFrom)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (version is null)
        {
            version = NewDraft($"Změna od {SchoolFormat.Date(request.ValidFrom)}", request.ValidFrom, source);
            db.TimetableVersions.Add(version);
            await db.SaveChangesAsync();
        }
        return Ok(ToDto(version));
    }

    private Task<TimetableVersion?> WithContentAsync(int id) =>
        db.TimetableVersions.Include(v => v.Entries).Include(v => v.Curriculum).Include(v => v.ClassCurriculum)
            .Include(v => v.Assignments).Include(v => v.Divisions)
            .Include(v => v.Groups).ThenInclude(g => g.Classes).Include(v => v.Groups).ThenInclude(g => g.Members)
            .AsSplitQuery().FirstOrDefaultAsync(v => v.Id == id);

    private TimetableVersion NewDraft(string name, DateOnly validFrom, TimetableVersion? source)
    {
        var schoolId = currentSchool.SchoolId!.Value;
        var divisions = (source?.Divisions ?? []).ToDictionary(d => d.Id, d => new GroupDivision
        {
            SchoolId = schoolId, Name = d.Name, HoursPerWeek = d.HoursPerWeek, DoubleLessons = d.DoubleLessons, ClassIds = [.. d.ClassIds],
            CoversSubjectId = d.CoversSubjectId,
        });
        var groups = (source?.Groups ?? []).ToDictionary(g => g.Id, g => new StudentGroup
        {
            SchoolId = schoolId, Name = g.Name, SubjectId = g.SubjectId, HoursPerWeek = g.HoursPerWeek, DoubleLessons = g.DoubleLessons,
            TeacherId = g.TeacherId, ClassroomId = g.ClassroomId,
            GroupDivision = g.GroupDivisionId is { } d ? divisions[d] : null,
            Classes = [.. g.Classes.Select(c => new StudentGroupClass { SchoolId = schoolId, SchoolClassId = c.SchoolClassId })],
            Members = [.. g.Members.Select(m => new StudentGroupMember { SchoolId = schoolId, StudentId = m.StudentId })],
        });
        return new TimetableVersion
        {
            SchoolId = schoolId,
            Name = name,
            ValidFrom = validFrom,
            Status = TimetableVersionStatus.Draft,
            CreatedAt = clock.GetUtcNow(),
            CreatedByUserId = User.UserId(),
            Entries = source?.Entries.Select(e => new TimetableEntry
            {
                SchoolId = schoolId,
                SchoolClassId = e.SchoolClassId,
                StudentGroup = e.StudentGroupId is { } g ? groups[g] : null,
                SubjectId = e.SubjectId,
                DayOfWeek = e.DayOfWeek,
                PeriodNumber = e.PeriodNumber,
                WeekParity = e.WeekParity,
                TeacherId = e.TeacherId,
                ClassroomId = e.ClassroomId,
                Locked = e.Locked,
            }).ToList() ?? [],
            Curriculum = source?.Curriculum.Select(c => new CurriculumItem
            {
                SchoolId = schoolId, Grade = c.Grade, SubjectId = c.SubjectId, HoursPerWeek = c.HoursPerWeek,
                DoubleLessons = c.DoubleLessons,
            }).ToList() ?? [],
            ClassCurriculum = source?.ClassCurriculum.Select(c => new ClassCurriculumItem
            {
                SchoolId = schoolId, SchoolClassId = c.SchoolClassId, SubjectId = c.SubjectId, HoursPerWeek = c.HoursPerWeek,
                DoubleLessons = c.DoubleLessons,
            }).ToList() ?? [],
            Assignments = source?.Assignments.Select(a => new TeachingAssignment
            {
                SchoolId = schoolId, SchoolClassId = a.SchoolClassId, SubjectId = a.SubjectId, TeacherId = a.TeacherId,
            }).ToList() ?? [],
            Divisions = [.. divisions.Values],
            Groups = [.. groups.Values],
        };
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
                if (version.ChangeNoticeOn is { } noticeOn)
                {
                    version.ChangeNoticeOn = noticeOn.AddDays(request.ValidFrom.DayNumber - version.ValidFrom.DayNumber);
                }
                else if (version.ChangeNoticeSentAt is not null)
                {
                    version.ChangeNoticeOn = Today;
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
    /// draft replaces the version valid until then. Those it concerns hear
    /// about it at the end of today, or of the day
    /// <see cref="PublishTimetableVersionRequest.NotifyDaysBefore"/> days before it starts.</summary>
    [HttpPost("{id:int}/publish")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> PublishVersion(int id, PublishTimetableVersionRequest? request = null)
    {
        if (request?.NotifyDaysBefore is < 1 or > MaxNotifyDaysBefore)
        {
            return BadRequest($"Upozornit lze nejvýše {MaxNotifyDaysBefore} dní předem.");
        }
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
        version.ChangeNoticeOn = request?.NotifyDaysBefore is { } days ? version.ValidFrom.AddDays(-days) : Today;
        await db.SaveChangesAsync();
        await db.RecomputeTimetableValidityAsync();
        await db.SaveChangesAsync();
        return Ok(ToDto(version));
    }

    /// <summary>A published version back among drafts — only while it doesn't
    /// apply yet, like archiving.</summary>
    [HttpPost("{id:int}/unpublish")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableVersionDto>> UnpublishVersion(int id)
    {
        var version = await db.TimetableVersions.Include(v => v.Entries).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null)
        {
            return NotFound();
        }
        if (version.Status != TimetableVersionStatus.Published)
        {
            return BadRequest("Zrušit zveřejnění jde jen u zveřejněné verze.");
        }
        if (version.ValidFrom <= Today)
        {
            return BadRequest("Verzi, která už platí nebo platila, nejde vrátit mezi koncepty.");
        }

        version.Status = TimetableVersionStatus.Draft;
        version.PublishedAt = null;
        version.PublishedByUserId = null;
        await new TimetableChangeNotice(db, clock).WithdrawAsync(version);
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
        await new TimetableChangeNotice(db, clock).WithdrawAsync(version);
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

public record EditFromDateRequest(DateOnly ValidFrom);

/// <param name="NotifyDaysBefore">Null = at the end of today.</param>
public record PublishTimetableVersionRequest(int? NotifyDaysBefore = null);
