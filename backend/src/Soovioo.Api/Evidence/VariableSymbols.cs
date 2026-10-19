using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

/// <summary>Default variable symbol for a student's payments, format
/// <c>RRNNNN</c>: RR = last two digits of the year the student was added,
/// NNNN = order within the school and that year (e.g. 260001). Admin may
/// overwrite it; uniqueness within the school is enforced on save.</summary>
public static partial class VariableSymbols
{
    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex SixDigits();

    /// <summary>Next free symbol for the year, after the highest existing
    /// one of the same year in <paramref name="existing"/>.</summary>
    public static string Next(IEnumerable<string?> existing, int year)
    {
        var prefix = (year % 100).ToString("00");
        var max = existing
            .Where(vs => vs is not null && SixDigits().IsMatch(vs) && vs.StartsWith(prefix))
            .Select(vs => int.Parse(vs![2..]))
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{max + 1:0000}";
    }

    public static async Task<string> NextAsync(AppDbContext db, int schoolId, int year)
    {
        var existing = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && s.DefaultVariableSymbol != null)
            .Select(s => s.DefaultVariableSymbol)
            .ToListAsync();
        return Next(existing, year);
    }

    /// <summary>Startup backfill: every student without a symbol gets one
    /// (year = enrollment year, else the current year), in id order.</summary>
    public static void Backfill(AppDbContext db)
    {
        var missing = db.Students.IgnoreQueryFilters()
            .Where(s => s.DefaultVariableSymbol == null || s.DefaultVariableSymbol == "")
            .OrderBy(s => s.Id)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var group in missing.GroupBy(s => s.SchoolId))
        {
            var existing = db.Students.IgnoreQueryFilters()
                .Where(s => s.SchoolId == group.Key && s.DefaultVariableSymbol != null && s.DefaultVariableSymbol != "")
                .Select(s => s.DefaultVariableSymbol)
                .ToList();
            foreach (var student in group)
            {
                var year = student.EnrollmentDate?.Year ?? DateTime.Today.Year;
                student.DefaultVariableSymbol = Next(existing, year);
                existing.Add(student.DefaultVariableSymbol);
            }
        }
        db.SaveChanges();
    }
}
