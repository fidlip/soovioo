using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Attendance;

/// <summary>What an omluvenka does to the days it covers, and whom it tells.
/// Adds and changes entities; the caller saves.</summary>
public class ExcuseBook(AppDbContext db, Notifier notifier, TimeProvider clock)
{
    public const int MaxDays = 92;

    public DateOnly Today => SchoolClock.Today(clock);

    public static ExcuseStatus StatusOf(IReadOnlyCollection<AbsenceStatus> days) =>
        days.Any(d => d == AbsenceStatus.PendingExcuse) || days.Count == 0 ? ExcuseStatus.Pending
        : days.All(d => d == AbsenceStatus.Excused) ? ExcuseStatus.Approved
        : days.All(d => d == AbsenceStatus.Rejected) ? ExcuseStatus.Rejected
        : ExcuseStatus.PartlyApproved;

    /// <summary>Brings the omluvenka's days in line with its range: its days
    /// waiting outside it are withdrawn, school days in it are added, or
    /// joined when the teacher already recorded them (a weekend day only
    /// then). A day already excused, rejected or under another omluvenka is
    /// skipped.
    /// Returns the skipped days and the days the omluvenka now waits for.
    /// Staff withdraw decided days outside the range too.</summary>
    public async Task<(List<SkippedDayDto> Skipped, List<DateOnly> Days)> ApplyAsync(
        Excuse excuse, StudentProfile student, int userId, bool withdrawDecided = false)
    {
        var family = await FamilyAsync(student.Id);
        var excuseId = excuse.Id;
        var records = await db.Absences
            .Where(a => a.StudentId == student.Id
                && ((a.Date >= excuse.DateFrom && a.Date <= excuse.DateTo) || (excuseId != 0 && a.ExcuseId == excuseId)))
            .ToListAsync();
        var days = new List<DateOnly>();
        for (var day = excuse.DateFrom; day <= excuse.DateTo; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || records.Any(a => a.Date == day))
            {
                days.Add(day);
            }
        }
        var mine = records.Where(a => excuseId != 0 && a.ExcuseId == excuseId).ToList();
        foreach (var absence in mine.Where(a => (withdrawDecided || a.Status == AbsenceStatus.PendingExcuse) && !days.Contains(a.Date)))
        {
            Withdraw(absence, family);
        }

