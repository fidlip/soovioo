using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;
using Soovioo.Api.SchoolYears;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.ClassBook;

/// <summary>Třídní kniha: the lessons of a class's day as taught (with the
/// published substitutions), each written with its topic.</summary>
[Authorize]
[ApiController]
[Route("api/class-book")]
[AuditArea("Třídní kniha")]
public class ClassBookController(AppDbContext db, ICurrentSchoolContext currentSchool, TimeProvider clock) : ControllerBase
{
    public const int TopicMaxLength = 500;

    /// <summary>The classes whose třídní kniha the caller sees.</summary>
    [HttpGet("classes")]
    [RequirePermission(Permission.ClassBookRead)]
    public async Task<ActionResult<IEnumerable<ClassBookClassDto>>> GetClasses()
    {
        var ids = await User.ClassIdsAsync(db, Permission.ClassBookRead);
        var classes = await db.SchoolClasses
            .Where(c => ids == null || ids.Contains(c.Id))
            .Select(c => new ClassBookClassDto(c.Id, c.Name))
            .ToListAsync();
        return Ok(classes.OrderBy(c => c.Name, StringComparer.Ordinal));
    }

    /// <summary>The lessons of the class on the day; without a class those
    /// the caller teaches then.</summary>
    [HttpGet("day")]
    [RequirePermission(Permission.ClassBookRead)]
    public async Task<ActionResult<ClassBookDayDto>> GetDay(DateOnly date, int? classId = null)
    {
        if (classId is { } id && !await User.ReachesClassAsync(db, Permission.ClassBookRead, id))
        {
            return Forbid();
        }
        var lessons = await LessonsAsync(date, classId);
        var calendar = await SchoolCalendar.LoadAsync(db, date, date);
        var dayOff = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? null : calendar.Off(date, classId)?.Name;
        return Ok(new ClassBookDayDto(date, await DtosAsync(lessons), dayOff));
    }

