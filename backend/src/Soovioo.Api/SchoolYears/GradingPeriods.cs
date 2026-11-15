using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.SchoolYears;

/// <summary>A period with the days it covers.</summary>
public record PeriodSpan(GradingPeriod Period, SchoolYear Year, DateOnly From, DateOnly To);

/// <summary>The report card a period ends with: everything since the
/// previous report card of the year. <paramref name="Number"/> counts the
/// year's report cards from 1 ("I. pololetí"); the last one is the
/// vysvědčení, the ones before it its výpis.</summary>
public record ReportPeriod(GradingPeriod Period, SchoolYear Year, DateOnly From, DateOnly To, int Number, bool Final)
{
    public int Id => Period.Id;

    public DateOnly IssuedOn => Period.IssuedOn ?? Period.To;
}

/// <summary>The school's years and their periods, as loaded for one request.</summary>
public sealed class GradingPeriods
{
    public const string NoYear = "Pro tento den není založený školní rok.";
    public const string UnknownPeriod = "Neznámé období.";

    private readonly List<PeriodSpan> spans = [];
    private readonly List<ReportPeriod> reports = [];

    private GradingPeriods(List<SchoolYear> years)
    {
        Years = years;
        foreach (var year in years)
        {
            var periods = year.Periods.OrderBy(p => p.To).ToList();
            var from = year.From;
            var reportFrom = year.From;
            var number = 0;
            var lastReport = periods.LastOrDefault(p => p.Report == PeriodReport.ReportCard);
            foreach (var period in periods)
            {
                var to = period == periods[^1] ? year.To : period.To;
                spans.Add(new PeriodSpan(period, year, from, to));
                if (period.Report == PeriodReport.ReportCard)
                {
                    reports.Add(new ReportPeriod(period, year, reportFrom, to, ++number, period == lastReport));
                    reportFrom = to.AddDays(1);
                }
                from = to.AddDays(1);
            }
        }
    }

    /// <summary>Oldest first, each with its periods.</summary>
    public IReadOnlyList<SchoolYear> Years { get; }

    public IReadOnlyList<PeriodSpan> Spans => spans;

    public IReadOnlyList<ReportPeriod> Reports => reports;

    public static async Task<GradingPeriods> LoadAsync(AppDbContext db) =>
        new(await db.SchoolYears.Include(y => y.Periods).OrderBy(y => y.From).ToListAsync());

    public SchoolYear? YearOn(DateOnly day) => Years.FirstOrDefault(y => y.From <= day && day <= y.To);

    public PeriodSpan? On(DateOnly day) => spans.FirstOrDefault(s => s.From <= day && day <= s.To);

    /// <summary>The end of lessons (of the last period) of the school year
    /// running on the day; after it, the next year's.</summary>
    public DateOnly? TeachingEnd(DateOnly day) =>
        Years.Where(y => y.Periods.Count > 0)
            .Select(y => (DateOnly?)y.Periods.Max(p => p.To))
            .FirstOrDefault(to => to >= day);

    public PeriodSpan? Span(int periodId) => spans.FirstOrDefault(s => s.Period.Id == periodId);

    public ReportPeriod? Report(int periodId) => reports.FirstOrDefault(r => r.Period.Id == periodId);
}

public static class GradingPeriodQueries
{
    public static Task<GradingPeriods> GradingPeriodsAsync(this AppDbContext db) => GradingPeriods.LoadAsync(db);

    /// <summary>Nothing is written for a day outside the school's years.</summary>
    public static async Task<bool> HasSchoolYearOnAsync(this AppDbContext db, DateOnly day) =>
        await db.SchoolYears.AnyAsync(y => y.From <= day && day <= y.To);
}
