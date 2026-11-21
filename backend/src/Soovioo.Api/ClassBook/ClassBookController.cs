using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.SchoolYears;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.ClassBook;

/// <summary>Třídní kniha: the lessons of a class's day as taught (with the
/// published substitutions), each written with its topic and who was there.</summary>
[Authorize]
[ApiController]
[Route("api/class-book")]
[AuditArea("Třídní kniha")]
public class ClassBookController(
    AppDbContext db, ICurrentSchoolContext currentSchool, TimeProvider clock, Notifier notifier,
    MessageAttachmentStorageService storage, PhotoAccess photoAccess) : ControllerBase
{
    public const int TopicMaxLength = 500;
    public const int MaxMinutes = 240;

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

    /// <summary>The caller's lesson going on now, else their next one today
    /// — to take its attendance. None (204) when nothing is left.</summary>
    [HttpGet("now")]
    [RequirePermission(Permission.ClassBookWrite)]
    public async Task<ActionResult<ClassBookLessonDto?>> GetNow()
    {
        var now = TimeOnly.FromDateTime(SchoolClock.Local(clock.GetUtcNow()));
        var periods = await db.SchoolPeriods.ToDictionaryAsync(p => p.PeriodNumber);
        var lesson = (await LessonsAsync(db.Today, null))
            .Where(l => periods.TryGetValue(l.Key.Period, out var p) && p.EndTime > now)
            .FirstOrDefault();
        if (lesson is null || await AccessAsync(lesson) != LessonAccess.Allowed)
        {
            return NoContent();
        }
        return Ok((await DtosAsync([lesson]))[0]);
    }

    /// <summary>The lesson's pupils with who was there — or, before the
    /// attendance is taken, who likely isn't.</summary>
    [HttpGet("lessons/pupils")]
    [RequirePermission(Permission.ClassBookRead)]
    public async Task<ActionResult<LessonPupilsDto>> GetPupils(
        DateOnly date, int periodNumber, int? schoolClassId = null, int? studentGroupId = null)
    {
        var key = new LessonKey(date, periodNumber, schoolClassId, studentGroupId);
        var lesson = (await LessonsAsync(date, null, all: true)).FirstOrDefault(l => l.Key == key && !l.Gone);
        if (lesson is null)
        {
            return NotFound("Tato hodina se ten den neučí.");
        }
        if (await AccessAsync(lesson) == LessonAccess.None && !await ReadsAnyAsync(lesson.ClassIds))
        {
            return Forbid();
        }
        var book = Book();
        var pupils = await book.PupilsAsync(key);
        var record = await RecordQuery(key).Include(r => r.Attendance).FirstOrDefaultAsync();
        var taken = record?.AttendanceTakenAt is not null;
        // Once taken, the absence is mostly the lesson's own; an omluvenka still tells.
        var suggested = await book.SuggestedAbsentAsync(key, [.. pupils.Select(p => p.Id)], excusesOnly: taken);
        var photos = await photoAccess.VisibleStudentPhotosAsync(User, pupils);
        return Ok(new LessonPupilsDto(taken, [.. pupils.Select(p =>
        {
            var mark = record?.Attendance.FirstOrDefault(a => a.StudentId == p.Id);
            return new LessonPupilDto(
                p.Id, p.Person!.FirstName, p.Person.LastName, photos.GetValueOrDefault(p.Id),
                mark?.Status, mark?.Minutes, suggested.GetValueOrDefault(p.Id));
        })]));
    }

    /// <summary>Writes the lesson's topic and, when given, who was there.
    /// An empty topic takes back a record without attendance.</summary>
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
        var book = Book();
        var pupils = request.Attendance is null ? [] : await book.PupilsAsync(key);
        if (request.Attendance is { } marks)
        {
            var pupilIds = pupils.Select(p => p.Id).ToHashSet();
            if (marks.Any(m => !pupilIds.Contains(m.StudentId)) || marks.DistinctBy(m => m.StudentId).Count() != marks.Count)
            {
                return BadRequest("Žák není v této hodině.");
            }
            if (marks.Any(m => m.Minutes is { } minutes
                && (m.Status is not (LessonAttendanceStatus.Late or LessonAttendanceStatus.LeftEarly) || minutes is < 1 or > MaxMinutes)))
            {
                return BadRequest($"Minuty patří jen k pozdnímu příchodu a odchodu, 1 až {MaxMinutes}.");
            }
        }

        var record = await RecordQuery(key).Include(r => r.Attendance).FirstOrDefaultAsync();
        if (topic.Length == 0 && request.Attendance is null && record?.AttendanceTakenAt is null)
        {
            if (record is not null)
            {
                db.LessonRecords.Remove(record);
                await db.SaveChangesAsync();
            }
            return Ok((await DtosAsync([lesson]))[0]);
        }

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
        var now = clock.GetUtcNow();
        record.Topic = topic;
        record.SubjectId = lesson.SubjectId;
        record.TeacherId = lesson.TeacherId;
        record.RecordedById = User.UserId();
        record.RecordedAt = now;
        if (request.Attendance is { } attendance)
        {
            record.AttendanceTakenAt = now;
            record.Attendance.Clear();
            record.Attendance.AddRange(attendance.Select(m => new LessonAttendance
            {
                StudentId = m.StudentId, SchoolId = record.SchoolId, Status = m.Status, Minutes = m.Minutes,
            }));
        }
        await db.SaveChangesAsync();
        if (request.Attendance is { } saved)
        {
            await book.SyncAbsencesAsync(key, pupils,
                saved.Where(m => m.Status == LessonAttendanceStatus.Absent).Select(m => m.StudentId).ToHashSet(), User.UserId());
        }
        return Ok((await DtosAsync([lesson]))[0]);
    }

    private LessonAttendanceBook Book() => new(db, notifier, clock, storage);

    private IQueryable<LessonRecord> RecordQuery(LessonKey key) => db.LessonRecords.Where(r =>
        r.Date == key.Date && r.PeriodNumber == key.Period && r.SchoolClassId == key.ClassId && r.StudentGroupId == key.GroupId);

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

    private async Task<bool> ReadsAnyAsync(IReadOnlyList<int> classIds) =>
        await User.ClassIdsAsync(db, Permission.ClassBookRead) is not { } ids || ids.Overlaps(classIds);

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
                r.Date, r.PeriodNumber, r.SchoolClassId, r.StudentGroupId, r.Topic, r.RecordedAt, r.AttendanceTakenAt,
                RecordedBy = r.RecordedBy == null ? null : PersonNames.Display(r.RecordedBy.Person, r.RecordedBy.Username),
                Marks = r.Attendance
                    .OrderBy(a => a.Student!.Person!.LastName).ThenBy(a => a.Student!.Person!.FirstName)
                    .Select(a => new LessonMarkDto(
                        a.StudentId, a.Student!.Person!.FirstName + " " + a.Student.Person.LastName, a.Status, a.Minutes))
                    .ToList(),
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
                string.IsNullOrEmpty(record?.Topic) ? null : record.Topic, record?.RecordedBy, record?.RecordedAt,
                access == LessonAccess.Allowed,
                record?.AttendanceTakenAt is not null, record?.Marks ?? []));
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
/// <param name="RecordedAt">When it was last saved (topic or attendance); null while never.</param>
/// <param name="CanWrite">The caller may write it now.</param>
/// <param name="AttendanceTaken">The teacher went through the pupils.</param>
/// <param name="Marks">The pupils not there the whole lesson; the rest were.</param>
public record ClassBookLessonDto(
    DateOnly Date, int PeriodNumber, int? SchoolClassId, int? StudentGroupId, TimeOnly? StartTime, TimeOnly? EndTime,
    string Who, int SubjectId, string SubjectName, string? SubjectAbbreviation,
    int? TeacherId, string? TeacherName, string? ClassroomName, string Label, bool Gone,
    string? Topic, string? RecordedByName, DateTimeOffset? RecordedAt, bool CanWrite,
    bool AttendanceTaken, IReadOnlyList<LessonMarkDto> Marks);

