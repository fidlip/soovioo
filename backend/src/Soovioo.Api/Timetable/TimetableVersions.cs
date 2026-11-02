using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable;

/// <summary>Which timetable applies on a day: the published version valid
/// then. Everything that asks "who teaches where" (access by
/// <see cref="Auth.PermissionScope.MyClasses"/>, directories, contacts,
/// Home) reads the lessons through <see cref="TimetableEntriesOn"/>, never
/// <see cref="AppDbContext.TimetableEntries"/> of every version.</summary>
public static class TimetableVersions
{
    public static IQueryable<TimetableEntry> TimetableEntriesOn(this AppDbContext db, DateOnly? day = null)
    {
        var on = day ?? db.Today;
        return db.TimetableEntries.Where(e =>
            e.TimetableVersion!.Status == TimetableVersionStatus.Published
            && e.TimetableVersion.ValidFrom <= on
            && (e.TimetableVersion.ValidTo == null || e.TimetableVersion.ValidTo >= on));
    }

    public static Task<TimetableVersion?> TimetableVersionOnAsync(this AppDbContext db, DateOnly day) =>
        db.TimetableVersions.FirstOrDefaultAsync(v =>
            v.Status == TimetableVersionStatus.Published
            && v.ValidFrom <= day
            && (v.ValidTo == null || v.ValidTo >= day));

    public static DateOnly SchoolYearStart(DateOnly day) => new(day.Month >= 9 ? day.Year : day.Year - 1, 9, 1);

    public static string SchoolYearName(DateOnly start) => $"Rozvrh {start.Year}/{(start.Year + 1) % 100:00}";

    /// <summary>The version valid on <paramref name="day"/>. A school that has
    /// no published version yet gets one from the start of the school year,
    /// so its first lessons have somewhere to go.</summary>
    public static async Task<TimetableVersion?> EnsureTimetableVersionOnAsync(
        this AppDbContext db, int schoolId, DateOnly day, DateTimeOffset now)
    {
        var version = await db.TimetableVersionOnAsync(day);
        if (version is not null
            || await db.TimetableVersions.AnyAsync(v => v.Status == TimetableVersionStatus.Published))
        {
            return version;
        }

        var start = SchoolYearStart(day);
        version = new TimetableVersion
        {
            SchoolId = schoolId,
            Name = SchoolYearName(start),
            ValidFrom = start,
            Status = TimetableVersionStatus.Published,
            CreatedAt = now,
            PublishedAt = now,
        };
        db.TimetableVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }

    /// <summary>Each published version ends the day before the next one
    /// starts; the last one, drafts and archived ones have no end.</summary>
    public static async Task RecomputeTimetableValidityAsync(this AppDbContext db)
    {
        var versions = await db.TimetableVersions.ToListAsync();
        var published = versions.Where(v => v.Status == TimetableVersionStatus.Published).OrderBy(v => v.ValidFrom).ToList();
        foreach (var v in versions)
        {
            v.ValidTo = null;
        }
        for (var i = 0; i < published.Count - 1; i++)
        {
            published[i].ValidTo = published[i + 1].ValidFrom.AddDays(-1);
        }
    }
}
