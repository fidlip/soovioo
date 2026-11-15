using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Demo;

/// <summary>Czech school year for the demo data: which days are teaching
/// days — MŠMT's prázdniny for <see cref="District"/> and the public
/// holidays, the days off the demo's school year gets.</summary>
public class DemoSchoolCalendar
{
    private readonly HashSet<DateOnly> daysOff = [];

    public DemoSchoolCalendar(DateOnly today)
    {
        // The school year that "today" belongs to; in July/August the one
        // that just ended (the new one has no data yet).
        StartYear = today.Month >= 9 ? today.Year : today.Year - 1;
        Start = new DateOnly(StartYear, 9, 1);
        End = new DateOnly(StartYear + 1, 6, 30);
        Today = today;

        foreach (var dayOff in MsmtCalendar.DaysOff(StartYear, Start, End, District))
        {
            AddRange(dayOff.From, dayOff.To);
        }
    }

    /// <summary>The demo school's okres.</summary>
    public const string District = "Brno-město";

    public int StartYear { get; }
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public DateOnly Today { get; }

    /// <summary>The second half-year starts in February.</summary>
    public DateOnly SecondHalfStart => new(StartYear + 1, 2, 1);

    public bool IsSchoolDay(DateOnly day) =>
        day >= Start && day <= End
        && day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !daysOff.Contains(day);

    /// <summary>Teaching days from the start of the year up to and including
    /// today (or the year's end).</summary>
    public List<DateOnly> PastSchoolDays()
    {
        var last = Today < End ? Today : End;
        var days = new List<DateOnly>();
        for (var day = Start; day <= last; day = day.AddDays(1))
        {
            if (IsSchoolDay(day))
            {
                days.Add(day);
            }
        }
        return days;
    }

    /// <summary>The next <paramref name="count"/> teaching days after today.</summary>
    public List<DateOnly> UpcomingSchoolDays(int count)
    {
        var days = new List<DateOnly>();
        for (var day = Today.AddDays(1); days.Count < count && day <= End.AddYears(1); day = day.AddDays(1))
        {
            // Past the year's end (summer): the next one's first weekdays.
            if (IsSchoolDay(day) || (day > End && day.Month >= 9 && day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)))
            {
                days.Add(day);
            }
        }
        return days;
    }

    private void AddRange(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            daysOff.Add(day);
        }
    }
}
