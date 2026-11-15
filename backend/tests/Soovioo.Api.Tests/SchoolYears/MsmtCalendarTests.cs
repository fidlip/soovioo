using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Tests.SchoolYears;

public class MsmtCalendarTests
{
    /// <summary>"Organizace školního roku" of MŠMT for 2025/26–2027/28.</summary>
    [Theory]
    [InlineData(2025, "2025-09-01", "2026-01-29", "2026-06-30")]
    [InlineData(2026, "2026-09-01", "2027-01-28", "2027-06-30")]
    [InlineData(2027, "2027-09-01", "2028-01-31", "2028-06-30")]
    public void ThePublishedYearsHaveMsmtsDates(int startYear, string start, string firstHalf, string end)
    {
        var dates = MsmtCalendar.For(startYear);

        Assert.True(dates.Official);
        Assert.Equal(
            (DateOnly.Parse(start), DateOnly.Parse(firstHalf), DateOnly.Parse(end)),
            (dates.TeachingStart, dates.FirstHalfIssuedOn, dates.SecondHalfIssuedOn));
    }

    /// <summary>1 Sep 2029 is a Saturday, 1 Sep 2024 a Sunday; 31 Jan is a
    /// Friday in 2025, a Sunday in 2021 and a Saturday in 2026; 30 Jun is a
    /// Saturday in 2029 and a Sunday in 2024.</summary>
    [Theory]
    [InlineData(2028, "2028-09-01", "2029-01-31", "2029-06-29")]
    [InlineData(2029, "2029-09-03", "2030-01-31", "2030-06-28")]
    [InlineData(2024, "2024-09-02", "2025-01-30", "2025-06-30")]
    [InlineData(2020, "2020-09-01", "2021-01-28", "2021-06-30")]
    [InlineData(2023, "2023-09-01", "2024-01-31", "2024-06-28")]
    public void OtherYearsAreEstimatedByTheUsualRule(int startYear, string start, string firstHalf, string end)
    {
        var dates = MsmtCalendar.For(startYear);

        Assert.False(dates.Official);
        Assert.Equal(
            (DateOnly.Parse(start), DateOnly.Parse(firstHalf), DateOnly.Parse(end)),
            (dates.TeachingStart, dates.FirstHalfIssuedOn, dates.SecondHalfIssuedOn));
    }

    /// <summary>§ 1 of vyhláška 16/2005 Sb.: the first pololetí ends on
    /// 31 Jan, whenever the výpis goes out; the second runs to the year's
    /// end, lessons end with the vysvědčení.</summary>
    [Fact]
    public void AYearsPololetiEndByTheLaw()
    {
        foreach (var startYear in new[] { 2025, 2026, 2027 })
        {
            var published = MsmtCalendar.For(startYear);
            var year = SchoolYear.Create(1, startYear, published);
            Assert.Equal(published.FirstHalfIssuedOn, year.Periods[0].IssuedOn);
            Assert.Equal(new DateOnly(startYear + 1, 1, 31), year.Periods[0].To);
            Assert.Equal(new DateOnly(startYear + 1, 8, 31), year.Periods[1].To);
            Assert.Equal(published.SecondHalfIssuedOn, year.TeachingEnd);
        }
    }

    private static List<(DateOnly From, DateOnly To, string Name)> DaysOff(int startYear, string? district)
    {
        var dates = MsmtCalendar.For(startYear);
        return [.. MsmtCalendar.DaysOff(startYear, dates.TeachingStart, dates.SecondHalfIssuedOn, district).Select(d => (d.From, d.To, d.Name))];
    }

    private static (DateOnly, DateOnly, string) Day(string date, string name) => (DateOnly.Parse(date), DateOnly.Parse(date), name);

    /// <summary>"Organizace školního roku 2026/2027" of MŠMT; 1 and 8 May
    /// 2027 are Saturdays.</summary>
    [Fact]
    public void TheYear2026HasMsmtsPrazdninyAndThePublicHolidays()
    {
        Assert.Equal(
            [
                Day("2026-09-28", "Den české státnosti"),
                Day("2026-10-28", "Den vzniku samostatného československého státu"),
                (new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 30), "Podzimní prázdniny"),
                Day("2026-11-17", "Den boje za svobodu a demokracii"),
                (new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 2), "Vánoční prázdniny"),
                Day("2027-01-29", "Pololetní prázdniny"),
                (new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 5), "Jarní prázdniny"),
                Day("2027-03-25", "Velikonoční prázdniny"),
                Day("2027-03-26", "Velký pátek"),
                Day("2027-03-29", "Velikonoční pondělí"),
            ],
            DaysOff(2026, "Liberec"));
    }

    /// <summary>MŠMT's "Organizace školního roku" 2025/26 and 2027/28: the
    /// podzimní prázdniny around a Tuesday's and a Thursday's 28 Oct, the
    /// vánoční from Monday when 23 Dec is a Tuesday, the pololetní on the
    /// Friday from 29 Jan to 4 Feb.</summary>
    [Fact]
    public void ThePrazdninyMoveWithTheWeekdays()
    {
        var y2025 = DaysOff(2025, null);
        Assert.Contains(Day("2025-10-27", "Podzimní prázdniny"), y2025);
        Assert.Contains(Day("2025-10-29", "Podzimní prázdniny"), y2025);
        Assert.Contains((new DateOnly(2025, 12, 22), new DateOnly(2026, 1, 2), "Vánoční prázdniny"), y2025);
        Assert.Contains(Day("2026-01-30", "Pololetní prázdniny"), y2025);
        Assert.Contains(Day("2026-04-02", "Velikonoční prázdniny"), y2025);
        Assert.DoesNotContain(y2025, d => d.Name == "Jarní prázdniny");

        var y2027 = DaysOff(2027, null);
        Assert.Contains(Day("2027-10-27", "Podzimní prázdniny"), y2027);
        Assert.Contains(Day("2027-10-29", "Podzimní prázdniny"), y2027);
        Assert.Contains(Day("2028-02-04", "Pololetní prázdniny"), y2027);
        Assert.Contains(Day("2028-04-13", "Velikonoční prázdniny"), y2027);

        // 3 Jan 2025 is a Friday.
        Assert.Contains((new DateOnly(2024, 12, 23), new DateOnly(2025, 1, 3), "Vánoční prázdniny"), DaysOff(2024, null));
    }

    /// <summary>The příloha of vyhláška 16/2005 Sb.</summary>
    [Theory]
    [InlineData(2022, "Praha 6–10", "2023-02-06")]
    [InlineData(2023, "Praha 1–5", "2024-02-05")]
    [InlineData(2024, "Hodonín", "2025-02-10")]
    [InlineData(2025, "Chomutov", "2026-02-02")]
    [InlineData(2025, "Praha 1–5", "2026-02-16")]
    [InlineData(2026, "Praha 6–10", "2027-03-01")]
    [InlineData(2026, "Plzeň-město", "2027-03-08")]
    [InlineData(2027, "Zlín", "2028-02-07")]
    [InlineData(2027, "Chomutov", "2028-02-21")]
    [InlineData(2027, "Praha 6–10", "2028-03-13")]
    public void TheJarniPrazdninyGoByTheDistrict(int startYear, string district, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), Districts.SpringHolidays(startYear, district));

    [Fact]
    public void EveryOkresHasItsWeek()
    {
        Assert.Equal(78, Districts.All.Count);
        Assert.Equal("Benešov", Districts.All[0]);
        Assert.Contains("Ústí nad Labem", Districts.All);
    }
}
