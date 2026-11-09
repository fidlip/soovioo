using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Grades;

public enum ReportCardResult
{
    /// <summary>Prospěl/a s vyznamenáním.</summary>
    WithHonours,

    /// <summary>Prospěl/a.</summary>
    Passed,

    /// <summary>Neprospěl/a.</summary>
    Failed,

    /// <summary>Nehodnocen/a.</summary>
    NotAssessed,
}

/// <param name="Average">Weighted average of the half-year's grades; null
/// without any.</param>
/// <param name="Stored">Changed or confirmed by a teacher; null = the
/// suggestion from the average.</param>
public record ReportCardSubject(int SubjectId, string SubjectName, double? Average, int GradeCount, ReportCardGrade? Stored)
{
    public int? Suggested => Average is { } average
        ? (int)Math.Clamp(Math.Round(average, MidpointRounding.AwayFromZero), 1, 5)
        : null;

    public bool Confirmed => Stored?.ConfirmedAt is not null;

    public ReportCardGradeKind? Kind => Stored?.Kind ?? (Suggested is null ? null : ReportCardGradeKind.Graded);

    public int? Value => Stored is { } stored ? stored.Value : Suggested;
}

/// <param name="UnexcusedHours">Everything not excused, as Docházka's
/// summary counts it.</param>
public record ReportCard(
    StudentProfile Student,
    BehaviorClassificationValue Behavior,
    int ExcusedHours,
    int UnexcusedHours,
    IReadOnlyList<ReportCardSubject> Subjects)
{
    /// <summary>Only once every subject is confirmed.</summary>
    public ReportCardResult? Result(SchoolHalfYear half) =>
        Subjects.Count > 0 && Subjects.All(s => s.Confirmed)
            ? ReportCards.Result(Subjects.Select(s => (s.Kind!.Value, s.Value)), Behavior, half)
            : null;
}

/// <summary>The report card (vysvědčení) of a half-year: the pupil's subjects
/// (their timetable's, plus any they have a grade or a report card grade in),
/// the grades' averages, the report card grades, behaviour and missed
/// lessons.</summary>
public static class ReportCards
{
    private static readonly StringComparer Czech = StringComparer.Create(SchoolFormat.Culture, true);

    public static async Task<List<ReportCard>> BuildAsync(
        AppDbContext db, IReadOnlyCollection<int> studentIds, int schoolYearStartYear, SchoolHalfYear half)
    {
        var (from, to) = HalfYears.Range(schoolYearStartYear, half);
        var students = await db.Students
            .Include(s => s.Person)
            .Include(s => s.SchoolClass)
            .Where(s => studentIds.Contains(s.Id))
            .ToListAsync();
        var classIds = students.Select(s => s.SchoolClassId).Distinct().ToList();

        var timetableDay = db.Today < from ? from : db.Today > to ? to : db.Today;
        var lessons = await db.TimetableEntriesOn(timetableDay)
            .Where(e => classIds.Contains(e.SchoolClassId))
            .Select(e => new { e.SchoolClassId, e.SubjectId, e.StudentGroupId })
            .Distinct()
            .ToListAsync();
        var groups = (await db.StudentGroupMembers
                .Where(m => studentIds.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.StudentGroupId })
                .ToListAsync())
            .ToLookup(m => m.StudentId, m => m.StudentGroupId);
        var covered = await db.CoveredSubjectIdsAsync();

