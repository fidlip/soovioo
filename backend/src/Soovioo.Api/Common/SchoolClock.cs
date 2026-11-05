namespace Soovioo.Api.Common;

/// <summary>The schools' calendar day — every school is in Czechia, so
/// "today" is Prague's, not the server's (UTC in production).</summary>
public static class SchoolClock
{
    public static readonly TimeZoneInfo TimeZone =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var prague) ? prague : TimeZoneInfo.Utc;

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZone).DateTime);

    public static DateTime Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TimeZone).DateTime;

    /// <summary>The moment <paramref name="time"/> on <paramref name="date"/> in Prague.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}
