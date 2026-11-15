using Soovioo.Api.Evidence;

namespace Soovioo.Api.SchoolYears;

/// <summary>A school year of a school, as a rule 1 Sep – 31 Aug; the
/// school sets its days, the years must not overlap. Divided into
/// <see cref="GradingPeriod"/>s that follow one another, the last one
/// ending with the year.</summary>
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

    /// <summary>The last day of lessons.</summary>
    public DateOnly TeachingEnd { get; set; }

    public List<GradingPeriod> Periods { get; set; } = [];

    public List<SchoolDayOff> DaysOff { get; set; } = [];

    public static string NameOf(DateOnly from, DateOnly to) =>
        from.Year == to.Year ? $"{from.Year}" : $"{from.Year}/{to.Year % 100:00}";

    /// <summary>The year from 1 Sep of <paramref name="startYear"/> to 31 Aug
    /// with its two pololetí (§ 1 of vyhláška 16/2005 Sb.: to 31 Jan and the
    /// rest of the year), each ending with a report card.</summary>
    public static SchoolYear Create(int schoolId, int startYear, SchoolYearDates dates)
    {
        var from = new DateOnly(startYear, 9, 1);
        var to = new DateOnly(startYear + 1, 8, 31);
        return new()
        {
            SchoolId = schoolId,
            Name = NameOf(from, to),
            From = from,
            To = to,
            TeachingStart = dates.TeachingStart,
            TeachingEnd = dates.SecondHalfIssuedOn,
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
                    To = to,
                    Report = PeriodReport.ReportCard,
                    IssuedOn = dates.SecondHalfIssuedOn,
                },
            ],
        };
    }

    /// <summary>Adds MŠMT's prázdniny and the public holidays of its days of
    /// lessons (<see cref="MsmtCalendar.DaysOff"/>).</summary>
    public SchoolYear WithDaysOff(string? district)
    {
        DaysOff.AddRange(MsmtCalendar.DaysOff(From.Year, TeachingStart, TeachingEnd, district)
            .Select(d => new SchoolDayOff { SchoolId = SchoolId, From = d.From, To = d.To, Name = d.Name, Kind = d.Kind }));
        return this;
    }
}
