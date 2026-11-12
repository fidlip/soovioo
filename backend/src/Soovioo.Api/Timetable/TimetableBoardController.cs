using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Arranging a draft's lessons by hand: where one can go, moving it,
/// the lessons the plan wants and the timetable lacks, and how the timetable
/// looks. A group's lesson moves in all its classes, a division's groups
/// together; a locked lesson doesn't move.</summary>
[Authorize]
[ApiController]
[Route("api/timetable-versions/{versionId:int}/board")]
[AuditArea("Rozvrh")]
public class TimetableBoardController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    private const string LockedLesson = "Zamčenou hodinu nejde přesunout.";
    private const string NotPlanned = "Hodina je navíc oproti učebnímu plánu, mezi neumístěné nepatří. Smažte ji křížkem.";
    private const string UnknownLesson = "Tato hodina v plánu chybět nemá.";

    /// <summary>One group, or the groups of a division.</summary>
    private record Part(int? GroupId, int? TeacherId, IReadOnlyList<int> ClassIds, int SubjectId);

    [HttpGet("unplaced")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<IEnumerable<UnplacedLessonDto>>> GetUnplaced(int versionId, int schoolClassId)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        return Ok(Unplaced(await TeachingPlan.LoadAsync(db, version), schoolClassId));
    }

    private static List<UnplacedLessonDto> Unplaced(TeachingPlan plan, int schoolClassId)
    {
        static IEnumerable<WeekParity> Tiles(decimal missing) =>
            Enumerable.Repeat(WeekParity.Every, (int)Math.Max(0, Math.Floor(missing)))
                .Concat(missing - Math.Floor(missing) >= 0.5m ? [WeekParity.Odd] : []);
        var whole = plan.Classes.Where(c => c.Id == schoolClassId)
            .SelectMany(c => c.Rows)
            .Where(r => r.PlanHours > 0 && r.Groups.Count == 0)
            .SelectMany(r => Tiles(r.PlanHours!.Value - r.TimetableHours).Select(parity => new UnplacedLessonDto(
                r.SubjectId, r.SubjectAbbreviation ?? r.SubjectName, r.SubjectName, null, null, r.TeacherName, parity)));
        var groups = plan.Groups.Where(g => g.ClassIds.Contains(schoolClassId))
            .GroupBy(g => g.DivisionId is { } d ? -d : g.Id)
            .SelectMany(together =>
            {
                var lead = together.First();
                var teachers = string.Join(", ", together.Select(g => g.TeacherName).Where(n => n is not null));
                return Tiles(lead.PlanHours - lead.TimetableHours).Select(parity => new UnplacedLessonDto(
                    lead.SubjectId, lead.SubjectAbbreviation ?? lead.SubjectName, lead.SubjectName, lead.Id,
                    lead.DivisionName ?? lead.Name, teachers.Length > 0 ? teachers : null, parity));
            });
        return [.. whole.Concat(groups)];
    }

    /// <summary>The slots for a lesson in the timetable, or for one from the
    /// unplaced ones.</summary>
    [HttpGet("slots")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<IEnumerable<DropSlot>>> GetSlots(
        int versionId, int? entryId = null, int? schoolClassId = null, int? subjectId = null, int? groupId = null,
        WeekParity weekParity = WeekParity.Every)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        var plan = await TeachingPlan.LoadAsync(db, version);
        var lessons = await LessonsAsync(version.Id);
        TimetableSlots.Moved? moved;
        if (entryId is { } id)
        {
            moved = await MovedOfAsync(lessons, id);
        }
        else if (schoolClassId is { } classId && subjectId is { } subject)
        {
            moved = Of(PartsOf(plan, classId, subject, groupId), weekParity, []);
        }
        else
        {
            return BadRequest();
        }
        if (moved is null)
        {
            return NotFound();
        }
        return Ok(TimetableSlots.Evaluate(moved, await WeekAsync(version, plan, lessons)));
    }

    [HttpPost("lessons/{entryId:int}/move")]
    [RequirePermission(Permission.Timetable)]
    [AuditAction("Přesunuta hodina v rozvrhu")]
    public async Task<IActionResult> Move(int versionId, int entryId, MoveLessonRequest request)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        if (TimetableController.LockedMessage(version) is { } readOnly)
        {
            return BadRequest(readOnly);
        }
        var lessons = await LessonsAsync(version.Id);
        if (await MovedOfAsync(lessons, entryId) is not { } moved)
        {
            return NotFound();
        }
        if (moved.Entries.Any(e => e.Locked))
        {
            return BadRequest(LockedLesson);
        }
        var plan = await TeachingPlan.LoadAsync(db, version);
        if (Refusal(TimetableSlots.Evaluate(moved, await WeekAsync(version, plan, lessons)), request) is { } refused)
        {
            return BadRequest(refused);
        }

        var rooms = await TimetableGenerationJob.RoomsAsync(db);
        var taken = Taken(lessons.Except(moved.Entries), request, moved.Parity);
        foreach (var part in moved.Entries.GroupBy(e => e.StudentGroupId))
        {
            var first = part.First();
            var room = first.ClassroomId is { } own && !taken.Contains(own)
                ? own
                : await FreeRoomAsync(new Part(part.Key, first.TeacherId, [.. part.Select(e => e.SchoolClassId)], first.SubjectId), rooms, taken);
            if (room is { } chosen)
            {
                taken.Add(chosen);
            }
            foreach (var lesson in part)
            {
                lesson.DayOfWeek = request.Day;
                lesson.PeriodNumber = request.Period;
                lesson.ClassroomId = room;
            }
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("lessons")]
    [RequirePermission(Permission.Timetable)]
    [AuditAction("Umístěna hodina do rozvrhu")]
    public async Task<IActionResult> Place(int versionId, PlaceLessonRequest request)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        if (TimetableController.LockedMessage(version) is { } readOnly)
        {
            return BadRequest(readOnly);
        }
        var plan = await TeachingPlan.LoadAsync(db, version);
        if (!Unplaced(plan, request.SchoolClassId).Any(u =>
                u.SubjectId == request.SubjectId && u.GroupId == request.GroupId && u.WeekParity == request.WeekParity))
        {
            return BadRequest(UnknownLesson);
        }
        var parts = PartsOf(plan, request.SchoolClassId, request.SubjectId, request.GroupId);
        var lessons = await LessonsAsync(version.Id);
        var target = new MoveLessonRequest(request.Day, request.Period);
        if (Refusal(TimetableSlots.Evaluate(Of(parts, request.WeekParity, []), await WeekAsync(version, plan, lessons)), target) is { } refused)
        {
            return BadRequest(refused);
        }

        var rooms = await TimetableGenerationJob.RoomsAsync(db);
        var taken = Taken(lessons, target, request.WeekParity);
        foreach (var part in parts)
        {
            var room = await FreeRoomAsync(part, rooms, taken);
            if (room is { } chosen)
            {
                taken.Add(chosen);
            }
            db.TimetableEntries.AddRange(part.ClassIds.Select(classId => new TimetableEntry
            {
                SchoolId = currentSchool.SchoolId!.Value,
                TimetableVersionId = version.Id,
                SchoolClassId = classId,
                StudentGroupId = part.GroupId,
                SubjectId = part.SubjectId,
                TeacherId = part.TeacherId,
                DayOfWeek = request.Day,
                PeriodNumber = request.Period,
                WeekParity = request.WeekParity,
                ClassroomId = room,
            }));
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Back among the unplaced ones, with the other groups of its
    /// division. Only what the plan wants: anything else wouldn't show there.</summary>
    [HttpDelete("lessons/{entryId:int}")]
    [RequirePermission(Permission.Timetable)]
    [AuditAction("Vrácena hodina z rozvrhu mezi neumístěné")]
    public async Task<IActionResult> Unplace(int versionId, int entryId)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        if (TimetableController.LockedMessage(version) is { } readOnly)
        {
            return BadRequest(readOnly);
        }
        if (await MovedOfAsync(await LessonsAsync(version.Id), entryId) is not { } moved)
        {
            return NotFound();
        }
        if (moved.Entries.Any(e => e.Locked))
        {
            return BadRequest(LockedLesson);
        }
        if (!AmongPlanned(await TeachingPlan.LoadAsync(db, version), moved.Entries.First()))
        {
            return BadRequest(NotPlanned);
        }
        db.TimetableEntries.RemoveRange(moved.Entries);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>How the timetable looks and what's wrong with it, as after
    /// generating and before publishing.</summary>
    [HttpGet("assessment")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableAssessmentDto>> GetAssessment(int versionId)
    {
        if (await db.TimetableVersions.FirstOrDefaultAsync(v => v.Id == versionId) is not { } version)
        {
            return NotFound();
        }
        var problems = await TimetableCheck.ProblemsAsync(db, version);
        var lessons = await LessonsAsync(version.Id);
        if (lessons.Count == 0)
        {
            return new TimetableAssessmentDto([], problems);
        }
        var plan = await TeachingPlan.LoadAsync(db, version);
        var classSizes = await db.Students.GroupBy(s => s.SchoolClassId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        var electives = await TimetableGenerationJob.ElectivesAsync(db, [.. plan.Groups], classSizes);
        var teachers = await TimetableGenerationJob.TeachersAsync(db, version.SchoolId, [.. plan.Teachers.Select(t => t.Id)]);
        var periods = await PeriodsAsync();
        var notes = TimetableGenerationJob.Assess(
            lessons, electives, plan.Classes.ToDictionary(c => c.Id, c => c.Name), teachers,
            plan.Teachers.ToDictionary(t => t.Id, t => t.Name),
            await MainBlock.ForClassesAsync(db, version), periods);
        return new TimetableAssessmentDto(notes, problems);
    }

    /// <summary>Taken out, it would be among the unplaced ones.</summary>
    private static bool AmongPlanned(TeachingPlan plan, TimetableEntry lesson)
    {
        if (lesson.StudentGroupId is { } groupId)
        {
            return plan.Groups.FirstOrDefault(g => g.Id == groupId) is { } group && group.TimetableHours <= group.PlanHours;
        }
        return plan.Classes.FirstOrDefault(c => c.Id == lesson.SchoolClassId)?.Rows.FirstOrDefault(r => r.SubjectId == lesson.SubjectId) is { } row
            && row.Groups.Count == 0 && row.TimetableHours <= row.PlanHours;
    }

    private static string? Refusal(List<DropSlot> slots, MoveLessonRequest target)
    {
        var slot = slots.FirstOrDefault(s => s.Day == target.Day && s.Period == target.Period);
        return slot is null ? "Tento čas v rozvrhu není."
            : slot.Status == DropSlotStatus.Blocked ? string.Join(" ", slot.Reasons)
            : null;
    }

    private Task<List<TimetableEntry>> LessonsAsync(int versionId) =>
        db.TimetableEntries.Where(e => e.TimetableVersionId == versionId).ToListAsync();

    private async Task<List<int>> PeriodsAsync()
    {
        var periods = await db.SchoolPeriods.Select(p => p.PeriodNumber).OrderBy(p => p).ToListAsync();
        return periods.Count > 0 ? periods : [.. Enumerable.Range(1, 8)];
    }

    private async Task<TimetableSlots.Week> WeekAsync(TimetableVersion version, TeachingPlan plan, List<TimetableEntry> lessons)
    {
        var members = await db.StudentGroupMembers.Where(m => m.StudentGroup!.TimetableVersionId == version.Id)
            .Select(m => new { m.StudentGroupId, m.StudentId }).ToListAsync();
        return new TimetableSlots.Week(
            TeacherProfile.Weekdays, await PeriodsAsync(), lessons,
            await TimetableGenerationJob.TeachersAsync(db, version.SchoolId, [.. plan.Teachers.Select(t => t.Id)]),
            plan.Teachers.ToDictionary(t => t.Id, t => t.Name),
            plan.Classes.ToDictionary(c => c.Id, c => c.Name),
            members.GroupBy(m => m.StudentGroupId)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)[.. g.Select(m => m.StudentId)]));
    }

    /// <summary>The lesson and, of a group, its other classes and the other
    /// groups of its division at the same time.</summary>
    private async Task<TimetableSlots.Moved?> MovedOfAsync(List<TimetableEntry> lessons, int entryId)
    {
        if (lessons.FirstOrDefault(e => e.Id == entryId) is not { } entry)
        {
            return null;
        }
        List<int> groupIds = [];
        if (entry.StudentGroupId is { } groupId)
        {
            var division = await db.StudentGroups.Where(g => g.Id == groupId).Select(g => g.GroupDivisionId).FirstOrDefaultAsync();
            groupIds = division is null
                ? [groupId]
                : await db.StudentGroups.Where(g => g.GroupDivisionId == division).Select(g => g.Id).ToListAsync();
        }
        var rows = entry.StudentGroupId is null
            ? [entry]
            : lessons.Where(e => e.StudentGroupId is { } g && groupIds.Contains(g)
                && e.DayOfWeek == entry.DayOfWeek && e.PeriodNumber == entry.PeriodNumber && e.WeekParity == entry.WeekParity).ToList();
        return new TimetableSlots.Moved(
            [.. rows.Select(e => e.SchoolClassId).Distinct()],
            [.. rows.Where(e => e.TeacherId is not null).Select(e => e.TeacherId!.Value).Distinct()],
            groupIds, entry.WeekParity, rows);
    }

    private static TimetableSlots.Moved Of(List<Part> parts, WeekParity parity, List<TimetableEntry> entries) => new(
        [.. parts.SelectMany(p => p.ClassIds).Distinct()],
        [.. parts.Where(p => p.TeacherId is not null).Select(p => p.TeacherId!.Value).Distinct()],
        [.. parts.Where(p => p.GroupId is not null).Select(p => p.GroupId!.Value)],
        parity, entries);

    private static List<Part> PartsOf(TeachingPlan plan, int schoolClassId, int subjectId, int? groupId)
    {
        if (groupId is { } id)
        {
            var group = plan.Groups.FirstOrDefault(g => g.Id == id);
            return group is null ? [] : [.. plan.Groups
                .Where(g => g.Id == id || (group.DivisionId is not null && g.DivisionId == group.DivisionId))
                .Select(g => new Part(g.Id, g.TeacherId, g.ClassIds, g.SubjectId))];
        }
        var row = plan.Classes.FirstOrDefault(c => c.Id == schoolClassId)?.Rows.FirstOrDefault(r => r.SubjectId == subjectId);
        return row is null ? [] : [new Part(null, row.TeacherId, [schoolClassId], subjectId)];
    }

    private static HashSet<int> Taken(IEnumerable<TimetableEntry> lessons, MoveLessonRequest target, WeekParity parity) =>
        [.. lessons.Where(e => e.DayOfWeek == target.Day && e.PeriodNumber == target.Period && e.ClassroomId is not null
                && (e.WeekParity == WeekParity.Every || parity == WeekParity.Every || e.WeekParity == parity))
            .Select(e => e.ClassroomId!.Value)];

    private async Task<int?> FixedRoomAsync(int? groupId) => groupId is { } id
        ? await db.StudentGroups.Where(g => g.Id == id).Select(g => g.ClassroomId).FirstOrDefaultAsync()
        : null;

    private async Task<int> MembersAsync(int? groupId, int classId) => groupId is { } id
        ? await db.StudentGroupMembers.CountAsync(m => m.StudentGroupId == id)
        : await db.Students.CountAsync(s => s.SchoolClassId == classId);

    /// <summary>The first free one the lesson can have that the pupils fit
    /// in; none = without a classroom, which the assessment reports.</summary>
    private async Task<int?> FreeRoomAsync(Part part, List<RoomInfo> rooms, HashSet<int> taken)
    {
        var subject = await db.Subjects.FirstAsync(s => s.Id == part.SubjectId);
        var fixedRoom = await FixedRoomAsync(part.GroupId);
        int? homeroom = part.ClassIds.Count == 1
            ? await db.SchoolClasses.Where(c => c.Id == part.ClassIds[0]).Select(c => c.HomeroomId).FirstOrDefaultAsync()
            : null;
        var size = await MembersAsync(part.GroupId, part.ClassIds[0]);
        return TimetableGenerationJob.ListedRooms(subject, fixedRoom, homeroom, rooms)
            .Where(r => (r.Capacity is null || r.Capacity >= size) && !taken.Contains(r.Id))
            .Select(r => (int?)r.Id).FirstOrDefault();
    }
}

public record MoveLessonRequest(DayOfWeek Day, int Period);

public record PlaceLessonRequest(int SchoolClassId, int SubjectId, DayOfWeek Day, int Period, int? GroupId = null, WeekParity WeekParity = WeekParity.Every);

/// <param name="GroupId">Of a division: its first group.</param>
/// <param name="GroupName">A group's or a division's.</param>
public record UnplacedLessonDto(
    int SubjectId, string SubjectShort, string SubjectName, int? GroupId, string? GroupName, string? TeacherName, WeekParity WeekParity);

public record TimetableAssessmentDto(IReadOnlyList<string> Notes, IReadOnlyList<string> Problems);
