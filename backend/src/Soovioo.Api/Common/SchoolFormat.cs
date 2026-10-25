using System.Globalization;

namespace Soovioo.Api.Common;

/// <summary>Dates, times and numbers in what the server writes for people —
/// documents, e-mails, notifications: the schools' locale (cs-CZ, like the
/// web's APP_LOCALE), moments in the schools' time zone
/// (<see cref="SchoolClock"/>). Not for exports with a format of their own
/// (Matrika, CSV).</summary>
public static class SchoolFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>"24. 10. 2026" — as the web shows dates.</summary>
    public static string Date(DateOnly date) => date.ToString("d. M. yyyy", Culture);

    public static string Date(DateTimeOffset at) => Date(DateOnly.FromDateTime(SchoolClock.Local(at)));

    /// <summary>"9:05".</summary>
    public static string Time(DateTimeOffset at) => SchoolClock.Local(at).ToString("H:mm", Culture);

    /// <summary>"24. 10. 2026 9:05".</summary>
    public static string DateTime(DateTimeOffset at) => $"{Date(at)} {Time(at)}";

    /// <summary>"1,5" — a decimal comma.</summary>
    public static string Number(double value, string format) => value.ToString(format, Culture);
}