        var grades = (await db.Grades
                .Where(g => studentIds.Contains(g.StudentId) && g.GradedOn >= from && g.GradedOn <= to)
                .Select(g => new { g.StudentId, g.SubjectId, g.Value, g.Weight })
                .ToListAsync())
            .ToLookup(g => g.StudentId);
        var stored = (await db.ReportCardGrades
                .Where(r => studentIds.Contains(r.StudentId)
                    && r.SchoolYearStartYear == schoolYearStartYear && r.HalfYear == half)
                .ToListAsync())
            .ToLookup(r => r.StudentId);
        var behavior = await db.BehaviorClassifications
            .Where(c => studentIds.Contains(c.StudentId)
                && c.SchoolYearStartYear == schoolYearStartYear && c.HalfYear == half)
            .ToDictionaryAsync(c => c.StudentId, c => c.Value);
        var absences = (await db.Absences
                .Where(a => studentIds.Contains(a.StudentId) && a.Date >= from && a.Date <= to)
                .Select(a => new { a.StudentId, a.Status, a.Periods })
                .ToListAsync())
            .ToLookup(a => a.StudentId);

        var subjectIds = lessons.Select(l => l.SubjectId)
            .Concat(grades.SelectMany(g => g).Select(g => g.SubjectId))
            .Concat(stored.SelectMany(r => r).Select(r => r.SubjectId))
            .ToHashSet();
        var subjectNames = await db.Subjects
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        return [.. students
            .OrderBy(s => s.SchoolClass!.Name, Czech)
            .ThenBy(s => s.Person!.LastName, Czech)
            .ThenBy(s => s.Person!.FirstName, Czech)
            .Select(student =>
            {
                var own = grades[student.Id].ToList();
                var ownStored = stored[student.Id].ToDictionary(r => r.SubjectId);
                var ids = lessons
                    .Where(l => l.SchoolClassId == student.SchoolClassId
                        && (l.StudentGroupId is not { } group || groups[student.Id].Contains(group)))
                    .Select(l => l.SubjectId)
                    .Where(id => !covered.Contains(id))
                    .Concat(own.Select(g => g.SubjectId))
                    .Concat(ownStored.Keys)
                    .Where(subjectNames.ContainsKey)
                    .Distinct();
                var subjects = ids
                    .Select(id =>
                    {
                        var inSubject = own.Where(g => g.SubjectId == id).ToList();
                        var weight = inSubject.Sum(g => g.Weight);
                        return new ReportCardSubject(
                            id,
                            subjectNames[id],
                            weight > 0 ? inSubject.Sum(g => g.Value * g.Weight) / (double)weight : null,
                            inSubject.Count,
                            ownStored.GetValueOrDefault(id));
                    })
                    .OrderBy(s => s.SubjectName, Czech)
                    .ToList();
                var missed = absences[student.Id].ToList();
                return new ReportCard(
                    student,
                    behavior.GetValueOrDefault(student.Id, BehaviorClassificationValue.VelmiDobre),
                    missed.Where(a => a.Status == AbsenceStatus.Excused).Sum(a => a.Periods.Count),
                    missed.Where(a => a.Status != AbsenceStatus.Excused).Sum(a => a.Periods.Count),
                    subjects);
            })];
    }

    /// <summary>Celkové hodnocení: s vyznamenáním — nothing worse than 2, an
    /// average of at most 1.5 and very good behaviour; prospěl/a — no 5;
    /// nehodnocen/a in a subject leaves the first half not assessed and
    /// fails the second. Exempt subjects don't count.</summary>
    public static ReportCardResult? Result(
        IEnumerable<(ReportCardGradeKind Kind, int? Value)> marks, BehaviorClassificationValue behavior, SchoolHalfYear half)
    {
        var list = marks.ToList();
        if (list.Any(m => m.Kind == ReportCardGradeKind.NotGraded))
        {
            return half == SchoolHalfYear.First ? ReportCardResult.NotAssessed : ReportCardResult.Failed;
        }
        var values = list.Where(m => m.Kind == ReportCardGradeKind.Graded).Select(m => m.Value!.Value).ToList();
        if (values.Count == 0)
        {
            return null;
        }
        if (values.Contains(5))
        {
            return ReportCardResult.Failed;
        }
        return values.Max() <= 2 && values.Average() <= 1.5 && behavior == BehaviorClassificationValue.VelmiDobre
            ? ReportCardResult.WithHonours
            : ReportCardResult.Passed;
    }
}
