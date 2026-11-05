using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Groups of pupils and divisions of a timetable version (see
/// <see cref="StudentGroup"/>). A change of pupils that would put one in two
/// lessons at once, or a group in a classroom it doesn't fit in, is refused.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions/{versionId:int}")]
public class StudentGroupsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet("groups")]
    public async Task<ActionResult<GroupsDto>> GetGroups(int versionId)
    {
        var scope = await User.HasPermissionAsync(db, Permission.Teaching)
            ? PermissionScope.School
            : await User.ScopeAsync(db, Permission.TeachingRead);
        if (scope == PermissionScope.None)
        {
            return Forbid();
        }
        var version = await db.TimetableVersions.FindAsync(versionId);
        if (version is null || (version.Status != TimetableVersionStatus.Published && !await User.SeesAllTimetableVersionsAsync(db)))
        {
            return NotFound();
        }
        var groups = await db.StudentGroups.Where(g => g.TimetableVersionId == versionId)
            .OrderBy(g => g.Name)
            .Select(g => new StudentGroupDto(
                g.Id, g.Name, g.GroupDivisionId, g.SubjectId, g.HoursPerWeek, g.DoubleLessons, g.TeacherId, g.ClassroomId,
                g.Classes.Select(c => c.SchoolClassId).ToList(), g.Members.Select(m => m.StudentId).ToList()))
            .ToListAsync();
        if (scope != PermissionScope.School)
        {
            var me = User.UserId();
            groups = [.. groups.Where(g => g.TeacherId == me)];
        }
        var divisions = await db.GroupDivisions.Where(d => d.TimetableVersionId == versionId)
            .OrderBy(d => d.Name)
            .Select(d => new GroupDivisionDto(d.Id, d.Name, d.HoursPerWeek, d.DoubleLessons, d.ClassIds))
            .ToListAsync();
        if (scope != PermissionScope.School)
        {
            divisions = [.. divisions.Where(d => groups.Any(g => g.DivisionId == d.Id))];
        }
        return Ok(new GroupsDto(divisions, groups));
    }

    [HttpPost("divisions")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<GroupDivisionDto>> CreateDivision(int versionId, SaveGroupDivisionRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        if (await DivisionErrorAsync(request) is { } problem)
        {
            return BadRequest(problem);
        }
        var division = new GroupDivision
        {
            SchoolId = currentSchool.SchoolId!.Value, TimetableVersionId = version.Id, Name = request.Name.Trim(),
            HoursPerWeek = request.HoursPerWeek, DoubleLessons = request.DoubleLessons, ClassIds = [.. request.ClassIds.Distinct()],
        };
        db.GroupDivisions.Add(division);
        await db.SaveChangesAsync();
        return Ok(new GroupDivisionDto(division.Id, division.Name, division.HoursPerWeek, division.DoubleLessons, request.ClassIds));
    }

    /// <summary>Its groups follow: the hours, the blocks and the classes —
    /// pupils of a class left out leave the groups too.</summary>
    [HttpPut("divisions/{id:int}")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<GroupDivisionDto>> UpdateDivision(int versionId, int id, SaveGroupDivisionRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var division = await db.GroupDivisions.FirstOrDefaultAsync(d => d.Id == id && d.TimetableVersionId == versionId);
        if (division is null)
        {
            return NotFound();
        }
        if (await DivisionErrorAsync(request) is { } problem)
        {
            return BadRequest(problem);
        }
        division.Name = request.Name.Trim();
        division.HoursPerWeek = request.HoursPerWeek;
        division.DoubleLessons = request.DoubleLessons;
        division.ClassIds = [.. request.ClassIds.Distinct()];
        var groups = await db.StudentGroups.Include(g => g.Classes).Include(g => g.Members).ThenInclude(m => m.Student)
            .Where(g => g.GroupDivisionId == id).ToListAsync();
        foreach (var group in groups)
        {
            group.HoursPerWeek = request.HoursPerWeek;
            group.DoubleLessons = request.DoubleLessons;
            await SetClassesAsync(group, request.ClassIds);
            group.Members.RemoveAll(m => !request.ClassIds.Contains(m.Student!.SchoolClassId));
        }
        await db.SaveChangesAsync();
        return Ok(new GroupDivisionDto(division.Id, division.Name, division.HoursPerWeek, division.DoubleLessons, request.ClassIds));
    }

    /// <summary>With its groups and their lessons; not one with a group with homework.</summary>
    [HttpDelete("divisions/{id:int}")]
    [RequirePermission(Permission.Teaching)]
    public async Task<IActionResult> DeleteDivision(int versionId, int id)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var division = await db.GroupDivisions.FirstOrDefaultAsync(d => d.Id == id && d.TimetableVersionId == versionId);
        if (division is null)
        {
            return NotFound();
        }
        var groupIds = await db.StudentGroups.Where(g => g.GroupDivisionId == id).Select(g => g.Id).ToListAsync();
        if (await db.Homework.AnyAsync(h => h.StudentGroupId != null && groupIds.Contains(h.StudentGroupId.Value)))
        {
            return BadRequest(HasHomework);
        }
        db.TimetableEntries.RemoveRange(db.TimetableEntries.Where(e => e.StudentGroupId != null && groupIds.Contains(e.StudentGroupId.Value)));
        db.StudentGroups.RemoveRange(db.StudentGroups.Where(g => g.GroupDivisionId == id));
        db.GroupDivisions.Remove(division);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("groups")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<StudentGroupDto>> CreateGroup(int versionId, SaveStudentGroupRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var group = new StudentGroup
        {
            SchoolId = currentSchool.SchoolId!.Value, TimetableVersionId = version.Id, Name = "",
        };
        if (await SaveAsync(group, request) is { } problem)
        {
            return BadRequest(problem);
        }
        db.StudentGroups.Add(group);
        await db.SaveChangesAsync();
        return Ok(ToDto(group));
    }

    [HttpPut("groups/{id:int}")]
    [RequirePermission(Permission.Teaching)]
    public async Task<ActionResult<StudentGroupDto>> UpdateGroup(int versionId, int id, SaveStudentGroupRequest request)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var group = await db.StudentGroups.Include(g => g.Classes).Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id && g.TimetableVersionId == versionId);
        if (group is null)
        {
            return NotFound();
        }
        if (request.DivisionId != group.GroupDivisionId)
        {
            return BadRequest("Skupinu nejde přesunout do jiného dělení.");
        }
        if (await SaveAsync(group, request) is { } problem)
        {
            return BadRequest(problem);
        }
        await db.SaveChangesAsync();
        return Ok(ToDto(group));
    }

    /// <summary>With its lessons; not one with homework.</summary>
    [HttpDelete("groups/{id:int}")]
    [RequirePermission(Permission.Teaching)]
    public async Task<IActionResult> DeleteGroup(int versionId, int id)
    {
        var (version, error) = await EditableVersionAsync(versionId);
        if (version is null)
        {
            return error!;
        }
        var group = await db.StudentGroups.FirstOrDefaultAsync(g => g.Id == id && g.TimetableVersionId == versionId);
        if (group is null)
        {
            return NotFound();
        }
        if (await db.Homework.AnyAsync(h => h.StudentGroupId == id))
        {
            return BadRequest(HasHomework);
        }
        db.TimetableEntries.RemoveRange(db.TimetableEntries.Where(e => e.StudentGroupId == id));
        db.StudentGroups.Remove(group);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private const string HasHomework = "Skupina má domácí úkoly, smazat ji nejde.";

    private async Task<string?> DivisionErrorAsync(SaveGroupDivisionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Zadejte název dělení.";
        }
        if (request.HoursPerWeek is { } hours && !ValidHours(hours))
        {
            return HoursMessage;
        }
        if (request.ClassIds.Count == 0)
        {
            return "Vyberte aspoň jednu třídu.";
        }
        var classIds = request.ClassIds.Distinct().ToList();
        return await db.SchoolClasses.CountAsync(c => classIds.Contains(c.Id)) != classIds.Count ? "Neznámá třída." : null;
    }

    /// <summary>Takes the request into the group, or says why not.</summary>
    private async Task<string?> SaveAsync(StudentGroup group, SaveStudentGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Zadejte název skupiny.";
        }
        if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId))
        {
            return "Neznámý předmět.";
        }
        if (request.TeacherId is { } teacherId
            && !await db.Users.AnyAsync(u => u.Id == teacherId && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)))
        {
            return "Neznámý učitel.";
        }
        if (request.ClassroomId is { } roomId && !await db.Classrooms.AnyAsync(r => r.Id == roomId))
        {
            return "Neznámá učebna.";
        }

        GroupDivision? division = null;
        List<int> classIds;
        decimal? hours;
        bool? blocks;
        if (request.DivisionId is { } divisionId)
        {
            division = await db.GroupDivisions.FirstOrDefaultAsync(d => d.Id == divisionId && d.TimetableVersionId == group.TimetableVersionId);
            if (division is null)
            {
                return "Neznámé dělení.";
            }
            classIds = division.ClassIds;
            hours = division.HoursPerWeek;
            blocks = division.DoubleLessons;
        }
        else
        {
            classIds = [.. request.ClassIds.Distinct()];
            hours = request.HoursPerWeek;
            blocks = request.DoubleLessons;
            if (hours is { } own && !ValidHours(own))
            {
                return HoursMessage;
            }
        }
        if (classIds.Count == 0)
        {
            return "Vyberte aspoň jednu třídu.";
        }
        if (await db.SchoolClasses.CountAsync(c => classIds.Contains(c.Id)) != classIds.Count)
        {
            return "Neznámá třída.";
        }

        var studentIds = request.StudentIds.Distinct().ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id))
            .Select(s => new Pupil(s.Id, s.SchoolClassId, s.Person!.FirstName, s.Person.LastName))
            .ToListAsync();
        if (students.Count != studentIds.Count)
        {
            return "Neznámý žák.";
        }
        if (students.FirstOrDefault(s => !classIds.Contains(s.ClassId)) is { } outside)
        {
            return $"{outside.Name} není v žádné třídě skupiny.";
        }

        // A lesson the group has, with its classroom.
        var lessons = group.Id == 0 ? [] : await db.TimetableEntries
            .Where(e => e.StudentGroupId == group.Id)
            .Select(e => new { e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.Classroom })
            .ToListAsync();
        var slots = lessons.DistinctBy(l => (l.DayOfWeek, l.PeriodNumber, l.WeekParity)).ToList();
        // Only pupils joining it are checked: what's there already was.
        var joining = students.Where(s => group.Members.All(m => m.StudentId != s.Id)).ToList();
        if (division is not null && joining.Count > 0)
        {
            var joiningIds = joining.Select(s => s.Id).ToList();
            var taken = await db.StudentGroupMembers
                .Where(m => m.StudentGroup!.GroupDivisionId == division.Id && m.StudentGroupId != group.Id && joiningIds.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.StudentGroup!.Name })
                .FirstOrDefaultAsync();
            if (taken is not null)
            {
                return $"{joining.First(s => s.Id == taken.StudentId).Name} už je ve skupině {taken.Name} tohoto dělení.";
            }
        }
        if (joining.Count > 0 && slots.FirstOrDefault(l => l.Classroom?.Capacity < students.Count) is { } small)
        {
            return $"Skupina by měla {students.Count} žáků, učebna {small.Classroom!.ShortName ?? small.Classroom.Name} má kapacitu {small.Classroom.Capacity} ({TimetableCheckSlot(small.DayOfWeek, small.PeriodNumber)}). Změňte nejdřív učebnu hodiny.";
        }

        // Pupils who'd have another lesson then: of their class, or of
        // another group of theirs (not one of this division).
        if (slots.Count > 0 && joining.Count > 0)
        {
            var siblings = division is null ? [] : await db.StudentGroups.Where(g => g.GroupDivisionId == division.Id).Select(g => g.Id).ToListAsync();
            var days = slots.Select(s => s.DayOfWeek).Distinct().ToList();
            var others = await db.TimetableEntries
                .Where(e => e.TimetableVersionId == group.TimetableVersionId && e.StudentGroupId != group.Id && days.Contains(e.DayOfWeek))
                .Where(e => e.StudentGroupId == null || !siblings.Contains(e.StudentGroupId.Value))
                .Select(e => new
                {
                    e.SchoolClassId, e.StudentGroupId, e.DayOfWeek, e.PeriodNumber, e.WeekParity,
                    Name = e.StudentGroup == null ? e.SchoolClass!.Name : e.StudentGroup.Name,
                    Subject = e.Subject!.Abbreviation ?? e.Subject.Name,
                    Members = e.StudentGroup == null ? new List<int>() : e.StudentGroup.Members.Select(m => m.StudentId).ToList(),
                })
                .ToListAsync();
            foreach (var student in joining)
            {
                var clash = others.FirstOrDefault(o =>
                    slots.Any(s => s.DayOfWeek == o.DayOfWeek && s.PeriodNumber == o.PeriodNumber
                        && (s.WeekParity == WeekParity.Every || o.WeekParity == WeekParity.Every || s.WeekParity == o.WeekParity))
                    && (o.StudentGroupId is null ? o.SchoolClassId == student.ClassId : o.Members.Contains(student.Id)));
                if (clash is not null)
                {
                    return $"{student.Name} má {TimetableCheckSlot(clash.DayOfWeek, clash.PeriodNumber)} hodinu {clash.Name} {clash.Subject}, kdy má skupina hodinu. Upravte nejdřív rozvrh.";
                }
            }
        }

        if (group.SubjectId != 0 && group.SubjectId != request.SubjectId)
        {
            foreach (var entry in await db.TimetableEntries.Where(e => e.StudentGroupId == group.Id).ToListAsync())
            {
                entry.SubjectId = request.SubjectId;
            }
        }
        group.Name = request.Name.Trim();
        group.SubjectId = request.SubjectId;
        group.HoursPerWeek = hours;
        group.DoubleLessons = blocks;
        group.TeacherId = request.TeacherId;
        group.ClassroomId = request.ClassroomId;
        group.GroupDivisionId = division?.Id;
        await SetClassesAsync(group, classIds);

        group.Members.RemoveAll(m => !studentIds.Contains(m.StudentId));
        foreach (var id in studentIds.Where(id => group.Members.All(m => m.StudentId != id)))
        {
            group.Members.Add(new StudentGroupMember { SchoolId = group.SchoolId, StudentId = id });
        }
        return null;
    }

    /// <summary>The group's lessons follow: a lesson of a class added for
    /// it, of a class left out gone.</summary>
    private async Task SetClassesAsync(StudentGroup group, IReadOnlyList<int> classIds)
    {
        var removed = group.Classes.Where(c => !classIds.Contains(c.SchoolClassId)).ToList();
        var added = classIds.Where(id => group.Classes.All(c => c.SchoolClassId != id)).ToList();
        foreach (var c in removed)
        {
            group.Classes.Remove(c);
        }
        foreach (var id in added)
        {
            group.Classes.Add(new StudentGroupClass { SchoolId = group.SchoolId, SchoolClassId = id });
        }
        if (group.Id == 0 || (removed.Count == 0 && added.Count == 0))
        {
            return;
        }
        var entries = await db.TimetableEntries.Where(e => e.StudentGroupId == group.Id).ToListAsync();
        db.TimetableEntries.RemoveRange(entries.Where(e => !classIds.Contains(e.SchoolClassId)));
        foreach (var lesson in entries.DistinctBy(e => (e.DayOfWeek, e.PeriodNumber, e.WeekParity)))
        {
            db.TimetableEntries.AddRange(added.Select(classId => new TimetableEntry
            {
                SchoolId = lesson.SchoolId, TimetableVersionId = lesson.TimetableVersionId, SchoolClassId = classId,
                StudentGroupId = group.Id, SubjectId = lesson.SubjectId, DayOfWeek = lesson.DayOfWeek,
                PeriodNumber = lesson.PeriodNumber, WeekParity = lesson.WeekParity, TeacherId = lesson.TeacherId,
                ClassroomId = lesson.ClassroomId, Locked = lesson.Locked,
            }));
        }
    }

    private static string TimetableCheckSlot(DayOfWeek day, int period) => $"{TeachingPlanner.OnDay(day)} {period}. hod.";

    private record Pupil(int Id, int ClassId, string FirstName, string LastName)
    {
        public string Name => $"{FirstName} {LastName}".Trim();
    }

    private static StudentGroupDto ToDto(StudentGroup g) => new(
        g.Id, g.Name, g.GroupDivisionId, g.SubjectId, g.HoursPerWeek, g.DoubleLessons, g.TeacherId, g.ClassroomId,
        [.. g.Classes.Select(c => c.SchoolClassId)], [.. g.Members.Select(m => m.StudentId)]);

    private static bool ValidHours(decimal hours) => hours is >= 0 and <= 40 && hours * 2 == Math.Floor(hours * 2);

    private const string HoursMessage = "Hodiny týdně musí být od 0 do 40, po půlhodinách.";

    private async Task<(TimetableVersion? Version, ActionResult? Error)> EditableVersionAsync(int versionId)
    {
        var version = await db.TimetableVersions.FindAsync(versionId);
        return version is null ? (null, NotFound())
            : version.Status == TimetableVersionStatus.Archived ? (null, BadRequest("Archivovanou verzi rozvrhu nejde upravovat."))
            : (version, null);
    }
}

