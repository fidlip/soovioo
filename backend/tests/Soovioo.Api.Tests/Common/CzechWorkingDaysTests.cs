using Soovioo.Api.Common;

namespace Soovioo.Api.Tests.Common;

public class CzechWorkingDaysTests
{
    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2025, 4, 20)]
    public void EasterSundayIsRight(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), CzechWorkingDays.EasterSunday(year));

    [Fact]
    public void WeekendsAndHolidaysAreSkipped()
    {
        // Thu 2026-12-23 + 2: Christmas Eve to St Stephen's and the weekend are off.
        Assert.Equal(new DateOnly(2026, 12, 29), CzechWorkingDays.Add(new DateOnly(2026, 12, 23), 2));
        // Thu before Easter + 1: Good Friday and Easter Monday are off.
        Assert.Equal(new DateOnly(2026, 4, 7), CzechWorkingDays.Add(new DateOnly(2026, 4, 2), 1));
        Assert.Equal(new DateOnly(2026, 11, 9), CzechWorkingDays.Add(new DateOnly(2026, 11, 9), 0));
        Assert.False(CzechWorkingDays.IsWorkingDay(new DateOnly(2026, 10, 28)));
        Assert.True(CzechWorkingDays.IsWorkingDay(new DateOnly(2026, 10, 29)));
    }
}
