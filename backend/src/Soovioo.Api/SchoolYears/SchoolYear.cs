using Soovioo.Api.Evidence;

namespace Soovioo.Api.SchoolYears;

/// <summary>A school year of a school, 1 Sep – 31 Aug; the school's years
/// follow one another without a gap. Divided into
/// <see cref="GradingPeriod"/>s.</summary>
public class SchoolYear
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>"2026/27".</summary>
    public required string Name { get; set; }

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>The first day of lessons.</summary>
    public DateOnly TeachingStart { get; set; }

    public List<GradingPeriod> Periods { get; set; } = [];

    public static string NameOf(int startYear) => $"{startYear}/{(startYear + 1) % 100:00}";

    /// <summary>The year from 1 Sep of <paramref name="startYear"/> with its
    /// two pololetí (§ 1 of vyhláška 16/2005 Sb.: to 31 Jan and to 30 Jun),
    /// each ending with a report card.</summary>
    public static SchoolYear Create(int schoolId, int startYear, SchoolYearDates dates) => new()
    {
        SchoolId = schoolId,
        Name = NameOf(startYear),
        From = new DateOnly(startYear, 9, 1),
        To = new DateOnly(startYear + 1, 8, 31),
        TeachingStart = dates.TeachingStart,
        Periods =
        [
            new GradingPeriod
            {
                SchoolId = schoolId,
                Name = "1. pololetí",
                To = new DateOnly(startYear + 1, 1, 31),
                Report = PeriodReport.ReportCard,
                IssuedOn = dates.FirstHalfIssuedOn,
            },
            new GradingPeriod
            {
                SchoolId = schoolId,
                Name = "2. pololetí",
                To = new DateOnly(startYear + 1, 6, 30),
                Report = PeriodReport.ReportCard,
                IssuedOn = dates.SecondHalfIssuedOn,
            },
        ],
    };
}
