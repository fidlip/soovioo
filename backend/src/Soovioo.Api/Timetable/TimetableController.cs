using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

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
                e.Teacher == null ? null : e.Teacher.Username,
                e.ClassroomId,
                e.Classroom == null ? null : e.Classroom.Name,
                null))
            .ToListAsync();

        return Ok(entries);
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
                e.Teacher == null ? null : e.Teacher.Username,
                e.ClassroomId,
                e.Classroom == null ? null : e.Classroom.Name))
            .ToListAsync();

        return Ok(entries);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<TimetableEntryDto>> AddEntry(int schoolClassId, CreateTimetableEntryRequest request)
    {
        // Both lookups are already scoped to the caller's school by the
        // global query filter, so a cross-tenant id just 404s/400s here.
        var schoolClass = await db.SchoolClasses.FindAsync(schoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Unknown schoolClassId.");
        }

        var subject = await db.Subjects.FindAsync(request.SubjectId);
        if (subject is null)
        {
            return BadRequest("Unknown subjectId.");
        }

        var teacher = await ResolveTeacherAsync(request.TeacherId);
        if (request.TeacherId is not null && teacher is null)
        {
            return BadRequest("Unknown teacherId.");
        }

        var classroom = await ResolveClassroomAsync(request.ClassroomId);
        if (request.ClassroomId is not null && classroom is null)
        {
            return BadRequest("Unknown classroomId.");
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

        var conflict = await FindClassroomConflictAsync(entry);
        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name, subject.Abbreviation,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity, entry.TeacherId, teacher?.Username,
            entry.ClassroomId, classroom?.Name, conflict);
        return CreatedAtAction(nameof(GetTimetable), new { schoolClassId }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
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
            return BadRequest("Unknown subjectId.");
        }

        var teacher = await ResolveTeacherAsync(request.TeacherId);
        if (request.TeacherId is not null && teacher is null)
        {
            return BadRequest("Unknown teacherId.");
        }

        var classroom = await ResolveClassroomAsync(request.ClassroomId);
        if (request.ClassroomId is not null && classroom is null)
        {
            return BadRequest("Unknown classroomId.");
        }

        entry.SubjectId = request.SubjectId;
        entry.DayOfWeek = request.DayOfWeek;
        entry.PeriodNumber = request.PeriodNumber;
        entry.WeekParity = request.WeekParity;
        entry.TeacherId = request.TeacherId;
        entry.ClassroomId = request.ClassroomId;
        await db.SaveChangesAsync();

        var conflict = await FindClassroomConflictAsync(entry);
        var dto = new TimetableEntryDto(
            entry.Id, entry.SchoolClassId, entry.SubjectId, subject.Name, subject.Abbreviation,
            entry.DayOfWeek, entry.PeriodNumber, entry.WeekParity, entry.TeacherId, teacher?.Username,
            entry.ClassroomId, classroom?.Name, conflict);
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

        return await db.Users.FirstOrDefaultAsync(u => u.Id == teacherId && u.Role == UserRole.Teacher);
    }

    private async Task<Classroom?> ResolveClassroomAsync(int? classroomId)
    {
        if (classroomId is null)
        {
            return null;
        }

        return await db.Classrooms.FindAsync(classroomId);
    }

    /// <summary>Non-blocking — collisions are reported back to the caller as
    /// a warning string on the DTO, never rejected, so a real schedule
    /// quirk (two classes briefly sharing a gym, a data-entry mistake to be
    /// fixed later) can still be saved. Only checks other TimetableEntry
    /// rows, not Events — cross-checking those is explicitly out of scope
    /// for now (see ROADMAP).</summary>
    private async Task<string?> FindClassroomConflictAsync(TimetableEntry entry)
    {
        if (entry.ClassroomId is null)
        {
            return null;
        }

        var candidates = await db.TimetableEntries
            .Where(e =>
                e.Id != entry.Id &&
                e.ClassroomId == entry.ClassroomId &&
                e.DayOfWeek == entry.DayOfWeek &&
                e.PeriodNumber == entry.PeriodNumber)
            .Select(e => new { e.WeekParity, e.SchoolClass!.Name, SubjectName = e.Subject!.Name })
            .ToListAsync();

        var conflict = candidates.FirstOrDefault(c => ParityOverlaps(c.WeekParity, entry.WeekParity));
        return conflict is null
            ? null
            : $"Učebna je ve stejný čas obsazená — {conflict.Name} ({conflict.SubjectName}).";
    }

    private static bool ParityOverlaps(WeekParity a, WeekParity b) =>
        a == WeekParity.Every || b == WeekParity.Every || a == b;

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
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
    /// <summary>Non-null only right after a successful add/update that
    /// collides with another entry's classroom — GetTimetable always
    /// returns null here, see FindClassroomConflictAsync's doc comment.</summary>
    string? ClassroomConflict);

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
    string? ClassroomName);
