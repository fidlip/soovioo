using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Tests;

internal static class TestSchoolYears
{
    /// <summary>The school year from 1 Sep of <paramref name="startYear"/>,
    /// created on first use: 1. pololetí to 31 Jan (issued that day),
    /// 2. pololetí to 30 Jun (issued that day).</summary>
    public static SchoolYear SchoolYear(this AppDbContext db, int startYear, int schoolId = 1)
    {
        var from = new DateOnly(startYear, 9, 1);
        var existing = db.SchoolYears.Local.FirstOrDefault(y => y.SchoolId == schoolId && y.From == from)
            ?? db.SchoolYears.IgnoreQueryFilters().Include(y => y.Periods)
                .FirstOrDefault(y => y.SchoolId == schoolId && y.From == from);
        if (existing is not null)
        {
            return existing;
        }
        var year = global::Soovioo.Api.SchoolYears.SchoolYear.Create(
            schoolId, startYear, new SchoolYearDates(from, new DateOnly(startYear + 1, 1, 31), new DateOnly(startYear + 1, 6, 30), false));
        db.SchoolYears.Add(year);
        db.SaveChanges();
        return year;
    }

    /// <summary>The years 2020/21–2029/30, so whatever day a test writes on
    /// has one.</summary>
    public static AppDbContext WithSchoolYears(this AppDbContext db, int schoolId = 1)
    {
        for (var startYear = 2020; startYear < 2030; startYear++)
        {
            db.SchoolYear(startYear, schoolId);
        }
        return db;
    }

    public static GradingPeriod FirstHalf(this AppDbContext db, int startYear, int schoolId = 1) =>
        db.SchoolYear(startYear, schoolId).Periods.OrderBy(p => p.To).First();

    public static GradingPeriod SecondHalf(this AppDbContext db, int startYear, int schoolId = 1) =>
        db.SchoolYear(startYear, schoolId).Periods.OrderBy(p => p.To).Last();
}
