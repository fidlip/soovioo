using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary><see cref="TimetableGenerationMode.Update"/>: for each row of the
/// plan, which of a version's unlocked lessons stay fixed, which the generator
/// can move and which go; afterwards what it placed written over them, so
/// lessons that haven't changed stay untouched.</summary>
public sealed class TimetableUpdate
{
    /// <summary>What a whole class's or a group's lessons should be.</summary>
    public record Wanted(int? GroupId, int? TeacherId, IReadOnlyList<int> Rooms, IReadOnlyList<int> ClassIds);

    /// <param name="Hours">Hours for the generator to place, those at
    /// <paramref name="Homes"/> included.</param>
    public record Sorted(List<TimetableEntry> Kept, List<TimetableGenerator.Home> Homes, decimal Hours);

    /// <param name="Count">Lessons changed.</param>
    public record Changes(int Count, List<string> Notes);

    private readonly TimetableUpdateMoves moves;
    private readonly TimetableUpdateMismatches mismatches;
    private readonly IReadOnlyList<TimetableEntry> entries;
    private readonly IReadOnlyDictionary<int, TimetableGenerator.Teacher> teachers;
    private readonly IReadOnlyDictionary<int, string> teacherNames;
    private readonly IReadOnlyDictionary<int, string> roomNames;
    private readonly HashSet<TimetableEntry> sorted = [];

    /// <summary>A teacher's lessons over their wished most a day.</summary>
    private readonly HashSet<(int Teacher, DayOfWeek Day, int Period)> overDaily = [];

    /// <summary>What was left at odds with the plan.</summary>
    public List<string> Messages { get; } = [];

    public TimetableUpdate(
        TimetableUpdateMoves moves, TimetableUpdateMismatches mismatches, IReadOnlyList<TimetableEntry> entries,
        IReadOnlyDictionary<int, TimetableGenerator.Teacher> teachers,
        IReadOnlyDictionary<int, string> teacherNames, IReadOnlyDictionary<int, string> roomNames)
    {
        this.moves = moves;
        this.mismatches = mismatches;
        this.entries = entries;
        this.teachers = teachers;
        this.teacherNames = teacherNames;
        this.roomNames = roomNames;
        foreach (var byDay in entries.Where(e => e.TeacherId is not null).GroupBy(e => (Teacher: e.TeacherId!.Value, e.DayOfWeek)))
        {
            if (teachers.GetValueOrDefault(byDay.Key.Teacher)?.Wishes?.MaxLessonsPerDay is not { } max)
            {
                continue;
            }
            var slots = byDay.DistinctBy(e => e.PeriodNumber).ToList();
            foreach (var e in slots.OrderBy(e => e.Locked).ThenByDescending(e => e.PeriodNumber).Take(slots.Count - max))
            {
                overDaily.Add((byDay.Key.Teacher, byDay.Key.DayOfWeek, e.PeriodNumber));
            }
        }
    }

    public static decimal HoursOf(IEnumerable<TimetableEntry> lessons) => lessons
        .Select(e => (e.DayOfWeek, e.PeriodNumber, e.WeekParity)).Distinct()
        .Sum(e => Weight(e.WeekParity));

    private static decimal Weight(WeekParity parity) => parity == WeekParity.Every ? 1m : 0.5m;

