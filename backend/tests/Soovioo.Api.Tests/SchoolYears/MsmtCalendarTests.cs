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
}
