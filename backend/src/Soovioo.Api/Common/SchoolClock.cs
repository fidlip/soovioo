namespace Soovioo.Api.Common;

/// <summary>The schools' calendar day — every school is in Czechia, so
/// "today" is Prague's, not the server's (UTC in production).</summary>
public static class SchoolClock
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var prague) ? prague : TimeZoneInfo.Utc;

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);
}
