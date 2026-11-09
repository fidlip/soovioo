namespace Soovioo.Api.Common;

/// <summary>Czech working days: not a weekend, not a public holiday
/// (zákon č. 245/2000 Sb.).</summary>
public static class CzechWorkingDays
{
    private static readonly (int Month, int Day)[] FixedHolidays =
    [
        (1, 1), (5, 1), (5, 8), (7, 5), (7, 6), (9, 28), (10, 28), (11, 17), (12, 24), (12, 25), (12, 26),
    ];

    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    public static bool IsHoliday(DateOnly date)
    {
        if (FixedHolidays.Contains((date.Month, date.Day)))
        {
            return true;
        }
        var easter = EasterSunday(date.Year);
        return date == easter.AddDays(-2) || date == easter.AddDays(1);
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
