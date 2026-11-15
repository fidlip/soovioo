using Soovioo.Api.Common;

namespace Soovioo.Api.SchoolYears;

/// <summary>A day off as the calendar suggests it.</summary>
public record DayOffDraft(DateOnly From, DateOnly To, string Name, DayOffKind Kind);

/// <param name="FirstHalfIssuedOn">The last day of lessons of the first half,
/// when the výpis z vysvědčení is handed out.</param>
/// <param name="SecondHalfIssuedOn">The last day of lessons of the year.</param>
/// <param name="Official">Published by MŠMT; otherwise estimated by the
/// usual rule.</param>
public record SchoolYearDates(
    DateOnly TeachingStart, DateOnly FirstHalfIssuedOn, DateOnly SecondHalfIssuedOn, bool Official);

/// <summary>The dates of a school year as MŠMT publishes them in "Organizace
/// školního roku" (msmt.gov.cz), two years ahead; an estimate for the years
/// it hasn't published yet.</summary>
public static class MsmtCalendar
{
    private static readonly Dictionary<int, SchoolYearDates> Published = new()
    {
        [2025] = new(new(2025, 9, 1), new(2026, 1, 29), new(2026, 6, 30), true),
        [2026] = new(new(2026, 9, 1), new(2027, 1, 28), new(2027, 6, 30), true),
        [2027] = new(new(2027, 9, 1), new(2028, 1, 31), new(2028, 6, 30), true),
    };

    public static SchoolYearDates For(int startYear) =>
        Published.GetValueOrDefault(startYear) ?? Estimate(startYear);

    /// <summary>Lessons start on 1 Sep, after a weekend on Monday. The first
    /// half's last lesson is on 31 Jan; when that is a Friday or a weekend, on
    /// the Thursday before (the Friday is the pololetní prázdniny). The year's
    /// last lesson is on 30 Jun, or the Friday before a weekend.</summary>
    private static SchoolYearDates Estimate(int startYear)
    {
        var start = new DateOnly(startYear, 9, 1);
        start = start.DayOfWeek switch
        {
            DayOfWeek.Saturday => start.AddDays(2),
            DayOfWeek.Sunday => start.AddDays(1),
            _ => start,
        };
        var firstHalf = new DateOnly(startYear + 1, 1, 31);
        firstHalf = firstHalf.DayOfWeek switch
        {
            DayOfWeek.Friday => firstHalf.AddDays(-1),
            DayOfWeek.Saturday => firstHalf.AddDays(-2),
            DayOfWeek.Sunday => firstHalf.AddDays(-3),
            _ => firstHalf,
        };
        var end = new DateOnly(startYear + 1, 6, 30);
        end = end.DayOfWeek switch
        {
            DayOfWeek.Saturday => end.AddDays(-1),
            DayOfWeek.Sunday => end.AddDays(-2),
            _ => end,
        };
        return new SchoolYearDates(start, firstHalf, end, false);
    }

    /// <summary>The prázdniny of the year by § 4 of vyhláška 16/2005 Sb. —
    /// the jarní ones only with the <paramref name="district"/> — and the
    /// public holidays on the days of lessons (those in the vánoční
    /// prázdniny aside).</summary>
    public static List<DayOffDraft> DaysOff(int startYear, DateOnly teachingStart, DateOnly teachingEnd, string? district)
    {
        var next = startYear + 1;
        var days = new List<DayOffDraft>();
        var autumn = AutumnHolidays(startYear);
        if (autumn[1] == autumn[0].AddDays(1))
        {
            days.Add(new(autumn[0], autumn[1], "Podzimní prázdniny", DayOffKind.Holidays));
        }
        else
        {
            days.AddRange(autumn.Select(day => new DayOffDraft(day, day, "Podzimní prázdniny", DayOffKind.Holidays)));
        }

        var christmasFrom = new DateOnly(startYear, 12, 23);
        if (christmasFrom.DayOfWeek == DayOfWeek.Tuesday)
        {
            christmasFrom = christmasFrom.AddDays(-1);
        }
        var christmasTo = new DateOnly(next, 1, 2);
        if (christmasTo.AddDays(1).DayOfWeek == DayOfWeek.Friday)
        {
            christmasTo = christmasTo.AddDays(1);
        }
        days.Add(new(christmasFrom, christmasTo, "Vánoční prázdniny", DayOffKind.Holidays));

        var halfYear = new DateOnly(next, 1, 29);
        while (halfYear.DayOfWeek != DayOfWeek.Friday)
        {
            halfYear = halfYear.AddDays(1);
        }
        days.Add(new(halfYear, halfYear, "Pololetní prázdniny", DayOffKind.Holidays));

        if (district is not null && Districts.IsKnown(district))
        {
            var spring = Districts.SpringHolidays(startYear, district);
            days.Add(new(spring, spring.AddDays(4), "Jarní prázdniny", DayOffKind.Holidays));
        }

        var easterThursday = CzechWorkingDays.EasterSunday(next).AddDays(-3);
        days.Add(new(easterThursday, easterThursday, "Velikonoční prázdniny", DayOffKind.Holidays));

        days.AddRange(CzechWorkingDays.Holidays(teachingStart, teachingEnd)
            .Where(h => h.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                && (h.Date < christmasFrom || h.Date > christmasTo))
            .Select(h => new DayOffDraft(h.Date, h.Date, h.Name, DayOffKind.PublicHoliday)));
        return [.. days.Where(d => d.To >= teachingStart && d.From <= teachingEnd).OrderBy(d => d.From)];
    }

    /// <summary>Two days next to 28 Oct, as MŠMT has set them: around it
    /// when it is a Tuesday or a Thursday, the two days after it on a Monday,
    /// a Wednesday or a Sunday, the two days before it on a Friday or a
    /// Saturday.</summary>
    private static DateOnly[] AutumnHolidays(int startYear)
    {
        var holiday = new DateOnly(startYear, 10, 28);
        return holiday.DayOfWeek switch
        {
            DayOfWeek.Tuesday or DayOfWeek.Thursday => [holiday.AddDays(-1), holiday.AddDays(1)],
            DayOfWeek.Friday or DayOfWeek.Saturday => [holiday.AddDays(-2), holiday.AddDays(-1)],
            _ => [holiday.AddDays(1), holiday.AddDays(2)],
        };
    }
}
