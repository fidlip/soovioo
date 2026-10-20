namespace Soovioo.Api.Demo;

/// <summary>Czech school year for the demo data: which days are teaching
/// days. Public holidays are exact; the school holidays (prázdniny) follow
/// the usual pattern, not any particular region's schedule — good enough
/// for data that should merely look plausible.</summary>
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

        var y = StartYear;
        var next = StartYear + 1;
        AddDays(new DateOnly(y, 9, 28));
        AddDays(new DateOnly(y, 10, 28));
        AddRange(new DateOnly(y, 10, 29), new DateOnly(y, 10, 30)); // podzimní prázdniny
        AddDays(new DateOnly(y, 11, 17));
        AddRange(new DateOnly(y, 12, 23), new DateOnly(next, 1, 2)); // vánoční prázdniny
        AddDays(FirstWeekday(new DateOnly(next, 1, 30), DayOfWeek.Friday)); // pololetní prázdniny
        var spring = FirstWeekday(new DateOnly(next, 2, 16), DayOfWeek.Monday); // jarní prázdniny
        AddRange(spring, spring.AddDays(4));
        var easter = EasterSunday(next);
        AddDays(easter.AddDays(-3), easter.AddDays(-2), easter.AddDays(1)); // Zelený čtvrtek, Velký pátek, Velikonoční pondělí
        AddDays(new DateOnly(next, 5, 1), new DateOnly(next, 5, 8));
    }

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

    private void AddDays(params DateOnly[] days) => daysOff.UnionWith(days);

    private void AddRange(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            daysOff.Add(day);
        }
    }

    private static DateOnly FirstWeekday(DateOnly from, DayOfWeek dayOfWeek)
    {
        while (from.DayOfWeek != dayOfWeek)
        {
            from = from.AddDays(1);
        }
        return from;
    }

    /// <summary>Gregorian Easter Sunday (anonymous algorithm).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
