using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>A lesson of the timetable on a day: once for a group, with all
/// its classes.</summary>
public record BaseLesson(LessonKey Key, IReadOnlyList<int> ClassIds, int SubjectId, int? TeacherId, int? ClassroomId);

/// <summary>A lesson as it is taught on a day, with the changes.</summary>
public sealed class EffectiveLesson
{
    /// <summary>Where it's taught: a moved lesson at its new period.</summary>
    public required LessonKey Key { get; init; }
    public required IReadOnlyList<int> ClassIds { get; init; }
    public int SubjectId { get; set; }
    public int? TeacherId { get; set; }
    public int? ClassroomId { get; set; }

    /// <summary>Null = an extra lesson.</summary>
    public BaseLesson? Original { get; init; }
    public LessonChangeData? Change { get; init; }

    /// <summary>The lesson at its new period; its old one is <see cref="Gone"/>.</summary>
    public bool MovedIn { get; init; }

    public EffectiveLesson? MergedInto { get; set; }
    public List<EffectiveLesson> MergedFrom { get; } = [];

    public bool Gone => Change is { Cancelled: true } || (Change?.MovedTo is not null && !MovedIn);

    public bool Changed => Change is not null || MergedFrom.Count > 0;
}

/// <summary>The lessons of some days with the changes of their weeks: from
/// the published days for everyone, from the agenda for who prepares them.
/// A lesson moves only within its week, so a week's changes are all there is
/// to know about its days.</summary>
public static class LessonChanges
{
    public static WeekParity ParityOf(DateOnly date) =>
        ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)) % 2 == 1 ? WeekParity.Odd : WeekParity.Even;

    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Changes of a lesson that isn't in the timetable (any more) are left out.</summary>
    public static List<EffectiveLesson> Apply(
        IEnumerable<BaseLesson> lessons, IEnumerable<LessonChangeData> changes,
        IReadOnlyDictionary<int, IReadOnlyList<int>> groupClasses)
    {
        var byKey = changes.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Last());
        var result = new List<EffectiveLesson>();
        foreach (var lesson in lessons)
        {
            var change = byKey.GetValueOrDefault(lesson.Key) is { Extra: false } c && c.Fits(lesson) ? c : null;
            result.Add(new EffectiveLesson
            {
                Key = lesson.Key, ClassIds = lesson.ClassIds, Original = lesson, Change = change,
                SubjectId = change?.MovedTo is null ? change?.SubjectId ?? lesson.SubjectId : lesson.SubjectId,
                TeacherId = change?.MovedTo is null ? change?.TeacherId ?? lesson.TeacherId : lesson.TeacherId,
                ClassroomId = change?.MovedTo is null ? change?.ClassroomId ?? lesson.ClassroomId : lesson.ClassroomId,
            });
            if (change?.MovedTo is { } to && !change.Cancelled)
            {
                result.Add(new EffectiveLesson
                {
                    Key = to, ClassIds = lesson.ClassIds, Original = lesson, Change = change, MovedIn = true,
                    SubjectId = change.SubjectId ?? lesson.SubjectId,
                    TeacherId = change.TeacherId ?? lesson.TeacherId,
                    ClassroomId = change.ClassroomId ?? lesson.ClassroomId,
                });
            }
        }
        foreach (var extra in byKey.Values.Where(c => c.Extra && c.SubjectId is not null))
        {
            IReadOnlyList<int> classIds = extra.ClassId is { } classId ? [classId]
                : groupClasses.GetValueOrDefault(extra.GroupId ?? 0) ?? [];
            result.Add(new EffectiveLesson
            {
                Key = extra.Key, ClassIds = classIds, Change = extra,
                SubjectId = extra.SubjectId!.Value, TeacherId = extra.TeacherId, ClassroomId = extra.ClassroomId,
            });
        }
        foreach (var lesson in result.Where(l => l.Change?.MergedWith is not null && !l.Gone && !l.MovedIn).ToList())
        {
            var target = result.FirstOrDefault(t => t != lesson && t.Key == lesson.Change!.MergedWith && !t.Gone
                && t.Change?.MergedWith is null);
            if (target is null)
            {
                continue;
            }
            lesson.MergedInto = target;
            target.MergedFrom.Add(lesson);
            lesson.SubjectId = target.SubjectId;
            lesson.TeacherId = target.TeacherId;
            lesson.ClassroomId = target.ClassroomId;
        }
        return [.. result.OrderBy(l => l.Key.Date).ThenBy(l => l.Key.Period)];
    }

    /// <summary>The lessons of the published versions valid on the days.</summary>
    public static async Task<List<BaseLesson>> BaseLessonsAsync(AppDbContext db, DateOnly from, DateOnly to)
    {
        var versions = await db.TimetableVersions
            .Where(v => v.Status == TimetableVersionStatus.Published && v.ValidFrom <= to && (v.ValidTo == null || v.ValidTo >= from))
            .Select(v => new { v.Id, v.ValidFrom, v.ValidTo })
            .ToListAsync();
        var ids = versions.Select(v => v.Id).ToList();
        var entries = await db.TimetableEntries.Where(e => ids.Contains(e.TimetableVersionId))
            .Select(e => new
            {
                e.TimetableVersionId, e.SchoolClassId, e.StudentGroupId, e.SubjectId, e.TeacherId, e.ClassroomId,
                e.DayOfWeek, e.PeriodNumber, e.WeekParity,
            })
            .ToListAsync();
        var lessons = new List<BaseLesson>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var version = versions.FirstOrDefault(v => v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo >= date));
            if (version is null)
            {
                continue;
            }
            var parity = ParityOf(date);
            lessons.AddRange(entries
                .Where(e => e.TimetableVersionId == version.Id && e.DayOfWeek == date.DayOfWeek
                    && (e.WeekParity == WeekParity.Every || e.WeekParity == parity))
                .GroupBy(e => e.StudentGroupId is { } g ? (0, g, e.PeriodNumber) : (e.SchoolClassId, 0, e.PeriodNumber))
                .Select(g =>
                {
                    var first = g.First();
                    return new BaseLesson(
                        new LessonKey(date, first.PeriodNumber, first.StudentGroupId is null ? first.SchoolClassId : null, first.StudentGroupId),
                        [.. g.Select(e => e.SchoolClassId).Distinct()], first.SubjectId, first.TeacherId, first.ClassroomId);
                }));
        }
        return lessons;
    }

    public static async Task<List<LessonChangeData>> PublishedAsync(AppDbContext db, DateOnly from, DateOnly to) =>
        [.. (await db.SubstitutionDays.Where(d => d.Date >= from && d.Date <= to).ToListAsync()).SelectMany(d => d.Changes)];

    public static async Task<List<LessonChangeData>> PreparedAsync(AppDbContext db, DateOnly from, DateOnly to) =>
        [.. (await db.LessonChanges.Where(c => c.Date >= from && c.Date <= to).ToListAsync()).Select(c => c.Data())];

    public static async Task<Dictionary<int, IReadOnlyList<int>>> GroupClassesAsync(AppDbContext db, IEnumerable<LessonChangeData> changes)
    {
        var ids = changes.Where(c => c.GroupId is not null).Select(c => c.GroupId!.Value).Distinct().ToList();
        return (await db.StudentGroupClasses.Where(c => ids.Contains(c.StudentGroupId))
                .Select(c => new { c.StudentGroupId, c.SchoolClassId }).ToListAsync())
            .GroupBy(c => c.StudentGroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(c => c.SchoolClassId)]);
    }

    /// <summary>The lessons of the days <paramref name="from"/> to <paramref name="to"/>,
    /// with the changes of their weeks: the published ones, or the agenda's.</summary>
    public static async Task<List<EffectiveLesson>> OnAsync(
        AppDbContext db, DateOnly from, DateOnly to, bool prepared = false,
        Func<List<LessonChangeData>, List<LessonChangeData>>? adjust = null)
    {
        var weekFrom = MondayOf(from);
        var weekTo = MondayOf(to).AddDays(6);
        var changes = prepared ? await PreparedAsync(db, weekFrom, weekTo) : await PublishedAsync(db, weekFrom, weekTo);
        if (adjust is not null)
        {
            changes = adjust(changes);
        }
        var lessons = await BaseLessonsAsync(db, weekFrom, weekTo);
        return [.. Apply(lessons, changes, await GroupClassesAsync(db, changes))
            .Where(l => l.Key.Date >= from && l.Key.Date <= to)];
    }

    /// <summary>The published lessons a teacher teaches on the day other than
    /// the timetable has it — substituting, or a class merged into theirs.</summary>
    public static async Task<List<EffectiveLesson>> SubstitutingAsync(AppDbContext db, int teacherId, DateOnly day)
    {
        var monday = MondayOf(day);
        var changes = await PublishedAsync(db, monday, monday.AddDays(6));
        if (!changes.Any(c => c.Date == day || c.MovedToDate == day))
        {
            return [];
        }
        return [.. (await OnAsync(db, day, day))
            .Where(l => !l.Gone && l.TeacherId == teacherId && l.Original?.TeacherId != teacherId)];
    }
}

