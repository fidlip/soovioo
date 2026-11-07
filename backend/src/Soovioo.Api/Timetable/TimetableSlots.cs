using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

public enum DropSlotStatus
{
    Free,

    /// <summary>It can go there, against a wish or leaving a gap.</summary>
    Unsuitable,

    /// <summary>It can't go there.</summary>
    Blocked,
}

/// <param name="Reasons">Why it's unsuitable or blocked.</param>
public record DropSlot(DayOfWeek Day, int Period, DropSlotStatus Status, IReadOnlyList<string> Reasons);

/// <summary>Where a lesson being moved by hand can go, by the rules alone;
/// what it does to the timetable as a whole shows in its assessment.</summary>
public static class TimetableSlots
{
    /// <param name="ClassIds">Classes it takes.</param>
    /// <param name="GroupIds">Its groups, for pupils in common with other lessons.</param>
    /// <param name="Entries">Its lessons now, none when not in the timetable yet.</param>
    public record Moved(
        IReadOnlyList<int> ClassIds, IReadOnlyList<int> TeacherIds, IReadOnlyList<int> GroupIds, WeekParity Parity,
        IReadOnlyCollection<TimetableEntry> Entries);

    /// <param name="Members">Pupils of each group.</param>
    public record Week(
        IReadOnlyList<DayOfWeek> Days, IReadOnlyList<int> Periods, IReadOnlyList<TimetableEntry> Lessons,
        IReadOnlyDictionary<int, TimetableGenerator.Teacher> Teachers, IReadOnlyDictionary<int, string> TeacherNames,
        IReadOnlyDictionary<int, string> ClassNames, IReadOnlyDictionary<int, IReadOnlyCollection<int>> Members);

    public static List<DropSlot> Evaluate(Moved moved, Week week)
    {
        var others = week.Lessons.Except(moved.Entries).ToList();
        return [.. from day in week.Days from period in week.Periods select Slot(moved, week, others, day, period)];
    }

    private static DropSlot Slot(Moved moved, Week week, List<TimetableEntry> others, DayOfWeek day, int period)
    {
        var there = others.Where(e => e.DayOfWeek == day && e.PeriodNumber == period && Overlap(e.WeekParity, moved.Parity)).ToList();
        var blocked = new List<string>();
        foreach (var classId in moved.ClassIds.Where(c => there.Any(e => e.SchoolClassId == c && PupilsMeet(e, moved, week))))
        {
            blocked.Add($"{week.ClassNames.GetValueOrDefault(classId)} má v tu dobu jinou hodinu.");
        }
        foreach (var teacherId in moved.TeacherIds)
        {
            var name = week.TeacherNames.GetValueOrDefault(teacherId);
            if (there.Any(e => e.TeacherId == teacherId))
            {
                blocked.Add($"{name} v tu dobu učí.");
            }
            if (Availability(week, teacherId, day, period) == AvailabilityKind.Unavailable)
            {
                blocked.Add($"{name} v tu dobu nemůže.");
            }
        }
        if (blocked.Count > 0)
        {
            return new DropSlot(day, period, DropSlotStatus.Blocked, blocked);
        }

        var unsuitable = new List<string>();
        foreach (var teacherId in moved.TeacherIds)
        {
            var name = week.TeacherNames.GetValueOrDefault(teacherId);
            if (Availability(week, teacherId, day, period) == AvailabilityKind.Avoid)
            {
                unsuitable.Add($"{name} v tu dobu raději ne.");
            }
            if (week.Teachers.GetValueOrDefault(teacherId)?.Wishes?.MaxLessonsPerDay is { } max
                && others.Where(e => e.TeacherId == teacherId && e.DayOfWeek == day).Select(e => e.PeriodNumber).Append(period).Distinct().Count() > max)
            {
                unsuitable.Add($"{name} by měl víc než {max} h denně.");
            }
        }
        if (period < 1)
        {
            unsuitable.Add("Nultá hodina.");
        }
        foreach (var classId in moved.ClassIds)
        {
            var periods = others.Where(e => e.SchoolClassId == classId && e.DayOfWeek == day).Select(e => e.PeriodNumber).ToList();
            if (TimetableGenerator.Holes(periods.Append(period)) > TimetableGenerator.Holes(periods))
            {
                unsuitable.Add($"{week.ClassNames.GetValueOrDefault(classId)} by měla okno.");
            }
        }
        return new DropSlot(day, period, unsuitable.Count > 0 ? DropSlotStatus.Unsuitable : DropSlotStatus.Free, unsuitable);
    }

    private static AvailabilityKind? Availability(Week week, int teacherId, DayOfWeek day, int period) =>
        AvailabilitySlot.At(week.Teachers.GetValueOrDefault(teacherId)?.Availability ?? [], day, period);

    private static bool Overlap(WeekParity a, WeekParity b) =>
        a == WeekParity.Every || b == WeekParity.Every || a == b;

    /// <summary>Lessons of a class clash unless both are of groups without a
    /// pupil in common.</summary>
    private static bool PupilsMeet(TimetableEntry lesson, Moved moved, Week week)
    {
        if (lesson.StudentGroupId is not { } other || moved.GroupIds.Count == 0)
        {
            return true;
        }
        var pupils = week.Members.GetValueOrDefault(other) ?? [];
        return moved.GroupIds.Any(g => g == other || (week.Members.GetValueOrDefault(g) ?? []).Intersect(pupils).Any());
    }
}
