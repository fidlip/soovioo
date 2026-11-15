namespace Soovioo.Api.SchoolYears;

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
}
