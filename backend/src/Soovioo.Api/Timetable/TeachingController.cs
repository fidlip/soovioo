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
public class TeachingController(AppDbContext db, ICurrentSchoolContext currentSchool, TimeProvider clock) : ControllerBase
{
    /// <summary>For whoever may see it or edit it.</summary>
    [HttpGet("teaching")]
    public async Task<ActionResult<TeachingOverviewDto>> GetTeaching(int versionId)
    {
        if (await ScopeAsync() == PermissionScope.None)
        {
            return Forbid();
        }
        var version = await VisibleVersionAsync(versionId);
        return version is null ? NotFound() : Ok(await OverviewAsync(version));
    }

    /// <summary>One teacher's přidělení in <paramref name="versionId"/> (the
    /// version valid today by default) — for the teacher themselves in Rozvrh
    /// and Nastavení, for others to whoever sees the whole school's. No
    /// content when they don't teach there (or there's no version).</summary>
    [HttpGet("/api/teaching/teachers/{teacherId:int}")]
    public async Task<ActionResult<TeacherTeachingDto>> GetTeacherTeaching(int teacherId, [FromQuery] int? versionId = null)
    {
        if (teacherId != User.UserId() && await ScopeAsync() != PermissionScope.School)
        {
            return Forbid();
        }
        var version = versionId is { } id
            ? await VisibleVersionAsync(id)
            : await db.TimetableVersionOnAsync(SchoolClock.Today(clock));
        if (version is null)
        {
            return NoContent();
        }
        var (_, teachers, _, _) = await PlanAsync(version);
        return teachers.FirstOrDefault(t => t.Id == teacherId) is { } teacher
            ? Ok(new TeacherTeachingDto(version.Id, version.Name, teacher))
            : NoContent();
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
        if (items.Any(i => !ValidHours(i.HoursPerWeek)))
        {
            return BadRequest(HoursMessage);
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
            HoursPerWeek = i.HoursPerWeek, DoubleLessons = i.DoubleLessons,
        }));
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>The class's own hours of the subject; null = as its grade
    /// has (so do the grade's hours themselves), 0 = the class doesn't have it.</summary>
    [HttpPut("class-curriculum")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> SetClassCurriculum(int versionId, SetClassCurriculumRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }
        if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId))
        {
            return BadRequest("Neznámý předmět.");
        }
        if (request.HoursPerWeek is { } wanted && !ValidHours(wanted))
        {
            return BadRequest(HoursMessage);
        }

        var gradeHours = schoolClass.Rocnik is { } grade
            ? await db.CurriculumItems
                .Where(c => c.TimetableVersionId == version.Id && c.Grade == grade && c.SubjectId == request.SubjectId)
                .Select(c => (decimal?)c.HoursPerWeek).FirstOrDefaultAsync()
            : null;
        var item = await db.ClassCurriculumItems.FirstOrDefaultAsync(c =>
            c.TimetableVersionId == version.Id && c.SchoolClassId == schoolClass.Id && c.SubjectId == request.SubjectId);
        var hours = request.HoursPerWeek == (gradeHours ?? 0) ? null : request.HoursPerWeek;
        SetClassItem(version, schoolClass.Id, request.SubjectId, item, i => i.HoursPerWeek = hours);
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>Whether the class has the subject in blocks of two; null =
    /// as its grade (or the subject) has.</summary>
    [HttpPut("class-double-lessons")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<TeachingOverviewDto>> SetClassDoubleLessons(int versionId, SetClassDoubleLessonsRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }
        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Neznámý předmět.");
        }
        var gradeBlocks = (schoolClass.Rocnik is { } grade
            ? await db.CurriculumItems
                .Where(c => c.TimetableVersionId == version.Id && c.Grade == grade && c.SubjectId == subject.Id)
                .Select(c => c.DoubleLessons).FirstOrDefaultAsync()
            : null) ?? subject.DoubleLessons;
        var item = await db.ClassCurriculumItems.FirstOrDefaultAsync(c =>
            c.TimetableVersionId == version.Id && c.SchoolClassId == schoolClass.Id && c.SubjectId == subject.Id);
        var blocks = request.DoubleLessons == gradeBlocks ? null : request.DoubleLessons;
        SetClassItem(version, schoolClass.Id, subject.Id, item, i => i.DoubleLessons = blocks);
        await db.SaveChangesAsync();
        return Ok(await OverviewAsync(version));
    }

    /// <summary>A class's own plan row goes once it says nothing of its own.</summary>
    private void SetClassItem(
        TimetableVersion version, int classId, int subjectId, ClassCurriculumItem? item, Action<ClassCurriculumItem> change)
    {
        item ??= db.ClassCurriculumItems.Add(new ClassCurriculumItem
        {
            SchoolId = currentSchool.SchoolId!.Value, TimetableVersionId = version.Id,
            SchoolClassId = classId, SubjectId = subjectId,
        }).Entity;
        change(item);
        if (item.HoursPerWeek is null && item.DoubleLessons is null)
        {
            db.ClassCurriculumItems.Remove(item);
        }
    }

    private static bool ValidHours(decimal hours) => hours is >= 0 and <= 40 && hours * 2 == Math.Floor(hours * 2);

    private const string HoursMessage = "Hodiny týdně musí být od 0 do 40, po půlhodinách.";

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
    /// school has none yet: a grade gets the hours most of its classes have
    /// (a class that differs gets its own), a class without a ročník its own
    /// plan, a class and subject the teacher who has most of its lessons.</summary>
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
        var ownPlanned = await db.ClassCurriculumItems.Where(c => c.TimetableVersionId == version.Id).ToListAsync();
        var classes = await db.SchoolClasses.Select(c => new { c.Id, c.Rocnik }).ToListAsync();
        decimal HoursOf(int classId, int subjectId) => lessons
            .Where(l => l.SchoolClassId == classId && l.SubjectId == subjectId)
            .Sum(l => l.WeekParity == WeekParity.Every ? 1m : 0.5m);
        void AddOwn(int classId, int subjectId, decimal hours)
        {
            if (ownPlanned.FirstOrDefault(o => o.SchoolClassId == classId && o.SubjectId == subjectId) is { } existing)
            {
                existing.HoursPerWeek ??= hours;
            }
            else
            {
                db.ClassCurriculumItems.Add(new ClassCurriculumItem
                {
                    SchoolId = schoolId, TimetableVersionId = version.Id, SchoolClassId = classId, SubjectId = subjectId,
                    HoursPerWeek = hours,
                });
            }
        }

        foreach (var (grade, subjectId) in lessons.Where(l => l.Rocnik is not null)
                     .Select(l => (Grade: l.Rocnik!.Value, l.SubjectId)).Distinct()
                     .Where(g => !planned.Any(p => p.Grade == g.Grade && p.SubjectId == g.SubjectId)))
        {
            var inGrade = classes.Where(c => c.Rocnik == grade)
                .Select(c => (ClassId: c.Id, Hours: HoursOf(c.Id, subjectId))).ToList();
            var hours = inGrade.GroupBy(x => x.Hours).OrderByDescending(h => h.Count()).ThenByDescending(h => h.Key).First().Key;
            if (hours > 0)
            {
                db.CurriculumItems.Add(new CurriculumItem
                {
                    SchoolId = schoolId, TimetableVersionId = version.Id, Grade = grade, SubjectId = subjectId, HoursPerWeek = hours,
                });
            }
            foreach (var (classId, classHours) in inGrade.Where(x => x.Hours != hours))
            {
                AddOwn(classId, subjectId, classHours);
            }
        }
        foreach (var (classId, subjectId) in lessons.Where(l => l.Rocnik is null)
                     .Select(l => (l.SchoolClassId, l.SubjectId)).Distinct())
        {
            AddOwn(classId, subjectId, HoursOf(classId, subjectId));
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

    private async Task<PermissionScope> ScopeAsync() =>
        await User.HasPermissionAsync(db, Permission.Teaching)
            ? PermissionScope.School
            : await User.ScopeAsync(db, Permission.TeachingRead);

    private async Task<TeachingOverviewDto> OverviewAsync(TimetableVersion version)
    {
        var (classDtos, teacherDtos, grades, curriculum) = await PlanAsync(version);
        if (await ScopeAsync() != PermissionScope.School)
        {
            var me = User.UserId();
            classDtos = classDtos
                .Select(c => c with { Problems = [], Rows = c.Rows.Where(r => r.TeacherId == me).ToList() })
                .Where(c => c.Rows.Count > 0)
                .ToList();
            teacherDtos = teacherDtos.Where(t => t.Id == me).ToList();
        }
        return new TeachingOverviewDto(
            version.Id,
            version.Status != TimetableVersionStatus.Archived && await User.HasPermissionAsync(db, Permission.Teaching),
            grades, curriculum, classDtos, teacherDtos);
    }

    private Task<TeachingPlan> PlanAsync(TimetableVersion version) => TeachingPlan.LoadAsync(db, version);
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

public record TeacherTeachingDto(int VersionId, string VersionName, TeachingTeacherDto Teacher);

public record SaveCurriculumRequest(IReadOnlyList<CurriculumItemDto> Items);

/// <param name="HoursPerWeek">Null = as the class's grade has; 0 = not in the class.</param>
public record SetClassCurriculumRequest(int SchoolClassId, int SubjectId, decimal? HoursPerWeek);

/// <param name="DoubleLessons">Null = as the grade.</param>
public record SetClassDoubleLessonsRequest(int SchoolClassId, int SubjectId, bool? DoubleLessons);

/// <param name="TeacherId">Null = chosen automatically.</param>
public record SetAssignmentRequest(int SchoolClassId, int SubjectId, int? TeacherId);