/// <param name="ClassIds">Of its groups.</param>
/// <param name="HoursPerWeek">Null = as the učební plán.</param>
public record GroupDivisionDto(int Id, string Name, decimal? HoursPerWeek, bool? DoubleLessons, IReadOnlyList<int> ClassIds);

public record StudentGroupDto(
    int Id, string Name, int? DivisionId, int SubjectId, decimal? HoursPerWeek, bool? DoubleLessons, int? TeacherId,
    int? ClassroomId, IReadOnlyList<int> ClassIds, IReadOnlyList<int> StudentIds);

public record GroupsDto(IReadOnlyList<GroupDivisionDto> Divisions, IReadOnlyList<StudentGroupDto> Groups);

/// <param name="ClassIds">Classes of its groups.</param>
/// <param name="HoursPerWeek">Null = as the učební plán of its classes has its groups' subjects.</param>
public record SaveGroupDivisionRequest(string Name, decimal? HoursPerWeek, bool? DoubleLessons, IReadOnlyList<int> ClassIds);

/// <param name="HoursPerWeek">Null = as the učební plán of its classes; ignored in a division: the division's.</param>
/// <param name="ClassIds">Ignored in a division: the division's.</param>
/// <param name="TeacherId">Null = chosen automatically.</param>
/// <param name="ClassroomId">Null = from the subject's classrooms.</param>
public record SaveStudentGroupRequest(
    string Name,
    int SubjectId,
    decimal? HoursPerWeek,
    bool? DoubleLessons,
    int? TeacherId,
    int? ClassroomId,
    int? DivisionId,
    IReadOnlyList<int> ClassIds,
    IReadOnlyList<int> StudentIds);