/// <summary>Names of what lessons refer to, loaded once.</summary>
public class LessonNames
{
    public required Dictionary<int, (string Name, string? Abbreviation)> Subjects { get; init; }
    public required Dictionary<int, string> Teachers { get; init; }
    public required Dictionary<int, string> Classrooms { get; init; }
    public required Dictionary<int, string> Classes { get; init; }
    public required Dictionary<int, (string Name, string Classes)> Groups { get; init; }

    public static async Task<LessonNames> LoadAsync(AppDbContext db, IEnumerable<EffectiveLesson> lessons)
    {
        var list = lessons.ToList();
        var groupIds = list.Select(l => l.Key.GroupId).Concat(list.Select(l => l.Change?.MergedWithGroupId))
            .Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        var teacherIds = list.SelectMany(l => new[] { l.TeacherId, l.Original?.TeacherId })
            .Where(t => t is not null).Select(t => t!.Value).Distinct().ToList();
        var classes = await db.SchoolClasses.Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name);
        var groupClasses = (await db.StudentGroupClasses.Where(c => groupIds.Contains(c.StudentGroupId))
                .Select(c => new { c.StudentGroupId, c.SchoolClassId }).ToListAsync())
            .ToLookup(c => c.StudentGroupId, c => classes.GetValueOrDefault(c.SchoolClassId) ?? "");
        return new LessonNames
        {
            Subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => (s.Name, s.Abbreviation)),
            Teachers = (await db.Users.Where(u => teacherIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Person, u.Username }).ToListAsync())
                .ToDictionary(u => u.Id, u => PersonNames.Display(u.Person, u.Username)),
            Classrooms = await db.Classrooms.ToDictionaryAsync(r => r.Id, r => r.ShortName ?? r.Name),
            Classes = classes,
            Groups = await db.StudentGroups.Where(g => groupIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id, g => (g.Name, string.Join(", ", groupClasses[g.Id].Order(StringComparer.Ordinal)))),
        };
    }

    public string Teacher(int? id) => id is { } t ? Teachers.GetValueOrDefault(t) ?? "" : "";

    /// <summary>"6.A", or "Aj 1 (6.A, 6.B)" for a group.</summary>
    public string Who(LessonKey key) => key.GroupId is { } g
        ? Groups.TryGetValue(g, out var group) ? $"{group.Name} ({group.Classes})" : ""
        : Classes.GetValueOrDefault(key.ClassId ?? 0) ?? "";

    public string Subject(int id) => Subjects.TryGetValue(id, out var s) ? s.Abbreviation ?? s.Name : "";
}
