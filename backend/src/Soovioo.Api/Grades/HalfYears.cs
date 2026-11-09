using Soovioo.Api.Behavior;

namespace Soovioo.Api.Grades;

/// <summary>Fixed half-year dates until the school has its own school year
/// settings (#253): 1. pololetí 1 Sep – 31 Jan, 2. pololetí 1 Feb – 31 Aug;
/// the report card is issued on 31 Jan and 30 Jun.</summary>
public static class HalfYears
{
    public static (DateOnly From, DateOnly To) Range(int schoolYearStartYear, SchoolHalfYear half) =>
        half == SchoolHalfYear.First
            ? (new DateOnly(schoolYearStartYear, 9, 1), new DateOnly(schoolYearStartYear + 1, 1, 31))
            : (new DateOnly(schoolYearStartYear + 1, 2, 1), new DateOnly(schoolYearStartYear + 1, 8, 31));

    public static DateOnly IssuedOn(int schoolYearStartYear, SchoolHalfYear half) =>
        half == SchoolHalfYear.First
            ? new DateOnly(schoolYearStartYear + 1, 1, 31)
            : new DateOnly(schoolYearStartYear + 1, 6, 30);

    public static (int SchoolYearStartYear, SchoolHalfYear Half) Of(DateOnly day) =>
        day.Month >= 9 ? (day.Year, SchoolHalfYear.First)
        : day.Month == 1 ? (day.Year - 1, SchoolHalfYear.First)
        : (day.Year - 1, SchoolHalfYear.Second);
}