    /// <param name="who">"5.A M"</param>
    /// <param name="lessons">Its unlocked lessons, of a group one per class.</param>
    /// <param name="hours">Hours the plan wants on top of its locked lessons.</param>
    public Sorted Sort(string who, IReadOnlyList<TimetableEntry> lessons, decimal hours, IReadOnlyList<Wanted> wanted)
    {
        sorted.UnionWith(lessons);
        var kept = new List<TimetableEntry>();
        var homes = new List<TimetableGenerator.Home>();
        var left = new List<(string What, WeekParity Parity)>();
        var candidates = new List<(List<TimetableEntry> Rows, bool Affected)>();
        foreach (var slot in lessons.GroupBy(e => (e.DayOfWeek, e.PeriodNumber, e.WeekParity)))
        {
            var rows = slot.ToList();
            // Nobody to replace them with.
            if (rows.Any(e => e.TeacherId is not null && WantedOf(wanted, e)?.TeacherId is null))
            {
                kept.AddRange(rows);
                continue;
            }
            var differing = Differences(rows, wanted);
            if (differing.Count > 0 && mismatches == TimetableUpdateMismatches.Leave)
            {
                kept.AddRange(rows);
                left.AddRange(differing.Select(what => (what, slot.Key.WeekParity)));
                continue;
            }
            candidates.Add((rows, differing.Count > 0 || Lacking(rows, wanted) || Unsuitable(rows, wanted)));
        }

        var remaining = hours - HoursOf(kept);
        var toPlace = remaining;
        foreach (var (rows, affected) in candidates.OrderBy(c => c.Affected).ThenBy(c => c.Rows[0].PeriodNumber))
        {
            var parity = rows[0].WeekParity;
            if (Weight(parity) > remaining)
            {
                if (mismatches == TimetableUpdateMismatches.Leave)
                {
                    kept.AddRange(rows);
                    left.Add(("o {0} víc, než je v plánu", parity));
                }
                continue;
            }
            remaining -= Weight(parity);
            if (!affected && (moves == TimetableUpdateMoves.AffectedOnly || parity != WeekParity.Every))
            {
                kept.AddRange(rows);
                toPlace -= Weight(parity);
            }
            else if (parity == WeekParity.Every)
            {
                homes.AddRange(rows.DistinctBy(e => e.StudentGroupId)
                    .Select(e => new TimetableGenerator.Home(e.DayOfWeek, e.PeriodNumber, e.StudentGroupId, e.ClassroomId)));
            }
        }
        Messages.AddRange(left.GroupBy(l => l.What)
            .Select(g => $"{who}: {string.Format(g.Key, TeachingPlanner.FormatHours(g.Sum(l => Weight(l.Parity))))}, ponecháno."));
        return new Sorted(kept, homes, toPlace);
    }

    private static Wanted? WantedOf(IReadOnlyList<Wanted> wanted, TimetableEntry e) =>
        wanted.FirstOrDefault(w => w.GroupId == e.StudentGroupId);

    /// <summary>How a lesson is at odds with the plan and the přidělení; {0} = its hours.</summary>
    private List<string> Differences(List<TimetableEntry> rows, IReadOnlyList<Wanted> wanted)
    {
        var differences = new List<string>();
        foreach (var byGroup in rows.GroupBy(e => e.StudentGroupId))
        {
            var e = byGroup.First();
            var w = WantedOf(wanted, e);
            if (w is null)
            {
                continue;
            }
            if (e.TeacherId is { } teacher && w.TeacherId is { } assigned && teacher != assigned)
            {
                differences.Add($"učí {teacherNames.GetValueOrDefault(teacher)}, přiděleno je {teacherNames.GetValueOrDefault(assigned)} ({{0}})");
            }
            if (e.ClassroomId is { } room && w.Rooms.Count > 0 && !w.Rooms.Contains(room))
            {
                differences.Add($"učebna {roomNames.GetValueOrDefault(room)} není mezi učebnami předmětu ({{0}})");
            }
            if (!byGroup.Select(r => r.SchoolClassId).ToHashSet().SetEquals(w.ClassIds))
            {
                differences.Add("v rozvrhu jsou jiné třídy než ve skupině ({0})");
            }
        }
        return differences;
    }

    /// <summary>A teacher or a classroom yet to fill in, or a group of the division not there.</summary>
    private static bool Lacking(List<TimetableEntry> rows, IReadOnlyList<Wanted> wanted) =>
        wanted.Any(w => rows.All(e => e.StudentGroupId != w.GroupId))
        || rows.Any(e => WantedOf(wanted, e) is { } w
            && ((e.TeacherId is null && w.TeacherId is not null) || (e.ClassroomId is null && w.Rooms.Count > 0)));

    /// <summary>Its teacher can't then — or, when only such lessons move, would rather not.</summary>
    private bool Unsuitable(List<TimetableEntry> rows, IReadOnlyList<Wanted> wanted)
    {
        var slot = rows[0];
        return wanted.Where(w => w.TeacherId is not null).Select(w => w.TeacherId!.Value).Any(id =>
        {
            var availability = teachers.GetValueOrDefault(id)?.Availability ?? [];
            var kind = AvailabilitySlot.At(availability, slot.DayOfWeek, slot.PeriodNumber);
            return kind == AvailabilityKind.Unavailable
                || (moves == TimetableUpdateMoves.AffectedOnly
                    && (kind == AvailabilityKind.Avoid || overDaily.Contains((id, slot.DayOfWeek, slot.PeriodNumber))));
        });
    }

