using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;
using Soovioo.Api.Timetable.Rvp;

namespace Soovioo.Api.Timetable;

/// <summary>What's wrong with a version's timetable — shown before it's
/// published: clashes, hours against the učební plán, lessons without a
/// teacher or a classroom, teachers on their days off, groups that don't fit
/// their classroom, pupils in none of a division's groups.</summary>
public static class TimetableCheck
{
    /// <summary>Of each kind, the rest is summed up after this many.</summary>
    private const int PerKind = 5;

    public static async Task<List<string>> ProblemsAsync(AppDbContext db, TimetableVersion version)
    {
        var entries = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id)
            .Select(e => new Lesson(
                e.SchoolClassId, e.TeacherId, e.ClassroomId, e.DayOfWeek, e.PeriodNumber, e.WeekParity,
                e.SchoolClass!.Name, e.Subject!.Abbreviation ?? e.Subject.Name,
                e.Subject.Classrooms.Any() || e.SchoolClass.HomeroomId != null || (e.StudentGroup != null && e.StudentGroup.ClassroomId != null),
                e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name,
                e.StudentGroupId, e.StudentGroup == null ? null : e.StudentGroup.Name,
                e.Classroom == null ? null : e.Classroom.Capacity))
            .ToListAsync();
        var groups = await db.StudentGroups.Where(g => g.TimetableVersionId == version.Id)
            .Select(g => new
            {
                g.Id, g.Name, g.GroupDivisionId, Division = g.GroupDivision == null ? null : g.GroupDivision.Name,
                Classes = g.Classes.Select(c => c.SchoolClassId).ToList(),
                Members = g.Members.Select(m => m.StudentId).ToList(),
            })
            .ToDictionaryAsync(g => g.Id);
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

        // Twice at a time (week parities that meet); a group's lesson is one
        // per class, the same lesson. Lessons of a class clash unless both
        // are of groups without a pupil in common.
        bool SameLesson(Lesson a, Lesson b) => a.GroupId is not null && a.GroupId == b.GroupId;
        bool PupilsMeet(Lesson a, Lesson b) =>
            a.GroupId is not { } ga || b.GroupId is not { } gb || !groups.ContainsKey(ga) || !groups.ContainsKey(gb)
            || groups[ga].Members.Intersect(groups[gb].Members).Any();
        IEnumerable<string> Clashes(Func<Lesson, int?> key, Func<Lesson, string> what, Func<Lesson, Lesson, bool> clash) =>
            entries.Where(e => key(e) is not null)
                .GroupBy(e => (Key: key(e)!.Value, e.Day, e.Period))
                .Where(g => g.Any(a => g.Any(b => !ReferenceEquals(a, b) && !SameLesson(a, b) && clash(a, b)
                    && (a.Parity == WeekParity.Every || b.Parity == WeekParity.Every || a.Parity == b.Parity))))
                .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Period)
                .Select(g => $"{what(g.First())} {Slot(g.Key.Day, g.Key.Period)}: {string.Join(", ", g.Where(e => e.GroupId is null).Concat(g.Where(e => e.GroupId is not null).DistinctBy(e => e.GroupId)).Select(e => $"{e.Who} {e.Subject}"))}.");
        Add(Clashes(e => e.ClassId, e => $"Třída {e.ClassName} má dvě hodiny najednou,", PupilsMeet));
        Add(Clashes(e => e.TeacherId, e => $"{e.TeacherName} učí dvakrát najednou,", (_, _) => true));
        Add(Clashes(e => e.ClassroomId, e => $"Učebna {e.RoomName} je obsazená dvakrát najednou,", (_, _) => true));

        // Against the plan and přidělení.
        var plan = await TeachingPlan.LoadAsync(db, version);
        Add(plan.Classes.SelectMany(c => c.Rows.Where(r => r.TimetableHours > 0 || r.PlanHours > 0)
            .SelectMany(r => r.Problems.Select(p => $"{c.Name} {r.SubjectAbbreviation ?? r.SubjectName}: {p}"))));
        Add(plan.Teachers.SelectMany(t => TeachingPlanner.Unavailable(
                t.Availability, entries.Where(e => e.TeacherId == t.Id).Select(e => (e.Day, e.Period)))
            .Select(when => $"{t.Name} učí {when}, kdy učit nemůže.")));

        Add(plan.Groups.Where(g => g.TimetableHours > 0 || g.PlanHours > 0)
            .SelectMany(g => g.Problems.Select(p => $"{g.Name} {g.SubjectAbbreviation ?? g.SubjectName}: {p}")));

        Add((await RvpCheck.LoadAsync(db, plan)).AllProblems);

        Add(entries.Where(e => e.NeedsRoom && e.ClassroomId is null)
            .DistinctBy(e => e.GroupId is { } g ? (g, e.Day, e.Period, e.Parity, 0) : (0, e.Day, e.Period, e.Parity, e.ClassId))
            .GroupBy(e => (e.Who, e.Subject))
            .OrderBy(g => g.Key.Who)
            .Select(g => $"{g.Key.Who} {g.Key.Subject}: {g.Count()} h bez učebny."));

        // Groups bigger than their classroom.
        Add(entries.Where(e => e.GroupId is { } g && groups.ContainsKey(g) && e.Capacity < groups[g].Members.Count)
            .DistinctBy(e => (e.GroupId, e.Day, e.Period, e.Parity))
            .OrderBy(e => e.Day).ThenBy(e => e.Period)
            .Select(e => $"{e.GroupName}: {groups[e.GroupId!.Value].Members.Count} žáků se nevejde do učebny {e.RoomName} (kapacita {e.Capacity}), {Slot(e.Day, e.Period)}"));

        // Pupils of a division's classes in none of its groups — free then.
        var divisions = groups.Values.Where(g => g.GroupDivisionId is not null).GroupBy(g => (g.GroupDivisionId, g.Division));
        foreach (var division in divisions.OrderBy(d => d.Key.Division))
        {
            var classIds = division.SelectMany(g => g.Classes).Distinct().ToList();
            var placed = division.SelectMany(g => g.Members).ToHashSet();
            var left = await db.Students.Where(s => classIds.Contains(s.SchoolClassId))
                .Select(s => new { s.Id, s.Person!.FirstName, s.Person.LastName })
                .ToListAsync();
            var names = left.Where(s => !placed.Contains(s.Id)).OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .Select(s => $"{s.FirstName} {s.LastName}").ToList();
            if (names.Count > 0)
            {
                problems.Add($"{division.Key.Division}: {names.Count} {(names.Count == 1 ? "žák není v žádné skupině, má" : names.Count < 5 ? "žáci nejsou v žádné skupině, mají" : "žáků není v žádné skupině, mají")} volno ({string.Join(", ", names.Take(PerKind))}{(names.Count > PerKind ? ", …" : "")}).");
            }
        }
        return problems;
    }

    private record Lesson(
        int ClassId, int? TeacherId, int? ClassroomId, DayOfWeek Day, int Period, WeekParity Parity,
        string ClassName, string Subject, bool NeedsRoom, string? TeacherName, string? RoomName,
        int? GroupId = null, string? GroupName = null, int? Capacity = null)
    {
        public string Who => GroupName ?? ClassName;
    }

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