public record LessonMarkDto(int StudentId, string StudentName, LessonAttendanceStatus Status, int? Minutes);

/// <param name="AttendanceTaken">False: <see cref="LessonPupilDto.Suggestion"/> says who likely isn't there.</param>
public record LessonPupilsDto(bool AttendanceTaken, IReadOnlyList<LessonPupilDto> Pupils);

/// <param name="Photo">The photo token, when the caller may see it.</param>
/// <param name="Status">Null = there (or, before the attendance is taken, not known).</param>
/// <param name="Suggestion">Why the pupil is likely away ("Omluvenka: nemoc"); null when nothing says so.</param>
public record LessonPupilDto(
    int StudentId, string FirstName, string LastName, string? Photo, LessonAttendanceStatus? Status, int? Minutes,
    string? Suggestion);

/// <param name="Minutes">For <see cref="LessonAttendanceStatus.Late"/> and <see cref="LessonAttendanceStatus.LeftEarly"/>.</param>
public record LessonMarkRequest(int StudentId, LessonAttendanceStatus Status, int? Minutes = null);

/// <param name="SchoolClassId">Null for a group's lesson.</param>
/// <param name="Topic">Empty takes back a record without attendance.</param>
/// <param name="Attendance">The pupils not there the whole lesson; null leaves the attendance as it is.</param>
public record SaveLessonRequest(
    DateOnly Date, int PeriodNumber, int? SchoolClassId, int? StudentGroupId, string? Topic,
    IReadOnlyList<LessonMarkRequest>? Attendance = null);
