using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Suplování: teachers' absences, the changes of single lessons of a
/// day (another teacher, subject or classroom, cancelled, moved within the
/// week, merged with another class's or group's lesson, an extra one), and
/// publishing a day — only then the changes show in the timetables and
/// whom they concern hear of them.</summary>
[Authorize]
[ApiController]
[Route("api/substitutions")]
public class SubstitutionsController(
    AppDbContext db, ICurrentSchoolContext currentSchool, TimeProvider clock, Notifier notifier) : ControllerBase
{
    private const string NoTimetable = "Na tento den není zveřejněný rozvrh.";
    private const string MergedInto = "S touto hodinou je spojená jiná hodina, nejdřív zrušte to spojení.";

    /// <summary>The published changes of a week, as the class, the pupil or
    /// the teacher sees them.</summary>
    [HttpGet("week")]
    public async Task<ActionResult<IEnumerable<LessonChangeDto>>> GetWeek(
        [FromQuery] DateOnly weekStart, [FromQuery] int? schoolClassId = null, [FromQuery] int? teacherId = null,
        [FromQuery] int? studentId = null)
    {
        var scope = await ScopeAsync(schoolClassId, teacherId, studentId);
        if (scope is null)
        {
            return Ok(Array.Empty<LessonChangeDto>());
        }
        var monday = LessonChanges.MondayOf(weekStart);
        var lessons = await LessonChanges.OnAsync(db, monday, monday.AddDays(6));
        return Ok(LessonChangeViews.For(lessons, scope, await LessonNames.LoadAsync(db, lessons)));
    }

    private async Task<LessonScope?> ScopeAsync(int? schoolClassId, int? teacherId, int? studentId)
    {
        if (teacherId is not null)
        {
            return new LessonScope(TeacherId: teacherId);
        }
        if (studentId is { } id)
        {
            var classId = await db.Students.Where(s => s.Id == id).Select(s => (int?)s.SchoolClassId).FirstOrDefaultAsync();
            if (classId is null)
            {
                return null;
            }
            var groups = await db.StudentGroupMembers.Where(m => m.StudentId == id).Select(m => m.StudentGroupId).ToListAsync();
            return new LessonScope(ClassId: classId, GroupIds: groups.ToHashSet());
        }
        return schoolClassId is null ? null : new LessonScope(ClassId: schoolClassId);
    }

    [HttpGet("days/{date}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionDayDto>> GetDay(DateOnly date) => Ok(await DayAsync(date));

    /// <summary>The days from <paramref name="from"/> on with changes prepared
    /// or published — what another version of the timetable would concern.</summary>
    [HttpGet("changed-days")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<IEnumerable<DateOnly>>> GetChangedDays([FromQuery] DateOnly from)
    {
        var prepared = await db.LessonChanges.Where(c => c.Date >= from).Select(c => c.Date).Distinct().ToListAsync();
        var published = (await db.SubstitutionDays.Where(d => d.Date >= from).ToListAsync())
            .Where(d => d.Changes.Count > 0).Select(d => d.Date);
        return Ok(prepared.Union(published).Order());
    }

    [HttpPost("absences")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionDayDto>> AddAbsence(TeacherAbsenceRequest request)
    {
        if (request.DateTo < request.DateFrom || request.PeriodTo < request.PeriodFrom)
        {
            return BadRequest("Konec nepřítomnosti je před jejím začátkem.");
        }
        if (!await db.Users.AnyAsync(u => u.Id == request.TeacherId && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)))
        {
            return BadRequest("Učitel neexistuje.");
        }
        db.TeacherAbsences.Add(new TeacherAbsence
        {
            SchoolId = currentSchool.SchoolId!.Value, TeacherId = request.TeacherId, DateFrom = request.DateFrom,
            DateTo = request.DateTo, PeriodFrom = request.PeriodFrom, PeriodTo = request.PeriodTo,
        });
        await db.SaveChangesAsync();
        return Ok(await DayAsync(request.Day ?? request.DateFrom));
    }

    [HttpDelete("absences/{id:int}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<IActionResult> DeleteAbsence(int id)
    {
        if (await db.TeacherAbsences.FindAsync(id) is not { } absence)
        {
            return NotFound();
        }
        db.TeacherAbsences.Remove(absence);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sets what differs from the timetable about one lesson; with
    /// nothing left the lesson is as the timetable has it again.</summary>
    [HttpPut("changes")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionDayDto>> SaveChange(LessonChangeRequest request)
    {
        var key = new LessonKey(request.Date, request.PeriodNumber, request.SchoolClassId, request.StudentGroupId);
        if ((key.ClassId is null) == (key.GroupId is null))
        {
            return BadRequest("Změna patří třídě, nebo skupině.");
        }
        if (await db.TimetableVersionOnAsync(request.Date) is not { } version)
        {
            return BadRequest(NoTimetable);
        }
        var periods = await db.SchoolPeriods.Select(p => p.PeriodNumber).ToListAsync();
        if (!periods.Contains(request.PeriodNumber))
        {
            return BadRequest("Taková hodina ve zvonění není.");
        }
        var lessons = await LessonChanges.OnAsync(db, request.Date, request.Date, prepared: true,
            adjust: changes => [.. changes.Where(c => c.Key != key)]);
        var original = lessons.FirstOrDefault(l => l.Original?.Key == key && !l.MovedIn)?.Original;
        var movedTo = request.MovedToDate is { } toDate && request.MovedToPeriod is { } toPeriod
            ? new LessonKey(toDate, toPeriod, key.ClassId, key.GroupId)
            : null;
        var mergedWith = request.MergedWithClassId is not null || request.MergedWithGroupId is not null
            ? new LessonKey(request.Date, request.PeriodNumber, request.MergedWithClassId, request.MergedWithGroupId)
            : null;

        if (request.Extra)
        {
            if (original is not null)
            {
                return BadRequest("V tu dobu už hodinu má — změňte tu.");
            }
            if (request.SubjectId is null)
            {
                return BadRequest("Vyberte předmět.");
            }
            if (movedTo is not null)
            {
                return BadRequest("Mimořádnou hodinu přidejte rovnou v jiný čas.");
            }
            if (key.ClassId is { } classId ? !await db.SchoolClasses.AnyAsync(c => c.Id == classId)
                : !await db.StudentGroups.AnyAsync(g => g.Id == key.GroupId && g.TimetableVersionId == version.Id))
            {
                return BadRequest("Taková třída nebo skupina není.");
            }
        }
        else if (original is null)
        {
            return BadRequest("Tato hodina v rozvrhu není.");
        }
        if (new[] { request.Cancelled, movedTo is not null, mergedWith is not null }.Count(x => x) > 1)
        {
            return BadRequest("Hodina buď odpadá, nebo se přesouvá, nebo se spojuje — jen jedno z toho.");
        }
        if (movedTo is not null)
        {
            if (LessonChanges.MondayOf(movedTo.Date) != LessonChanges.MondayOf(key.Date)
                || movedTo.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                return BadRequest("Hodina se přesouvá jen v rámci týdne. Jinak ji zrušte a přidejte mimořádnou.");
            }
            if (movedTo == key)
            {
                return BadRequest("Hodina už je v tento čas.");
            }
            if (!periods.Contains(movedTo.Period))
            {
                return BadRequest("Taková hodina ve zvonění není.");
            }
            if (await db.TimetableVersionOnAsync(movedTo.Date) is null)
            {
                return BadRequest(NoTimetable);
            }
        }
        if (mergedWith is not null && (mergedWith == key || !lessons.Any(l => l.Key == mergedWith && !l.Gone && l.Change?.MergedWith is null)))
        {
            return BadRequest("V tu dobu ta třída nebo skupina hodinu nemá, s níž by šlo hodinu spojit.");
        }
        if ((request.Cancelled || movedTo is not null || mergedWith is not null || request.Extra)
            && lessons.Any(l => l.Change?.MergedWith == key))
        {
            return Conflict(MergedInto);
        }
        if (request.TeacherId is { } teacherId && !await db.Users.AnyAsync(u => u.Id == teacherId))
        {
            return BadRequest("Učitel neexistuje.");
        }
        if (request.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId))
        {
            return BadRequest("Předmět neexistuje.");
        }
        if (request.ClassroomId is { } roomId && !await db.Classrooms.AnyAsync(r => r.Id == roomId))
        {
            return BadRequest("Učebna neexistuje.");
        }

        var noLesson = request.Cancelled || mergedWith is not null;
        int? Differs(int? value, int? timetable) => noLesson || value == timetable ? null : value;
        var change = new LessonChangeData(
            key.Date, key.Period, key.ClassId, key.GroupId, request.Extra, request.Cancelled,
            request.Extra ? request.TeacherId : Differs(request.TeacherId, original!.TeacherId),
            request.Extra ? request.SubjectId : Differs(request.SubjectId, original!.SubjectId),
            request.Extra ? request.ClassroomId : Differs(request.ClassroomId, original!.ClassroomId),
            movedTo?.Date, movedTo?.Period, mergedWith?.ClassId, mergedWith?.GroupId,
            original?.SubjectId, original?.TeacherId);
        var row = await FindAsync(key);
        if (change.Empty)
        {
            if (row is not null)
            {
                db.LessonChanges.Remove(row);
            }
        }
        else
        {
            if (row is null)
            {
                row = new LessonChange { SchoolId = currentSchool.SchoolId!.Value };
                db.LessonChanges.Add(row);
            }
            Set(row, change);
        }
        await db.SaveChangesAsync();
        return Ok(await DayAsync(request.Date));
    }

    private Task<LessonChange?> FindAsync(LessonKey key) =>
        db.LessonChanges.FirstOrDefaultAsync(c => c.Date == key.Date && c.PeriodNumber == key.Period
            && c.SchoolClassId == key.ClassId && c.StudentGroupId == key.GroupId);

    private static void Set(LessonChange row, LessonChangeData data)
    {
        row.Date = data.Date;
        row.PeriodNumber = data.Period;
        row.SchoolClassId = data.ClassId;
        row.StudentGroupId = data.GroupId;
        row.Extra = data.Extra;
        row.Cancelled = data.Cancelled;
        row.TeacherId = data.TeacherId;
        row.SubjectId = data.SubjectId;
        row.ClassroomId = data.ClassroomId;
        row.MovedToDate = data.MovedToDate;
        row.MovedToPeriod = data.MovedToPeriod;
        row.MergedWithClassId = data.MergedWithClassId;
        row.MergedWithGroupId = data.MergedWithGroupId;
        row.OfSubjectId = data.OfSubjectId;
        row.OfTeacherId = data.OfTeacherId;
    }

    [HttpDelete("changes/{id:int}")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionDayDto>> DeleteChange(int id)
    {
        if (await db.LessonChanges.FindAsync(id) is not { } row)
        {
            return NotFound();
        }
        var key = row.Data().Key;
        if (await db.LessonChanges.AnyAsync(c => c.Date == key.Date && c.PeriodNumber == key.Period
            && c.MergedWithClassId == key.ClassId && c.MergedWithGroupId == key.GroupId))
        {
            return Conflict(MergedInto);
        }
        db.LessonChanges.Remove(row);
        await db.SaveChangesAsync();
        return Ok(await DayAsync(key.Date));
    }

    /// <summary>Who can substitute the lesson taught at the period: free then
    /// and present, first those with the aprobace, then those teaching that
    /// day, then those with the fewest substitutions in the month. And the
    /// classrooms free then, those the subject uses first.</summary>
    [HttpGet("candidates")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionCandidatesDto>> GetCandidates(
        [FromQuery] DateOnly date, [FromQuery] int period, [FromQuery] int? schoolClassId = null,
        [FromQuery] int? studentGroupId = null, [FromQuery] int? subjectId = null)
    {
        var key = new LessonKey(date, period, schoolClassId, studentGroupId);
        var lessons = await LessonChanges.OnAsync(db, date, date, prepared: true);
        return Ok(await CandidatesAsync(key, lessons, subjectId));
    }

    private async Task<SubstitutionCandidatesDto> CandidatesAsync(LessonKey key, List<EffectiveLesson> lessons, int? subjectId)
    {
        var lesson = lessons.FirstOrDefault(l => l.Key == key && !l.Gone);
        var classIds = lesson?.ClassIds ?? (key.ClassId is { } c ? [c]
            : await db.StudentGroupClasses.Where(g => g.StudentGroupId == key.GroupId).Select(g => g.SchoolClassId).ToListAsync());
        var subject = subjectId ?? lesson?.Original?.SubjectId ?? lesson?.SubjectId;
        var grades = await db.SchoolClasses.Where(x => classIds.Contains(x.Id)).Select(x => x.Rocnik).ToListAsync();
        var same = lessons.Where(l => !l.Gone && l.Key.Date == key.Date && l.Key != key && l.MergedInto?.Key != key).ToList();
        var busy = same.Where(l => l.Key.Period == key.Period).ToList();
        var absent = (await db.TeacherAbsences.Where(a => a.DateFrom <= key.Date && a.DateTo >= key.Date).ToListAsync())
            .Where(a => a.Covers(key.Date, key.Period)).Select(a => a.TeacherId).ToHashSet();
        var monthStart = new DateOnly(key.Date.Year, key.Date.Month, 1);
        var counts = await db.LessonChanges
            .Where(x => x.Date >= monthStart && x.Date < monthStart.AddMonths(1) && x.TeacherId != null && !x.Cancelled)
            .GroupBy(x => x.TeacherId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        var teachers = await db.Users
            .Where(u => u.Roles.Any(r => r.BuiltIn == UserRole.Teacher) && u.DeactivatedAt == null)
            .Select(u => new { u.Id, u.Person, u.Username, Profile = u.Person!.TeacherProfile })
            .ToListAsync();
        var qualifications = (await db.TeacherQualifications
                .Where(q => q.TeacherProfile!.Person!.UserId != null)
                .Select(q => new { UserId = q.TeacherProfile!.Person!.UserId!.Value, Qualification = q }).ToListAsync())
            .ToLookup(q => q.UserId, q => q.Qualification);
        var candidates = teachers
            .Where(t => !absent.Contains(t.Id) && !busy.Any(l => l.TeacherId == t.Id) && t.Id != lesson?.Original?.TeacherId
                && AvailabilitySlot.At(t.Profile?.Availability ?? [], key.Date.DayOfWeek, key.Period) != AvailabilityKind.Unavailable)
            .Select(t => new SubstituteDto(
                t.Id, PersonNames.Display(t.Person, t.Username),
                subject is { } s && qualifications[t.Id].Any(q => grades.DefaultIfEmpty().All(g => q.Covers(s, g))),
                same.Any(l => l.TeacherId == t.Id), counts.GetValueOrDefault(t.Id)))
            .OrderByDescending(t => t.Qualified).ThenByDescending(t => t.TeachesThatDay)
            .ThenBy(t => t.SubstitutionsThisMonth).ThenBy(t => t.Name, StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), false))
            .ToList();

        var rooms = await TimetableGenerationJob.RoomsAsync(db);
        var taken = busy.Where(l => l.ClassroomId is not null).Select(l => l.ClassroomId!.Value).ToHashSet();
        var homeroom = key.ClassId is { } classId
            ? await db.SchoolClasses.Where(x => x.Id == classId).Select(x => x.HomeroomId).FirstOrDefaultAsync()
            : null;
        var listed = subject is { } subjectKey && await db.Subjects.FirstOrDefaultAsync(x => x.Id == subjectKey) is { } entity
            ? TimetableGenerationJob.ListedRooms(entity, null, homeroom, rooms).Select(r => r.Id).ToList()
            : [];
        var freeRooms = rooms.Where(r => !taken.Contains(r.Id))
            .OrderBy(r => listed.Contains(r.Id) ? listed.IndexOf(r.Id) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), false))
            .Select(r => new FreeClassroomDto(r.Id, r.Name, listed.Contains(r.Id)))
            .ToList();
        return new SubstitutionCandidatesDto(candidates, freeRooms);
    }

    /// <summary>Each lesson of an absent teacher without a substitute gets the
    /// first one offered, if there's anyone. Doesn't publish.</summary>
    [HttpPost("days/{date}/fill")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionFillDto>> Fill(DateOnly date)
    {
        var tried = new HashSet<LessonKey>();
        var unfilled = 0;
        while (true)
        {
            var lessons = await LessonChanges.OnAsync(db, date, date, prepared: true);
            var absences = await AbsencesOnAsync(date);
            var next = lessons.FirstOrDefault(l => NeedsCover(l, absences) && !tried.Contains(l.Key));
            if (next is null)
            {
                break;
            }
            tried.Add(next.Key);
            var candidate = (await CandidatesAsync(next.Key, lessons, null)).Teachers.FirstOrDefault();
            if (candidate is null)
            {
                unfilled++;
                continue;
            }
            var key = next.Change?.Key ?? next.Key;
            var row = await FindAsync(key);
            var fresh = row is null;
            if (row is null)
            {
                row = new LessonChange { SchoolId = currentSchool.SchoolId!.Value };
                db.LessonChanges.Add(row);
            }
            if (fresh || next.Original is { } original && !row.Data().Fits(original))
            {
                Set(row, new LessonChangeData(key.Date, key.Period, key.ClassId, key.GroupId, false, false,
                    null, null, null, null, null, null, null, next.Original?.SubjectId, next.Original?.TeacherId));
            }
            row.TeacherId = candidate.TeacherId;
            await db.SaveChangesAsync();
        }
        return Ok(new SubstitutionFillDto(await DayAsync(date), unfilled));
    }

    /// <summary>The day's changes as they are now show in the timetables;
    /// whom they concern, and whom the earlier published ones did, hear of
    /// what changed for them.</summary>
    [HttpPost("days/{date}/publish")]
    [RequirePermission(Permission.Timetable)]
    public async Task<ActionResult<SubstitutionDayDto>> Publish(DateOnly date)
    {
        if (await db.TimetableVersionOnAsync(date) is null)
        {
            return BadRequest(NoTimetable);
        }
        var monday = LessonChanges.MondayOf(date);
        var prepared = Ordered((await db.LessonChanges.Where(c => c.Date == date).ToListAsync()).Select(c => c.Data()));
        var before = await LessonChanges.OnAsync(db, monday, monday.AddDays(6));
        var after = await LessonChanges.OnAsync(db, monday, monday.AddDays(6),
            adjust: changes => [.. changes.Where(c => c.Date != date), .. prepared]);
        var day = await db.SubstitutionDays.FirstOrDefaultAsync(d => d.Date == date);
        if (day is null)
        {
            day = new SubstitutionDay { SchoolId = currentSchool.SchoolId!.Value, Date = date };
            db.SubstitutionDays.Add(day);
        }
        day.Changes = prepared;
        day.PublishedAt = clock.GetUtcNow();
        if (date >= SchoolClock.Today(clock))
        {
            await new SubstitutionNotice(db, notifier).AddAsync(currentSchool.SchoolId!.Value, date, before, after);
        }
        await db.SaveChangesAsync();
        return Ok(await DayAsync(date));
    }

    private static List<LessonChangeData> Ordered(IEnumerable<LessonChangeData> changes) =>
        [.. changes.OrderBy(c => c.Period).ThenBy(c => c.ClassId).ThenBy(c => c.GroupId)];

    private async Task<List<TeacherAbsence>> AbsencesOnAsync(DateOnly date) =>
        await db.TeacherAbsences.Where(a => a.DateFrom <= date && a.DateTo >= date).ToListAsync();

    private static bool NeedsCover(EffectiveLesson lesson, List<TeacherAbsence> absences) =>
        !lesson.Gone && lesson.MergedInto is null && lesson.TeacherId is { } teacher
        && absences.Any(a => a.TeacherId == teacher && a.Covers(lesson.Key.Date, lesson.Key.Period));

    private async Task<SubstitutionDayDto> DayAsync(DateOnly date)
    {
        var lessons = await LessonChanges.OnAsync(db, date, date, prepared: true);
        var names = await LessonNames.LoadAsync(db, lessons);
        var absences = await AbsencesOnAsync(date);
        var monday = LessonChanges.MondayOf(date);
        var rows = await db.LessonChanges.Where(c => c.Date >= monday && c.Date <= monday.AddDays(6)).ToListAsync();
        var ids = rows.ToDictionary(r => r.Data().Key, r => r.Id);
        var day = await db.SubstitutionDays.FirstOrDefaultAsync(d => d.Date == date);
        var prepared = Ordered(rows.Where(r => r.Date == date).Select(r => r.Data()));
        var absentNames = await db.Users.Where(u => absences.Select(a => a.TeacherId).Contains(u.Id))
            .Select(u => new { u.Id, u.Person, u.Username }).ToListAsync();

        var problems = new List<SubstitutionProblemDto>();
        void Problem(string text, LessonKey? change = null) =>
            problems.Add(new SubstitutionProblemDto(text, change is null ? null : ids.GetValueOrDefault(change)));
        var taught = lessons.Where(l => !l.Gone && l.MergedInto is null).ToList();
        foreach (var clash in taught.Where(l => l.TeacherId is not null).GroupBy(l => (l.Key.Period, l.TeacherId)).Where(g => g.Count() > 1))
        {
            Problem($"{clash.Key.Period}. hodina: {names.Teacher(clash.Key.TeacherId)} učí zároveň {string.Join(" a ", clash.Select(l => names.Who(l.Key)))}.");
        }
        foreach (var clash in taught.Where(l => l.ClassroomId is not null).GroupBy(l => (l.Key.Period, l.ClassroomId)).Where(g => g.Count() > 1))
        {
            Problem($"{clash.Key.Period}. hodina: v učebně {names.Classrooms.GetValueOrDefault(clash.Key.ClassroomId!.Value)} jsou zároveň {string.Join(" a ", clash.Select(l => names.Who(l.Key)))}.");
        }
        foreach (var slot in lessons.Where(l => !l.Gone).GroupBy(l => l.Key.Period))
        {
            foreach (var classId in slot.SelectMany(l => l.ClassIds).Distinct())
            {
                var of = slot.Where(l => l.ClassIds.Contains(classId)).ToList();
                if (of.Count > 1 && of.Any(l => l.Key.GroupId is null))
                {
                    Problem($"{slot.Key}. hodina: {names.Classes.GetValueOrDefault(classId)} má zároveň {string.Join(" a ", of.Select(l => names.Subject(l.SubjectId)))}.");
                }
            }
        }
        foreach (var orphan in prepared.Where(c => !c.Extra && !lessons.Any(l => l.Change?.Key == c.Key && !l.MovedIn)))
        {
            Problem(lessons.Any(l => l.Original?.Key == orphan.Key)
                ? $"{orphan.Period}. hodina {names.Who(orphan.Key)}: v rozvrhu je teď jiná hodina, než pro jakou je změna. Zkontrolujte ji."
                : $"{orphan.Period}. hodina {names.Who(orphan.Key)}: změna se týká hodiny, která v rozvrhu není.", orphan.Key);
        }
        foreach (var lesson in lessons.Where(l => l.Change?.MergedWith is not null && l.MergedInto is null && !l.Gone && !l.MovedIn))
        {
            Problem($"{lesson.Key.Period}. hodina {names.Who(lesson.Key)}: hodina, s níž je spojená, v tu dobu není.", lesson.Key);
        }

        var periods = await db.SchoolPeriods.OrderBy(p => p.PeriodNumber).Select(p => p.PeriodNumber).ToListAsync();
        var teachers = await db.Users
            .Where(u => u.Roles.Any(r => r.BuiltIn == UserRole.Teacher) && u.DeactivatedAt == null)
            .Select(u => new { u.Id, u.Person, u.Username, Profile = u.Person!.TeacherProfile })
            .ToListAsync();
        var collator = StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), false);
        var periodsOf = taught.Where(l => l.TeacherId is not null).ToLookup(l => l.TeacherId!.Value, l => l.Key.Period);
        var free = periods.Select(period =>
        {
            var names = teachers
                .Where(t => !taught.Any(l => l.Key.Period == period && l.TeacherId == t.Id)
                    && !absences.Any(a => a.TeacherId == t.Id && a.Covers(date, period))
                    && AvailabilitySlot.At(t.Profile?.Availability ?? [], date.DayOfWeek, period) != AvailabilityKind.Unavailable)
                .ToList();
            var atSchool = names.Where(t => periodsOf[t.Id].Any(p => p < period) && periodsOf[t.Id].Any(p => p > period))
                .Select(t => PersonNames.Display(t.Person, t.Username)).Order(collator).ToList();
            return new FreeTeachersDto(period, atSchool, names.Count - atSchool.Count);
        }).ToList();
        var classes = await db.SchoolClasses.Select(c => new DayClassDto(c.Id, c.Name)).ToListAsync();

        return new SubstitutionDayDto(
            date,
            await db.TimetableVersionOnAsync(date) is not null,
            day?.PublishedAt,
            day is null ? prepared.Count > 0 : !day.Changes.SequenceEqual(prepared),
            [.. absences.Select(a => new TeacherAbsenceDto(
                a.Id, a.TeacherId,
                absentNames.FirstOrDefault(u => u.Id == a.TeacherId) is { } u ? PersonNames.Display(u.Person, u.Username) : "",
                a.DateFrom, a.DateTo, a.PeriodFrom, a.PeriodTo))],
            [.. lessons.Select(l =>
            {
                var subject = names.Subjects.GetValueOrDefault(l.SubjectId);
                var original = l.Original;
                var shown = original is not null && !l.Gone;
                return new SubstitutionLessonDto(
                    l.Key.Period, l.Key.ClassId, l.Key.GroupId, names.Who(l.Key),
                    l.SubjectId, subject.Abbreviation ?? subject.Name ?? "", l.TeacherId, l.TeacherId is null ? null : names.Teacher(l.TeacherId),
                    l.ClassroomId, l.ClassroomId is { } room ? names.Classrooms.GetValueOrDefault(room) : null,
                    original?.SubjectId, original?.TeacherId,
                    original?.TeacherId is { } t && absences.Any(a => a.TeacherId == t && a.Covers(original.Key.Date, original.Key.Period)),
                    NeedsCover(l, absences), l.Gone, l.Changed && LessonChangeViews.Label(l, names) is { Length: > 0 } label ? label : null,
                    l.Change is { } change ? ids.GetValueOrDefault(change.Key) : null, l.Change, l.MovedIn,
                    l.ClassIds, l.Key.GroupId is { } g && names.Groups.TryGetValue(g, out var group) ? group.Name : null,
                    shown && original!.SubjectId != l.SubjectId ? names.Subject(original.SubjectId) : null,
                    shown && original!.TeacherId != l.TeacherId && original.TeacherId is not null ? names.Teacher(original.TeacherId) : null,
                    shown && original!.ClassroomId != l.ClassroomId && original.ClassroomId is { } was
                        ? names.Classrooms.GetValueOrDefault(was) : null);
            })],
            problems, periods, [.. classes.OrderBy(c => c.Name, collator)], free);
    }
}

public record TeacherAbsenceRequest(
    int TeacherId, DateOnly DateFrom, DateOnly DateTo, int? PeriodFrom = null, int? PeriodTo = null,
    /// <summary>The day the agenda shows afterwards; the first day by default.</summary>
    DateOnly? Day = null);

public record TeacherAbsenceDto(
    int Id, int TeacherId, string TeacherName, DateOnly DateFrom, DateOnly DateTo, int? PeriodFrom, int? PeriodTo);

/// <param name="Extra">A lesson the timetable doesn't have; then a subject is required.</param>
/// <param name="TeacherId">Null = the timetable's; likewise the subject and the classroom.</param>
public record LessonChangeRequest(
    DateOnly Date, int PeriodNumber, int? SchoolClassId = null, int? StudentGroupId = null, bool Extra = false,
    bool Cancelled = false, int? TeacherId = null, int? SubjectId = null, int? ClassroomId = null,
    DateOnly? MovedToDate = null, int? MovedToPeriod = null, int? MergedWithClassId = null, int? MergedWithGroupId = null);

/// <param name="Unpublished">The prepared changes differ from those published (or none were yet).</param>
public record SubstitutionDayDto(
    DateOnly Date, bool HasTimetable, DateTimeOffset? PublishedAt, bool Unpublished,
    IReadOnlyList<TeacherAbsenceDto> Absences, IReadOnlyList<SubstitutionLessonDto> Lessons, IReadOnlyList<SubstitutionProblemDto> Problems,
    IReadOnlyList<int> Periods, IReadOnlyList<DayClassDto> Classes, IReadOnlyList<FreeTeachersDto> Free);

public record DayClassDto(int Id, string Name);

/// <summary>Who's free at the period: those teaching before and after it (in school) by name, the others counted.</summary>
public record FreeTeachersDto(int Period, IReadOnlyList<string> AtSchool, int Others);

/// <param name="ChangeId">The change it's about, to undo it.</param>
public record SubstitutionProblemDto(string Text, int? ChangeId);

/// <summary>A lesson of the day as prepared; a moved one at its new period
/// (<paramref name="MovedIn"/>) as well as gone from the old one.</summary>
/// <param name="OriginalTeacherAbsent">The timetable's teacher is away then.</param>
/// <param name="NeedsCover">Its teacher is away and nobody substitutes.</param>
/// <param name="Change">What differs, with which lesson of the timetable it is.</param>
public record SubstitutionLessonDto(
    int PeriodNumber, int? SchoolClassId, int? StudentGroupId, string Who,
    int SubjectId, string Subject, int? TeacherId, string? Teacher, int? ClassroomId, string? Classroom,
    int? OriginalSubjectId, int? OriginalTeacherId, bool OriginalTeacherAbsent, bool NeedsCover, bool Gone,
    string? Label, int? ChangeId, LessonChangeData? Change, bool MovedIn,
    IReadOnlyList<int> ClassIds, string? GroupName,
    /// <summary>The timetable's subject when another is taught; likewise the teacher and the classroom.</summary>
    string? WasSubject, string? WasTeacher, string? WasClassroom);

public record SubstituteDto(int TeacherId, string Name, bool Qualified, bool TeachesThatDay, int SubstitutionsThisMonth);

/// <param name="Suits">One the subject uses.</param>
public record FreeClassroomDto(int Id, string Name, bool Suits);

public record SubstitutionCandidatesDto(IReadOnlyList<SubstituteDto> Teachers, IReadOnlyList<FreeClassroomDto> Classrooms);

/// <param name="Unfilled">Lessons nobody could take.</param>
public record SubstitutionFillDto(SubstitutionDayDto Day, int Unfilled);
