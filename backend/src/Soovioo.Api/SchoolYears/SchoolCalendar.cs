using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.SchoolYears;

/// <summary>Days without lessons: the school's <see cref="SchoolDayOff"/>s
/// and the days of a year outside its lessons (hlavní prázdniny). A day no
/// year covers counts as a day of lessons — the school didn't say otherwise.</summary>
public class SchoolCalendar
{
    public const string SummerHolidays = "Hlavní prázdniny";

    private readonly List<SchoolYear> years;
    private readonly List<SchoolDayOff> daysOff;

    private SchoolCalendar(List<SchoolYear> years, List<SchoolDayOff> daysOff)
    {
        this.years = years;
        this.daysOff = daysOff;
    }

    public static async Task<SchoolCalendar> LoadAsync(AppDbContext db, DateOnly from, DateOnly to) => new(
        await db.SchoolYears.AsNoTracking().Where(y => y.From <= to && y.To >= from).ToListAsync(),
        await db.SchoolDaysOff.AsNoTracking().Where(d => d.From <= to && d.To >= from).OrderBy(d => d.From).ToListAsync());

    /// <summary>A weekday that isn't off for the class; without one, for the
    /// whole school.</summary>
    public bool IsTeachingDay(DateOnly date, int? classId = null) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && Off(date, classId) is null;

    /// <summary>Whether the day is off for every one of the classes.</summary>
    public bool IsOffForAll(DateOnly date, IReadOnlyCollection<int> classIds) =>
        classIds.Count == 0 ? Off(date) is not null : classIds.All(c => Off(date, c) is not null);

    /// <summary>Why the day is off for the class (without one: for the
    /// whole school); null on a day of lessons. Weekends aside.</summary>
    public DayOffSpan? Off(DateOnly date, int? classId = null)
    {
        var dayOff = daysOff.FirstOrDefault(d => d.From <= date && date <= d.To
            && (d.ClassIds.Count == 0 || (classId is { } c && d.ClassIds.Contains(c))));
        if (dayOff is not null)
        {
            return Span(dayOff);
        }
        return Summer().FirstOrDefault(s => s.From <= date && date <= s.To);
    }

    private IEnumerable<DayOffSpan> Summer() => years.SelectMany(y => new[]
        {
            new DayOffSpan(y.From, y.TeachingStart.AddDays(-1), SummerHolidays, DayOffKind.Holidays, []),
            new DayOffSpan(y.TeachingEnd.AddDays(1), y.To, SummerHolidays, DayOffKind.Holidays, []),
        })
        .Where(s => s.From <= s.To);

    private static DayOffSpan Span(SchoolDayOff d) => new(d.From, d.To, d.Name, d.Kind, d.ClassIds);
}

/// <param name="ClassIds">Empty = the whole school.</param>
public record DayOffSpan(DateOnly From, DateOnly To, string Name, DayOffKind Kind, IReadOnlyList<int> ClassIds);