    /// <summary>Writes the lesson's topic; an empty one takes the record back.</summary>
    [HttpPut("lessons")]
    [RequirePermission(Permission.ClassBookWrite)]
    [AuditAction("Zapsána hodina do třídní knihy")]
    public async Task<ActionResult<ClassBookLessonDto>> SaveLesson(SaveLessonRequest request)
    {
        var key = new LessonKey(request.Date, request.PeriodNumber, request.SchoolClassId, request.StudentGroupId);
        var lesson = (await LessonsAsync(request.Date, null, all: true)).FirstOrDefault(l => l.Key == key && !l.Gone);
        if (lesson is null)
        {
            return NotFound("Tato hodina se ten den neučí.");
        }
        switch (await AccessAsync(lesson))
        {
            case LessonAccess.None:
                return Forbid();
            case LessonAccess.TooEarly:
                return BadRequest("Hodinu jde zapsat nejdříve v její den.");
            case LessonAccess.TooLate:
                return BadRequest("Vyučující může hodinu zapsat nejpozději následující den, později už jen třídní nebo vedení.");
        }
        if (!await db.HasSchoolYearOnAsync(request.Date))
        {
            return BadRequest(GradingPeriods.NoYear);
        }
        var topic = request.Topic?.Trim() ?? "";
        if (topic.Length > TopicMaxLength)
        {
            return this.FieldError("topic", $"Téma může mít nejvýš {TopicMaxLength} znaků.");
        }

        var record = await db.LessonRecords.FirstOrDefaultAsync(r => r.Date == key.Date && r.PeriodNumber == key.Period
            && r.SchoolClassId == key.ClassId && r.StudentGroupId == key.GroupId);
        if (topic.Length == 0)
        {
            if (record is not null)
            {
                db.LessonRecords.Remove(record);
            }
        }
        else
        {
            if (record is null)
            {
                record = new LessonRecord
                {
                    SchoolId = currentSchool.SchoolId!.Value,
                    Date = key.Date,
                    PeriodNumber = key.Period,
                    SchoolClassId = key.ClassId,
                    StudentGroupId = key.GroupId,
                };
                db.LessonRecords.Add(record);
            }
            record.Topic = topic;
            record.SubjectId = lesson.SubjectId;
            record.TeacherId = lesson.TeacherId;
            record.RecordedById = User.UserId();
            record.RecordedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync();
        return Ok((await DtosAsync([lesson]))[0]);
    }

    /// <summary>The day's lessons of the class, or the caller's; with
    /// <paramref name="all"/> every lesson of the school.</summary>
    private async Task<List<EffectiveLesson>> LessonsAsync(DateOnly date, int? classId, bool all = false)
    {
        var userId = User.UserId();
        return [.. (await LessonChanges.OnAsync(db, date, date))
            .Where(l => all || (classId is { } c ? l.ClassIds.Contains(c) : l.TeacherId == userId && !l.Gone))
            .OrderBy(l => l.Key.Period)
            .ThenBy(l => l.Gone)];
    }

    private enum LessonAccess { None, Allowed, TooEarly, TooLate }

    /// <summary>The school's or the třídní's any time, the teacher's on its
    /// day and the next — none before the day.</summary>
    private async Task<LessonAccess> AccessAsync(EffectiveLesson lesson)
    {
        var scope = await User.ScopeAsync(db, Permission.ClassBookWrite);
        var reaches = scope == PermissionScope.School
            || (scope == PermissionScope.ClassTeacher
                && (await ClassTeacherOfAsync()).Overlaps(lesson.ClassIds));
        var teaches = scope != PermissionScope.None && lesson.TeacherId == User.UserId();
        if (!reaches && !teaches)
        {
            return LessonAccess.None;
        }
        if (lesson.Key.Date > db.Today)
        {
            return LessonAccess.TooEarly;
        }
        return reaches || db.Today <= lesson.Key.Date.AddDays(1) ? LessonAccess.Allowed : LessonAccess.TooLate;
    }

    private HashSet<int>? classTeacherOf;

    private async Task<HashSet<int>> ClassTeacherOfAsync() =>
        classTeacherOf ??= [.. await db.GetOwnClassIdsAsClassTeacherAsync(User.UserId())];

    private async Task<List<ClassBookLessonDto>> DtosAsync(List<EffectiveLesson> lessons)
    {
        var names = await LessonNames.LoadAsync(db, lessons);
        var periods = await db.SchoolPeriods.ToDictionaryAsync(p => p.PeriodNumber);
        var dates = lessons.Select(l => l.Key.Date).Distinct().ToList();
        var records = await db.LessonRecords
            .Where(r => dates.Contains(r.Date))
            .Select(r => new
            {
                r.Date, r.PeriodNumber, r.SchoolClassId, r.StudentGroupId, r.Topic, r.RecordedAt,
                RecordedBy = r.RecordedBy == null ? null : PersonNames.Display(r.RecordedBy.Person, r.RecordedBy.Username),
            })
            .ToListAsync();
        var result = new List<ClassBookLessonDto>();
        foreach (var lesson in lessons)
        {
            var key = lesson.Key;
            var record = records.FirstOrDefault(r => r.Date == key.Date && r.PeriodNumber == key.Period
                && r.SchoolClassId == key.ClassId && r.StudentGroupId == key.GroupId);
            var period = periods.GetValueOrDefault(key.Period);
            var subject = names.Subjects.GetValueOrDefault(lesson.SubjectId);
            var access = lesson.Gone ? LessonAccess.None : await AccessAsync(lesson);
            result.Add(new ClassBookLessonDto(
                key.Date, key.Period, key.ClassId, key.GroupId, period?.StartTime, period?.EndTime,
                names.Who(key), lesson.SubjectId, subject.Name ?? "", subject.Abbreviation,
                lesson.TeacherId, lesson.TeacherId is null ? null : names.Teacher(lesson.TeacherId),
                lesson.ClassroomId is { } room ? names.Classrooms.GetValueOrDefault(room) : null,
                LessonChangeViews.Label(lesson, names), lesson.Gone,
                record?.Topic, record?.RecordedBy, record?.RecordedAt,
                access == LessonAccess.Allowed));
        }
        return result;
    }
}

public record ClassBookClassDto(int Id, string Name);

/// <param name="DayOff">Why there are no lessons ("Podzimní prázdniny"); null on a day of lessons and at the weekend.</param>
public record ClassBookDayDto(DateOnly Date, IReadOnlyList<ClassBookLessonDto> Lessons, string? DayOff);

/// <summary>A lesson of the day; <see cref="Date"/>, <see cref="PeriodNumber"/>,
/// <see cref="SchoolClassId"/> and <see cref="StudentGroupId"/> say which.</summary>
/// <param name="SchoolClassId">Null for a group's lesson.</param>
/// <param name="Who">"6.A", or "Aj 1 (6.A, 6.B)" for a group.</param>
/// <param name="Label">"Supl", "Odpadá", …; empty for the timetable's lesson.</param>
/// <param name="Gone">Not taught: cancelled or moved elsewhere.</param>
/// <param name="Topic">Null while not written.</param>
/// <param name="CanWrite">The caller may write it now.</param>
public record ClassBookLessonDto(
    DateOnly Date, int PeriodNumber, int? SchoolClassId, int? StudentGroupId, TimeOnly? StartTime, TimeOnly? EndTime,
    string Who, int SubjectId, string SubjectName, string? SubjectAbbreviation,
    int? TeacherId, string? TeacherName, string? ClassroomName, string Label, bool Gone,
    string? Topic, string? RecordedByName, DateTimeOffset? RecordedAt, bool CanWrite);

/// <param name="SchoolClassId">Null for a group's lesson.</param>
/// <param name="Topic">Empty takes the record back.</param>
public record SaveLessonRequest(DateOnly Date, int PeriodNumber, int? SchoolClassId, int? StudentGroupId, string? Topic);
