using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests;

internal static class TestTimetable
{
    /// <summary>A published version valid since long ago, created on first use
    /// — lessons outside one don't apply on any day.</summary>
    public static TimetableVersion CurrentTimetableVersion(this AppDbContext db, int schoolId = 1)
    {
        var existing = db.TimetableVersions.Local.FirstOrDefault(v => v.SchoolId == schoolId && v.Status == TimetableVersionStatus.Published)
            ?? db.TimetableVersions.IgnoreQueryFilters()
                .FirstOrDefault(v => v.SchoolId == schoolId && v.Status == TimetableVersionStatus.Published);
        if (existing is not null)
        {
            return existing;
        }
        var version = new TimetableVersion
        {
            SchoolId = schoolId,
            Name = "Rozvrh",
            ValidFrom = new DateOnly(2000, 9, 1),
            Status = TimetableVersionStatus.Published,
        };
        db.TimetableVersions.Add(version);
        db.SaveChanges();
        return version;
    }
}
