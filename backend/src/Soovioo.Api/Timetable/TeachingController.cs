using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Učební plán and přidělení výuky of a timetable version, with what
/// doesn't add up (see <see cref="TeachingPlanner"/>). A teacher with
/// <see cref="PermissionScope.Own"/> sees only what they teach.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions/{versionId:int}")]
public class TeachingController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet("teaching")]
    [RequirePermission(Permission.TeachingRead)]
    public async Task<ActionResult<TeachingOverviewDto>> GetTeaching(int versionId)
    {
        var version = await VisibleVersionAsync(versionId);
        return version is null ? NotFound() : Ok(await OverviewAsync(version));
    }

    /// <summary>Replaces the version's whole učební plán.</summary>
    [HttpPut("curriculum")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> SaveCurriculum(int versionId, SaveCurriculumRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var items = request.Items.Where(i => i.HoursPerWeek != 0).ToList();
        if (items.Any(i => i.Grade is < 1 or > 13))
        {
            return BadRequest("Ročník musí být od 1 do 13.");
        }
        if (items.Any(i => i.HoursPerWeek is < 0 or > 40 || i.HoursPerWeek * 2 != Math.Floor(i.HoursPerWeek * 2)))
        {
            return BadRequest("Hodiny týdně musí být od 0,5 do 40, po půlhodinách.");
        }
        if (items.GroupBy(i => (i.Grade, i.SubjectId)).Any(g => g.Count() > 1))
        {
            return BadRequest("Předmět je v ročníku v plánu dvakrát.");
        }
        var subjectIds = items.Select(i => i.SubjectId).Distinct().ToList();
        if (await db.Subjects.CountAsync(s => subjectIds.Contains(s.Id)) != subjectIds.Count)
        {
            return BadRequest("Neznámý předmět.");
        }

        var schoolId = currentSchool.SchoolId!.Value;
        db.CurriculumItems.RemoveRange(await db.CurriculumItems.Where(c => c.TimetableVersionId == version.Id).ToListAsync());
        db.CurriculumItems.AddRange(items.Select(i => new CurriculumItem
        {
            SchoolId = schoolId, TimetableVersionId = version.Id, Grade = i.Grade, SubjectId = i.SubjectId,
            HoursPerWeek = i.HoursPerWeek,
        }));
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>Who teaches the subject in the class; no teacher = chosen
    /// automatically again.</summary>
    [HttpPut("assignments")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> SetAssignment(int versionId, SetAssignmentRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        if (!await db.SchoolClasses.AnyAsync(c => c.Id == request.SchoolClassId))
        {
            return BadRequest("Neznámá třída.");
        }
        if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId))
        {
            return BadRequest("Neznámý předmět.");
        }
        if (request.TeacherId is { } teacherId
            && !await db.Users.AnyAsync(u => u.Id == teacherId && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)))
        {
            return BadRequest("Neznámý učitel.");
        }

        var assignment = await db.TeachingAssignments.FirstOrDefaultAsync(a =>
            a.TimetableVersionId == version.Id && a.SchoolClassId == request.SchoolClassId && a.SubjectId == request.SubjectId);
        if (request.TeacherId is null)
        {
            if (assignment is not null)
            {
                db.TeachingAssignments.Remove(assignment);
            }
        }
        else if (assignment is null)
        {
            db.TeachingAssignments.Add(new TeachingAssignment
            {
                SchoolId = currentSchool.SchoolId!.Value, TimetableVersionId = version.Id,
                SchoolClassId = request.SchoolClassId, SubjectId = request.SubjectId, TeacherId = request.TeacherId.Value,
            });
        }
        else
        {
            assignment.TeacherId = request.TeacherId.Value;
        }
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>Every automatic choice becomes the school's own, so it stays
    /// whatever changes later.</summary>
    [HttpPost("assignments/confirm")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> ConfirmAssignments(int versionId)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var overview = await OverviewAsync(version);
        AddAssignments(version, overview.Classes.SelectMany(c => c.Rows
            .Where(r => r.Automatic && r.TeacherId is not null)
            .Select(r => (c.Id, r.SubjectId, r.TeacherId!.Value))));
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>Učební plán and přidělení from the version's lessons where the
    /// school has none yet: a grade gets the hours most of its classes have,
    /// a class and subject the teacher who has most of its lessons.</summary>
    [HttpPost("teaching/import")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> ImportFromTimetable(int versionId)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var schoolId = currentSchool.SchoolId!.Value;
        var lessons = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
            .Select(e => new { e.SchoolClassId, e.SchoolClass!.Rocnik, e.SubjectId, e.TeacherId, e.WeekParity })
            .ToListAsync();
        var planned = await db.CurriculumItems.Where(c => c.TimetableVersionId == version.Id)
            .Select(c => new { c.Grade, c.SubjectId }).ToListAsync();
        var perClass = lessons.Where(l => l.Rocnik is not null)
            .GroupBy(l => (Grade: l.Rocnik!.Value, l.SubjectId, l.SchoolClassId))
            .Select(g => (g.Key.Grade, g.Key.SubjectId, Hours: g.Sum(l => l.WeekParity == WeekParity.Every ? 1m : 0.5m)));
        foreach (var grade in perClass.GroupBy(x => (x.Grade, x.SubjectId))
                     .Where(g => !planned.Any(p => p.Grade == g.Key.Grade && p.SubjectId == g.Key.SubjectId)))
        {
            db.CurriculumItems.Add(new CurriculumItem
            {
                SchoolId = schoolId, TimetableVersionId = version.Id, Grade = grade.Key.Grade, SubjectId = grade.Key.SubjectId,
                HoursPerWeek = grade.GroupBy(x => x.Hours).OrderByDescending(h => h.Count()).ThenByDescending(h => h.Key).First().Key,
            });
        }
        AddAssignments(version, lessons.Where(l => l.TeacherId is not null)
            .GroupBy(l => (l.SchoolClassId, l.SubjectId))
            .Select(g => (g.Key.SchoolClassId, g.Key.SubjectId, g
                .GroupBy(l => l.TeacherId!.Value)
                .OrderByDescending(t => t.Sum(l => l.WeekParity == WeekParity.Every ? 1m : 0.5m)).ThenBy(t => t.Key)
                .First().Key)));
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    private void AddAssignments(TimetableVersion version, IEnumerable<(int SchoolClassId, int SubjectId, int TeacherId)> wanted)
    {
        var existing = db.TeachingAssignments.Where(a => a.TimetableVersionId == version.Id)
            .Select(a => new { a.SchoolClassId, a.SubjectId }).ToList();
        foreach (var (classId, subjectId, teacherId) in wanted)
        {
            if (!existing.Any(e => e.SchoolClassId == classId && e.SubjectId == subjectId))
            {
                db.TeachingAssignments.Add(new TeachingAssignment
                {
                    SchoolId = currentSchool.SchoolId!.Value, TimetableVersionId = version.Id,
                    SchoolClassId = classId, SubjectId = subjectId, TeacherId = teacherId,
                });
            }
        }
    }

    private async Task<TimetableVersion?> VisibleVersionAsync(int versionId)
    {
        var version = await db.TimetableVersions.FindAsync(versionId);
        return version is not null
            && (version.Status == TimetableVersionStatus.Published || await User.SeesAllTimetableVersionsAsync(db))
                ? version
                : null;
    }

    private async Task<(TimetableVersion? Version, ActionResult? Error)> EditableVersionAsync(int versionId)
    {
        var version = await db.TimetableVersions.FindAsync(versionId);
        return version is null ? (null, NotFound())
            : version.Status == TimetableVersionStatus.Archived ? (null, BadRequest("Archivovanou verzi rozvrhu nejde upravovat."))
            : (version, null);
    }

    private async Task<TeachingOverviewDto> OverviewAsync(TimetableVersion version)
    {
        var classes = await db.SchoolClasses.OrderBy(c => c.Name)
            .Select(c => new TeachingPlanner.ClassInput(c.Id, c.Name, c.Rocnik, c.ClassTeacherId))
            .ToListAsync();
        var subjects = await db.Subjects.OrderBy(s => s.Name)
            .Select(s => new TeachingPlanner.SubjectInput(s.Id, s.Name, s.Abbreviation))
            .ToListAsync();
        var curriculum = await db.CurriculumItems.Where(c => c.TimetableVersionId == version.Id)
            .OrderBy(c => c.Grade).ThenBy(c => c.SubjectId)
            .Select(c => new CurriculumItemDto(c.Grade, c.SubjectId, c.HoursPerWeek))
            .ToListAsync();
        var assignments = (await db.TeachingAssignments.Where(a => a.TimetableVersionId == version.Id)
                .Select(a => new { a.SchoolClassId, a.SubjectId, a.TeacherId })
                .ToListAsync())
            .Select(a => (a.SchoolClassId, a.SubjectId, a.TeacherId))
            .ToList();
        var lessons = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
            .Select(e => new TeachingPlanner.LessonInput(e.SchoolClassId, e.SubjectId, e.TeacherId, e.DayOfWeek, e.WeekParity))
            .ToListAsync();

        var involved = assignments.Select(a => a.TeacherId)
            .Concat(lessons.Where(l => l.TeacherId is not null).Select(l => l.TeacherId!.Value))
            .Distinct().ToList();
        var users = await db.Users
            .Include(u => u.Person).ThenInclude(p => p!.TeacherProfile).ThenInclude(t => t!.Qualifications).ThenInclude(q => q.Subject)
            .Where(u => involved.Contains(u.Id)
                || (u.DeactivatedAt == null && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)))
            .AsSplitQuery()
            .ToListAsync();
        var teachers = users.Select(u =>
        {
            var profile = u.Person?.TeacherProfile;
            return new TeachingPlanner.TeacherInput(
                u.Id, PersonNames.Display(u),
                u.DeactivatedAt == null && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher),
                profile?.Qualifications ?? [], profile?.Describe(), profile?.ContractedHours,
                profile?.TeachingDays ?? [.. TeacherProfile.Weekdays], profile is not null);
        }).ToList();

        var (classDtos, teacherDtos) = TeachingPlanner.Plan(new TeachingPlanner.Input(
            classes, subjects, teachers, curriculum, assignments, lessons));

        var scope = await User.HasPermissionAsync(db, Permission.Teaching)
            ? PermissionScope.School
            : await User.ScopeAsync(db, Permission.TeachingRead);
        if (scope != PermissionScope.School)
        {
            var me = User.UserId();
            classDtos = classDtos
                .Select(c => c with { Problems = [], Rows = c.Rows.Where(r => r.TeacherId == me).ToList() })
                .Where(c => c.Rows.Count > 0)
                .ToList();
            teacherDtos = teacherDtos.Where(t => t.Id == me).ToList();
        }

        var grades = classes.Where(c => c.Grade is not null).Select(c => c.Grade!.Value)
            .Concat(curriculum.Select(c => c.Grade)).Distinct().Order().ToList();
        return new TeachingOverviewDto(
            version.Id,
            version.Status != TimetableVersionStatus.Archived && await User.HasPermissionAsync(db, Permission.Teaching),
            grades, curriculum, classDtos, teacherDtos);
    }
}

/// <param name="CanEdit">The caller may change the plan and přidělení of
/// this version (not an archived one).</param>
/// <param name="Grades">Ročníky of the school's classes and of the plan.</param>
public record TeachingOverviewDto(
    int VersionId,
    bool CanEdit,
    IReadOnlyList<int> Grades,
    IReadOnlyList<CurriculumItemDto> Curriculum,
    IReadOnlyList<TeachingClassDto> Classes,
    IReadOnlyList<TeachingTeacherDto> Teachers);

public record SaveCurriculumRequest(IReadOnlyList<CurriculumItemDto> Items);

/// <param name="TeacherId">Null = chosen automatically.</param>
public record SetAssignmentRequest(int SchoolClassId, int SubjectId, int? TeacherId);