    /// <summary>Unlocked lessons no row of the plan wants: those to keep.</summary>
    public List<TimetableEntry> Unplanned(Func<TimetableEntry, string> who)
    {
        var unplanned = entries.Where(e => !e.Locked && !sorted.Contains(e)).ToList();
        if (mismatches != TimetableUpdateMismatches.Leave)
        {
            return [];
        }
        Messages.AddRange(unplanned.GroupBy(who)
            .Select(g => $"{g.Key}: není v plánu ({TeachingPlanner.FormatHours(HoursOf(g))}), ponecháno."));
        return unplanned;
    }

    /// <summary>Writes what was placed over <paramref name="replaced"/>: a
    /// lesson at the same time stays with its teacher and classroom updated.</summary>
    public static Changes Apply(
        AppDbContext db, List<TimetableEntry> replaced, List<TimetableEntry> placed, Func<TimetableEntry, string> who)
    {
        var removed = new List<TimetableEntry>(replaced);
        var added = new List<TimetableEntry>();
        var otherTeacher = new List<TimetableEntry>();
        var otherRoom = new List<TimetableEntry>();
        foreach (var lesson in placed)
        {
            var same = removed.FirstOrDefault(e => e.SchoolClassId == lesson.SchoolClassId
                && e.StudentGroupId == lesson.StudentGroupId && e.SubjectId == lesson.SubjectId
                && e.DayOfWeek == lesson.DayOfWeek && e.PeriodNumber == lesson.PeriodNumber && e.WeekParity == lesson.WeekParity);
            if (same is null)
            {
                added.Add(lesson);
                continue;
            }
            removed.Remove(same);
            if (same.TeacherId != lesson.TeacherId)
            {
                otherTeacher.Add(same);
                same.TeacherId = lesson.TeacherId;
            }
            if (same.ClassroomId != lesson.ClassroomId)
            {
                otherRoom.Add(same);
                same.ClassroomId = lesson.ClassroomId;
            }
        }
        db.TimetableEntries.RemoveRange(removed);
        db.TimetableEntries.AddRange(added);

        // A group's lesson once, not once per class.
        static IEnumerable<TimetableEntry> Once(IEnumerable<TimetableEntry> lessons) => lessons
            .DistinctBy(e => (e.StudentGroupId, Class: e.StudentGroupId is null ? e.SchoolClassId : 0, e.SubjectId, e.DayOfWeek, e.PeriodNumber, e.WeekParity));
        var moved = new List<TimetableEntry>();
        var onlyAdded = new List<TimetableEntry>();
        var onlyRemoved = new List<TimetableEntry>();
        var taken = Once(removed).ToLookup(who);
        var given = Once(added).ToLookup(who);
        foreach (var name in taken.Select(g => g.Key).Union(given.Select(g => g.Key)))
        {
            var from = taken[name].ToList();
            var to = given[name].ToList();
            var both = Math.Min(from.Count, to.Count);
            moved.AddRange(to.Take(both));
            onlyAdded.AddRange(to.Skip(both));
            onlyRemoved.AddRange(from.Skip(both));
        }

        var notes = new List<string>();
        void Note(string what, List<TimetableEntry> lessons)
        {
            var once = Once(lessons).ToList();
            if (once.Count > 0)
            {
                notes.Add($"{what} {TeachingPlanner.FormatHours(once.Sum(e => Weight(e.WeekParity)))} ({string.Join(", ", once.Select(who).Distinct().Order())}).");
            }
        }
        Note("Přesunuto", moved);
        Note("Přidáno", onlyAdded);
        Note("Odebráno", onlyRemoved);
        Note("Změněn učitel:", otherTeacher);
        Note("Změněna učebna:", otherRoom);
        return new Changes(
            moved.Concat(onlyAdded).Concat(onlyRemoved).Concat(Once(otherTeacher)).Concat(Once(otherRoom)).Count(),
            notes.Count > 0 ? notes : ["V rozvrhu se nic nezměnilo."]);
    }
}
