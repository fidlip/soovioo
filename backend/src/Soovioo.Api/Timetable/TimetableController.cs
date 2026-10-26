using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

[Authorize]
[ApiController]
[Route("api/school-classes/{schoolClassId}/timetable")]
public class TimetableController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimetableEntryDto>>> GetTimetable(int schoolClassId)
    {
        var entries = await db.TimetableEntries
            .Where(e => e.SchoolClassId == schoolClassId)
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.PeriodNumber)
            .Select(e => new TimetableEntryDto(
                e.Id,
                e.SchoolClassId,
                e.SubjectId,
                e.Subject!.Name,
                e.Subject!.Abbreviation,
                e.DayOfWeek,
                e.PeriodNumber,
                e.WeekParity,
                e.TeacherId,
                e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId,
                e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name,
                Array.Empty<string>()))
            .ToListAsync();

        var conflicts = await FindConflictsAsync(entries.Select(e =>
            new ConflictSlot(e.Id, e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.ClassroomId)));
        return Ok(entries.Select(e => e with { Conflicts = conflicts.GetValueOrDefault(e.Id, []) }));
    }

    /// <summary>A teacher's own schedule, across every class they teach —
    /// unlike <see cref="GetTimetable"/>, not scoped to one class. Same
    /// unrestricted-within-tenant visibility as the class view (see
    /// ROADMAP — Rozvrh picker: Admin/Teacher may look up anyone, timetable
    /// slots aren't considered sensitive the way grades/absences are).
    /// Route is absolute (`/api/teachers/...`), not nested under this
    /// controller's own `api/school-classes/{schoolClassId}/timetable`
    /// prefix, since it isn't scoped to a class at all.</summary>
    [HttpGet("/api/teachers/{teacherId:int}/timetable")]
    public async Task<ActionResult<IEnumerable<TeacherTimetableEntryDto>>> GetTeacherTimetable(int teacherId)
    {
        var entries = await db.TimetableEntries
            .Where(e => e.TeacherId == teacherId)
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.PeriodNumber)
            .Select(e => new TeacherTimetableEntryDto(
                e.Id,
                e.SchoolClassId,
                e.SchoolClass!.Name,
                e.SubjectId,
                e.Subject!.Name,
                e.Subject!.Abbreviation,
                e.DayOfWeek,
                e.PeriodNumber,
                e.WeekParity,
                e.TeacherId,
                e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId,
                e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name,
                Array.Empty<string>()))
            .ToListAsync();

        var conflicts = await FindConflictsAsync(entries.Select(e =>
            new ConflictSlot(e.Id, e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.ClassroomId)));
        return Ok(entries.Select(e => e with { Conflicts = conflicts.GetValueOrDefault(e.Id, []) }));
    }

    [HttpPost]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableEntryDto>> AddEntry(int schoolClassId, CreateTimetableEntryRequest request)
    {
        // Both lookups are already scoped to the caller's school by the
        // global query filter, so a cross-tenant id just 404s/400s here.
        var schoolClass = await db.SchoolClasses.FindAsync(schoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Neznámý předmět.");
        }

        var teacher = await ResolveTeacherAsync(request.TeacherId);
        if (request.TeacherId is not null && teacher is null)
        {
            return BadRequest("Neznámý učitel.");
        }

        var classroom = await ResolveClassroomAsync(request.ClassroomId);
        if (request.ClassroomId is not null && classroom is null)
        {
            return BadRequest("Neznámá učebna.");
        }

        var entry = new TimetableEntry
        {
            SchoolClassId = schoolClassId,
            SubjectId = request.SubjectId,
            DayOfWeek = request.DayOfWeek,
            PeriodNumber = request.PeriodNumber,
            WeekParity = request.WeekParity,
            TeacherId = request.TeacherId,
            ClassroomId = request.ClassroomId,
            SchoolId = currentSchool.SchoolId!.Value,
        };

        db.TimetableEntries.Add(entry);
        await db.SaveChangesAsync();

        var conflicts = await FindConflictsAsync([ConflictSlot.Of(entry)]);
        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name, subject.Abbreviation,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity, entry.TeacherId, teacher == null ? null : PersonNames.Display(teacher),
            entry.ClassroomId, classroom?.ShortName ?? classroom?.Name, conflicts.GetValueOrDefault(entry.Id, []));
        return CreatedAtAction(nameof(GetTimetable), new { schoolClassId }, dto);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<TimetableEntryDto>> UpdateEntry(
        int schoolClassId, int id, CreateTimetableEntryRequest request)
    {
        var entry = await db.TimetableEntries.FirstOrDefaultAsync(e => e.Id == id && e.SchoolClassId == schoolClassId);
        if (entry is null)
        {
            return NotFound();
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Neznámý předmět.");
        }

        var teacher = await ResolveTeacherAsync(request.TeacherId);
        if (request.TeacherId is not null && teacher is null)
        {
            return BadRequest("Neznámý učitel.");
        }

        var classroom = await ResolveClassroomAsync(request.ClassroomId);
        if (request.ClassroomId is not null && classroom is null)
        {
            return BadRequest("Neznámá učebna.");
        }

        entry.SubjectId = request.SubjectId;
        entry.DayOfWeek = request.DayOfWeek;
        entry.PeriodNumber = request.PeriodNumber;
        entry.WeekParity = request.WeekParity;
        entry.TeacherId = request.TeacherId;
        entry.ClassroomId = request.ClassroomId;
        await db.SaveChangesAsync();

        var conflicts = await FindConflictsAsync([ConflictSlot.Of(entry)]);
        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name, subject.Abbreviation,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity, entry.TeacherId, teacher == null ? null : PersonNames.Display(teacher),
            entry.ClassroomId, classroom?.ShortName ?? classroom?.Name, conflicts.GetValueOrDefault(entry.Id, []));
        return Ok(dto);
    }

    /// <summary>Null teacherId is valid (unassigned); a non-null id must
    /// resolve to an actual Teacher in this school, not just any user.</summary>
    private async Task<User?> ResolveTeacherAsync(int? teacherId)
    {
        if (teacherId is null)
        {
            return null;
        }

        return await db.Users.Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == teacherId && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher));
    }

    private async Task<Classroom?> ResolveClassroomAsync(int? classroomId)
    {
        if (classroomId is null)
        {
            return null;
        }

        return await db.Classrooms.FindAsync(classroomId);
    }

    /// <summary>Non-blocking — collisions (same teacher or same classroom
    /// in the same day/period, with overlapping week parity) are reported
    /// back as warning strings per entry id, never rejected, so a real
    /// schedule quirk (two classes briefly sharing a gym, a data-entry
    /// mistake to be fixed later) can still be saved. Computed on every read
    /// so the grid keeps flagging them until fixed. Only checks other
    /// TimetableEntry rows, not Events (see ROADMAP).</summary>
    private async Task<Dictionary<int, string[]>> FindConflictsAsync(IEnumerable<ConflictSlot> slots)
    {
        var slotList = slots.ToList();
        var teacherIds = slotList.Where(s => s.TeacherId is not null).Select(s => s.TeacherId!.Value).Distinct().ToList();
        var classroomIds = slotList.Where(s => s.ClassroomId is not null).Select(s => s.ClassroomId!.Value).Distinct().ToList();
        if (teacherIds.Count == 0 && classroomIds.Count == 0)
        {
            return [];
        }

        var candidates = await db.TimetableEntries
            .Where(e =>
                (e.TeacherId != null && teacherIds.Contains(e.TeacherId.Value)) ||
                (e.ClassroomId != null && classroomIds.Contains(e.ClassroomId.Value)))
            .Select(e => new
            {
                e.Id,
                e.DayOfWeek,
                e.PeriodNumber,
                e.WeekParity,
                e.TeacherId,
                e.ClassroomId,
                ClassName = e.SchoolClass!.Name,
                SubjectName = e.Subject!.Name,
                TeacherName = e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                ClassroomName = e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name,
            })
            .ToListAsync();

        var result = new Dictionary<int, string[]>();
        foreach (var slot in slotList)
        {
            var messages = new List<string>();
            foreach (var c in candidates.Where(c =>
                         c.Id != slot.Id &&
                         c.DayOfWeek == slot.DayOfWeek &&
                         c.PeriodNumber == slot.PeriodNumber &&
                         ParityOverlaps(c.WeekParity, slot.WeekParity)))
            {
                if (slot.TeacherId is not null && c.TeacherId == slot.TeacherId)
                {
                    messages.Add($"{c.TeacherName} učí ve stejný čas i {c.ClassName} ({c.SubjectName}).");
                }
                if (slot.ClassroomId is not null && c.ClassroomId == slot.ClassroomId)
                {
                    messages.Add($"Učebna {c.ClassroomName} je ve stejný čas obsazená — {c.ClassName} ({c.SubjectName}).");
                }
            }
            if (messages.Count > 0)
            {
                result[slot.Id] = [.. messages];
            }
        }
        return result;
    }

    private record ConflictSlot(
        int Id, DayOfWeek DayOfWeek, int PeriodNumber, WeekParity WeekParity, int? TeacherId, int? ClassroomId)
    {
        public static ConflictSlot Of(TimetableEntry e) =>
            new(e.Id, e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.ClassroomId);
    }

    private static bool ParityOverlaps(WeekParity a, WeekParity b) =>
        a == WeekParity.Every || b == WeekParity.Every || a == b;

    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<IActionResult> DeleteEntry(int schoolClassId, int id)
    {
        var entry = await db.TimetableEntries.FirstOrDefaultAsync(e => e.Id == id && e.SchoolClassId == schoolClassId);
        if (entry is null)
        {
            return NotFound();
        }

        db.TimetableEntries.Remove(entry);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateTimetableEntryRequest(
    int SubjectId,
    DayOfWeek DayOfWeek,
    int PeriodNumber,
    WeekParity WeekParity,
    int? TeacherId = null,
    int? ClassroomId = null);

public record TimetableEntryDto(
    int Id,
    int SchoolClassId,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    DayOfWeek DayOfWeek,
    int PeriodNumber,
    WeekParity WeekParity,
    int? TeacherId,
    string? TeacherName,
    int? ClassroomId,
    string? ClassroomName,
    /// <summary>Teacher/classroom collisions with other entries (empty when
    /// none) — see FindConflictsAsync's doc comment.</summary>
    IReadOnlyList<string> Conflicts);

/// <summary>Same shape as <see cref="TimetableEntryDto"/>, plus which class
/// each entry belongs to — needed here (unlike the class-scoped DTO above)
/// because a teacher's entries can span several different classes.</summary>
public record TeacherTimetableEntryDto(
    int Id,
    int SchoolClassId,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    DayOfWeek DayOfWeek,
    int PeriodNumber,
    WeekParity WeekParity,
    int? TeacherId,
    string? TeacherName,
    int? ClassroomId,
    string? ClassroomName,
    IReadOnlyList<string> Conflicts);
