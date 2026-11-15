using Soovioo.Api.Evidence;

namespace Soovioo.Api.SchoolYears;

/// <summary>What a grading period ends with.</summary>
public enum PeriodReport
{
    None,

    /// <summary>An assessment that isn't a report card (čtvrtletí).</summary>
    Interim,

    /// <summary>Vysvědčení, or its výpis when another follows in the year.</summary>
    ReportCard,
}

/// <summary>A part of a <see cref="SchoolYear"/> ("období": pololetí). Only
/// its end is kept: it starts the day after the previous one ends, the first
/// with the year; the year's last one runs to the year's end, so the summer
/// holidays belong to it (<see cref="GradingPeriods"/>). A grade, an absence
/// and a behaviour record belong to the period of their date.</summary>
public class GradingPeriod
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int SchoolYearId { get; set; }
    public SchoolYear? SchoolYear { get; set; }

    /// <summary>"1. pololetí".</summary>
    public required string Name { get; set; }

    public DateOnly To { get; set; }

    public PeriodReport Report { get; set; }

    /// <summary>The day the report card is handed out; families see it from
    /// then on.</summary>
    public DateOnly? IssuedOn { get; set; }
}
