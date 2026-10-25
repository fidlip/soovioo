using Soovioo.Api.Common;

namespace Soovioo.Api.Tests.Common;

public class SchoolFormatTests
{
    [Fact]
    public void DatesAsTheWebShowsThem()
    {
        Assert.Equal("1. 2. 2026", SchoolFormat.Date(new DateOnly(2026, 2, 1)));
        Assert.Equal("24. 10. 2026", SchoolFormat.Date(new DateOnly(2026, 10, 24)));
    }

    [Fact]
    public void MomentsInPragueTime()
    {
        // 22:30 UTC in summer = 0:30 the next day in Prague.
        var lateEvening = new DateTimeOffset(2026, 7, 1, 22, 30, 0, TimeSpan.Zero);
        Assert.Equal("2. 7. 2026", SchoolFormat.Date(lateEvening));
        Assert.Equal("0:30", SchoolFormat.Time(lateEvening));
        // Winter: UTC+1.
        Assert.Equal("24. 12. 2026 9:05", SchoolFormat.DateTime(new DateTimeOffset(2026, 12, 24, 8, 5, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void NumbersWithADecimalComma() =>
        Assert.Equal("1,5", SchoolFormat.Number(1.5, "0.0"));
}