        var timetable = excuse.Periods.Count > 0 ? [] : await TimetablePeriodsAsync(student, excuse.DateFrom, excuse.DateTo);
        List<int> PeriodsOn(DateOnly day) => excuse.Periods.Count > 0 ? [.. excuse.Periods] : timetable.GetValueOrDefault(day) ?? [];
        var skipped = new List<SkippedDayDto>();
        var waiting = new List<DateOnly>();
        foreach (var day in days)
        {
            var onDay = records.Where(a => a.Date == day).ToList();
            var own = onDay.Where(mine.Contains).ToList();
            if (own.Count > 0)
            {
                foreach (var absence in own.Where(a => a.Status == AbsenceStatus.PendingExcuse))
                {
                    if (MadeByFamily(absence, family))
                    {
                        absence.Periods = PeriodsOn(day);
                    }
                    waiting.Add(day);
                }
                continue;
            }
            var open = onDay
                .Where(a => a.ExcuseId == null && a.Status is AbsenceStatus.Unexcused or AbsenceStatus.PendingExcuse)
                .ToList();
            if (open.Count > 0)
            {
                foreach (var absence in open)
                {
                    absence.Excuse = excuse;
                    absence.Status = AbsenceStatus.PendingExcuse;
                    absence.ReviewNote = null;
                }
                waiting.Add(day);
                continue;
            }
            if (onDay.Count > 0)
            {
                skipped.Add(new SkippedDayDto(day, onDay[0].Status));
                continue;
            }
            db.Absences.Add(new Absence
            {
                SchoolId = excuse.SchoolId,
                StudentId = student.Id,
                Date = day,
                Periods = PeriodsOn(day),
                Status = AbsenceStatus.PendingExcuse,
                CreatedById = userId,
                Excuse = excuse,
            });
            waiting.Add(day);
        }
        return (skipped, [.. waiting.Distinct()]);
    }

    /// <summary>A day the family made goes; one the teacher recorded is
    /// unexcused again, without the omluvenka.</summary>
    public void Withdraw(Absence absence, HashSet<int> family)
    {
        if (MadeByFamily(absence, family))
        {
            db.Absences.Remove(absence);
            return;
        }
        absence.Excuse = null;
        absence.ExcuseId = null;
        absence.Status = AbsenceStatus.Unexcused;
        absence.ReviewNote = null;
    }

    public async Task<HashSet<int>> FamilyAsync(int studentId) => (await notifier.FamilyAsync(studentId)).ToHashSet();

    /// <summary>Approves the days up to today (later ones only on their
    /// day), or rejects them with the note. While days wait only those are
    /// decided; on a decided omluvenka the decision changes. Returns the
    /// days decided.</summary>
    public static List<Absence> Decide(IEnumerable<Absence> days, bool approve, string? note, DateOnly today)
    {
        var all = days.ToList();
        var pool = all.Any(a => a.Status == AbsenceStatus.PendingExcuse)
            ? all.Where(a => a.Status == AbsenceStatus.PendingExcuse)
            : all;
        var decided = pool
            .Where(a => approve ? a.Status != AbsenceStatus.Excused && a.Date <= today : a.Status != AbsenceStatus.Unexcused)
            .OrderBy(a => a.Date)
            .ToList();
        foreach (var absence in decided)
        {
            absence.Status = approve ? AbsenceStatus.Excused : AbsenceStatus.Rejected;
            absence.ReviewNote = approve ? null : note;
        }
        return decided;
    }

    /// <summary>The decided days wait again — the family may change the
    /// omluvenka. Returns those days.</summary>
    public static List<Absence> Reopen(IEnumerable<Absence> days)
    {
        var reopened = days
            .Where(a => a.Status is AbsenceStatus.Excused or AbsenceStatus.Rejected)
            .OrderBy(a => a.Date)
            .ToList();
        foreach (var absence in reopened)
        {
            absence.Status = AbsenceStatus.PendingExcuse;
            absence.ReviewNote = null;
        }
        return reopened;
    }

    public static string DecidedTitle(bool approve) => approve ? "Omluvenka schválena" : "Omluvenka zamítnuta";

    /// <summary>Tells the family what the school did with these days of the
    /// omluvenka.</summary>
    public async Task TellFamilyAsync(Excuse excuse, IReadOnlyList<Absence> days, string title, string? note, int exceptUserId)
    {
        if (days.Count == 0)
        {
            return;
        }
        var name = await db.Students.Where(s => s.Id == excuse.StudentId)
            .Select(s => s.Person!.FirstName + " " + s.Person.LastName).FirstAsync();
        var body = $"{name} — {Range(days[0].Date, days[^1].Date)}{(note is null ? "" : $": {note}")}";
        await notifier.NotifyAsync(
            excuse.SchoolId, await notifier.FamilyAsync(excuse.StudentId), NotificationType.Excuse,
            title, Notifier.Preview(body), "absences", excuse.Id, excuse.StudentId, exceptUserId);
    }

    /// <summary>Tells the class teacher of a new or changed omluvenka. On a
    /// day of it the class teacher is away, also whoever teaches their
    /// lessons in the class then, by the published substitutions — the rest
    /// once the day is published (<see cref="PublishedAsync"/>). Those hear
    /// of an omluvenka once.</summary>
    public async Task TellTeachersAsync(Excuse excuse, IReadOnlyCollection<DateOnly> days, bool edited, int exceptUserId) =>
        await SendAsync(excuse, days, [], classTeacher: true, edited, exceptUserId);

    /// <summary>The day's substitutions were just published (and saved).</summary>
    public async Task PublishedAsync(DateOnly date)
    {
        var excuses = await db.Excuses
            .Where(e => e.Absences.Any(a => a.Date == date && a.Status == AbsenceStatus.PendingExcuse))
            .ToListAsync();
        var lessons = new Dictionary<DateOnly, List<EffectiveLesson>>();
        foreach (var excuse in excuses)
        {
            await SendAsync(excuse, [date], lessons, classTeacher: false, edited: false, null);
        }
    }

    private async Task SendAsync(
        Excuse excuse, IReadOnlyCollection<DateOnly> days, Dictionary<DateOnly, List<EffectiveLesson>> lessons,
        bool classTeacher, bool edited, int? exceptUserId)
    {
        var student = await db.Students
            .Where(s => s.Id == excuse.StudentId)
            .Select(s => new { s.SchoolClassId, s.SchoolClass!.ClassTeacherId, s.Person!.FirstName, s.Person.LastName })
            .FirstOrDefaultAsync();
        if (student?.ClassTeacherId is not { } teacher || days.Count == 0)
        {
            return;
        }

        var standIns = new HashSet<int>();
        var first = days.Min();
        var last = days.Max();
        var away = await db.TeacherAbsences
            .Where(a => a.TeacherId == teacher && a.DateFrom <= last && a.DateTo >= first)
            .ToListAsync();
        foreach (var day in days.Where(d => away.Any(a => a.DateFrom <= d && d <= a.DateTo)))
        {
            if (!lessons.TryGetValue(day, out var ofDay))
            {
                ofDay = await LessonChanges.OnAsync(db, day, day);
                lessons[day] = ofDay;
            }
            standIns.UnionWith(ofDay
                .Where(l => !l.Gone && l.Original?.TeacherId == teacher && l.ClassIds.Contains(student.SchoolClassId))
                .Select(l => (l.MergedInto ?? l).TeacherId)
                .Where(t => t is not null && t != teacher)
                .Select(t => t!.Value));
        }
        var told = await db.Notifications
            .Where(n => n.Type == NotificationType.NewExcuse && n.TargetId == excuse.Id)
            .Select(n => n.UserId)
            .ToListAsync();
        standIns.ExceptWith(told);
        var recipients = standIns;
        if (classTeacher && (edited || !told.Contains(teacher)))
        {
            recipients.Add(teacher);
        }
        if (recipients.Count == 0)
        {
            return;
        }
        await notifier.NotifyAsync(
            excuse.SchoolId, recipients, NotificationType.NewExcuse,
            $"{(edited ? "Upravená omluvenka" : "Nová omluvenka")} — {student.FirstName} {student.LastName}",
            Notifier.Preview($"{Range(excuse.DateFrom, excuse.DateTo)}: {excuse.Reason}"), "absences",
            excuse.Id, excuse.StudentId, exceptUserId);
    }

    /// <summary>An omluvenka with no day left goes, with its files. True when it went.</summary>
    public async Task<bool> DropIfEmptyAsync(int excuseId, Messaging.MessageAttachmentStorageService storage)
    {
        if (await db.Absences.AnyAsync(a => a.ExcuseId == excuseId)
            || await db.Excuses.Include(e => e.Attachments).FirstOrDefaultAsync(e => e.Id == excuseId) is not { } excuse)
        {
            return false;
        }
        var files = excuse.Attachments.Select(a => a.StoragePath).ToList();
        db.Excuses.Remove(excuse);
        await db.SaveChangesAsync();
        foreach (var path in files)
        {
            storage.Delete(path);
        }
        return true;
    }

    public static string Range(DateOnly from, DateOnly to) =>
        from == to ? SchoolFormat.Date(from) : $"{SchoolFormat.Date(from)} – {SchoolFormat.Date(to)}";

    private static bool MadeByFamily(Absence absence, HashSet<int> family) =>
        absence.CreatedById is { } by && family.Contains(by);

    /// <summary>The periods the pupil has lessons on each day — of their
    /// class and their groups, with the published substitutions.</summary>
    private async Task<Dictionary<DateOnly, List<int>>> TimetablePeriodsAsync(StudentProfile student, DateOnly from, DateOnly to)
    {
        var groups = await db.StudentGroupMembers.Where(m => m.StudentId == student.Id).Select(m => m.StudentGroupId).ToListAsync();
        return (await LessonChanges.OnAsync(db, from, to))
            .Where(l => !l.Gone && (l.Key.GroupId is { } g ? groups.Contains(g) : l.Key.ClassId == student.SchoolClassId))
            .GroupBy(l => l.Key.Date)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Key.Period).Distinct().Order().ToList());
    }
}
