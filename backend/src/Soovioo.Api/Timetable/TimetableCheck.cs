using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>What's wrong with a version's timetable — shown before it's
/// published: clashes, hours against the učební plán, lessons without a
/// teacher or a classroom, teachers on their days off.</summary>
public static class TimetableCheck
{
    /// <summary>Of each kind, the rest is summed up after this many.</summary>
    private const int PerKind = 5;

    public static async Task<List<string>> ProblemsAsync(AppDbContext db, TimetableVersion version)
    {
        var entries = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
            .Select(e => new Lesson(
                e.SchoolClassId, e.TeacherId, e.ClassroomId, e.DayOfWeek, e.PeriodNumber, e.WeekParity,
                e.SchoolClass!.Name, e.Subject!.Abbreviation ?? e.Subject.Name, e.Subject.Classrooms.Any() || e.SchoolClass.HomeroomId != null,
                e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name))
            .ToListAsync();
        if (entries.Count == 0)
        {
            return ["Verze nemá žádné hodiny."];
        }

        var problems = new List<string>();
        void Add(IEnumerable<string> found)
        {
            var list = found.ToList();
            problems.AddRange(list.Take(PerKind));
            if (list.Count > PerKind)
            {
                problems.Add($"… a dalších {list.Count - PerKind} podobných.");
            }
        }
        static string Slot(DayOfWeek day, int period) => $"{Day(day)} {period}. hod.";

        // Twice at a time (week parities that meet).
        IEnumerable<string> Clashes(Func<Lesson, int?> key, Func<Lesson, string> what) =>
            entries.Where(e => key(e) is not null)
                .GroupBy(e => (Key: key(e)!.Value, e.Day, e.Period))
                .Where(g => g.Any(a => g.Any(b => !ReferenceEquals(a, b)
                    && (a.Parity == WeekParity.Every || b.Parity == WeekParity.Every || a.Parity == b.Parity))))
                .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Period)
                .Select(g => $"{what(g.First())} {Slot(g.Key.Day, g.Key.Period)}: {string.Join(", ", g.Select(e => $"{e.ClassName} {e.Subject}"))}.");
        Add(Clashes(e => e.ClassId, e => $"Třída {e.ClassName} má dvě hodiny najednou,"));
        Add(Clashes(e => e.TeacherId, e => $"{e.TeacherName} učí dvakrát najednou,"));
        Add(Clashes(e => e.ClassroomId, e => $"Učebna {e.RoomName} je obsazená dvakrát najednou,"));

        // Against the plan and přidělení.
        var plan = await TeachingPlan.LoadAsync(db, version);
        Add(plan.Classes.SelectMany(c => c.Rows.Where(r => r.TimetableHours > 0 || r.PlanHours > 0)
            .SelectMany(r => r.Problems.Select(p => $"{c.Name} {r.SubjectAbbreviation ?? r.SubjectName}: {p}"))));
        Add(plan.Teachers.SelectMany(t => TeachingPlanner.Unavailable(
                t.Availability, entries.Where(e => e.TeacherId == t.Id).Select(e => (e.Day, e.Period)))
            .Select(when => $"{t.Name} učí {when}, kdy učit nemůže.")));

        Add(entries.Where(e => e.NeedsRoom && e.ClassroomId is null)
            .GroupBy(e => (e.ClassName, e.Subject))
            .OrderBy(g => g.Key.ClassName)
            .Select(g => $"{g.Key.ClassName} {g.Key.Subject}: {g.Count()} h bez učebny."));
        return problems;
    }

    private record Lesson(
        int ClassId, int? TeacherId, int? ClassroomId, DayOfWeek Day, int Period, WeekParity Parity,
        string ClassName, string Subject, bool NeedsRoom, string? TeacherName, string? RoomName);

    private static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "po",
        DayOfWeek.Tuesday => "út",
        DayOfWeek.Wednesday => "st",
        DayOfWeek.Thursday => "čt",
        DayOfWeek.Friday => "pá",
        DayOfWeek.Saturday => "so",
        _ => "ne",
    };
}
