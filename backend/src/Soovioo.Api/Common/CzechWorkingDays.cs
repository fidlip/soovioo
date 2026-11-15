namespace Soovioo.Api.Common;

/// <summary>Czech working days: not a weekend, not a public holiday
/// (zákon č. 245/2000 Sb.).</summary>
public static class CzechWorkingDays
{
    private static readonly (int Month, int Day, string Name)[] FixedHolidays =
    [
        (1, 1, "Den obnovy samostatného českého státu"),
        (5, 1, "Svátek práce"),
        (5, 8, "Den vítězství"),
        (7, 5, "Den slovanských věrozvěstů Cyrila a Metoděje"),
        (7, 6, "Den upálení mistra Jana Husa"),
        (9, 28, "Den české státnosti"),
        (10, 28, "Den vzniku samostatného československého státu"),
        (11, 17, "Den boje za svobodu a demokracii"),
        (12, 24, "Štědrý den"),
        (12, 25, "1. svátek vánoční"),
        (12, 26, "2. svátek vánoční"),
    ];

    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    public static bool IsHoliday(DateOnly date) => HolidayName(date) is not null;

    public static string? HolidayName(DateOnly date)
    {
        if (FixedHolidays.FirstOrDefault(h => h.Month == date.Month && h.Day == date.Day) is { Name: { } name })
        {
            return name;
        }
        var easter = EasterSunday(date.Year);
        return date == easter.AddDays(-2) ? "Velký pátek"
            : date == easter.AddDays(1) ? "Velikonoční pondělí"
            : null;
    }

    /// <summary>The public holidays from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static IEnumerable<(DateOnly Date, string Name)> Holidays(DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (HolidayName(date) is { } name)
            {
                yield return (date, name);
            }
        }
    }

    /// <summary>The <paramref name="days"/>-th working day after <paramref name="from"/>.</summary>
    public static DateOnly Add(DateOnly from, int days)
    {
        var date = from;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date))
            {
                days--;
            }
        }
        return date;
    }

    /// <summary>Gregorian Easter Sunday (anonymous algorithm).</summary>
    public static DateOnly EasterSunday(int year)
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
