using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.SchoolYears;
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

/// <param name="Average">Weighted average of the period's grades; null
/// without any.</param>
/// <param name="Stored">Changed, confirmed or with a slovní hodnocení; its
/// Kind null = the suggestion from the average.</param>
public record ReportCardSubject(
    int SubjectId, string SubjectName, double? Average, int GradeCount, ReportCardGrade? Stored,
    AssessmentMode Mode = AssessmentMode.Classification,
    ReportCardMarkFormat MarkFormat = ReportCardMarkFormat.Words)
{
    public int? Suggested => Average is { } average
        ? (int)Math.Clamp(Math.Round(average, MidpointRounding.AwayFromZero), 1, 5)
        : null;

    public bool Confirmed => Stored?.ConfirmedAt is not null;

    /// <summary>Changed by a teacher.</summary>
    public bool Changed => Stored?.Kind is not null;

    public ReportCardGradeKind? Kind => Stored?.Kind ?? (Suggested is null ? null : ReportCardGradeKind.Graded);

    public int? Value => Changed ? Stored!.Value : Suggested;

    public string? VerbalText => Stored?.VerbalText;

    /// <summary>A slovní hodnocení goes with it — not with uvolněn/a or
    /// nehodnocen/a.</summary>
    public bool NeedsText => Mode != AssessmentMode.Classification && Kind == ReportCardGradeKind.Graded;

    /// <summary>Has what it takes to be confirmed.</summary>
    public bool Complete => Kind is not null && (!NeedsText || !string.IsNullOrWhiteSpace(VerbalText));
}

/// <param name="UnexcusedHours">Everything not excused, as Docházka's
/// summary counts it.</param>
public record ReportCard(
    StudentProfile Student,
    BehaviorClassificationValue Behavior,
    int ExcusedHours,
    int UnexcusedHours,
    IReadOnlyList<ReportCardSubject> Subjects,
    AssessmentMode BehaviorMode = AssessmentMode.Classification,
    ReportCardNote? Note = null,
    ReportCardMarkFormat BehaviorMarkFormat = ReportCardMarkFormat.Words)
{
    /// <summary>The souhrnné slovní hodnocení may replace the subjects: on
    /// a výpis (not the year's final report card), when every subject is
    /// assessed only slovně.</summary>
    public bool SummaryAllowed(bool final) =>
        !final && Subjects.Count > 0 && Subjects.All(s => s.Mode == AssessmentMode.Verbal);

    /// <summary>The souhrnné hodnocení stands in for the subjects.</summary>
    public bool Summarised(bool final) =>
        SummaryAllowed(final) && !string.IsNullOrWhiteSpace(Note?.SummaryText);

    public bool SummaryConfirmed => Note?.SummaryConfirmedAt is not null;

    /// <summary>Any slovní hodnocení — the report card goes on list A/B.</summary>
    public bool HasVerbal(bool final) =>
        BehaviorMode != AssessmentMode.Classification
        || Summarised(final)
        || Subjects.Any(s => s.Mode != AssessmentMode.Classification);

    /// <summary>Only once every subject is confirmed — or the souhrnné
    /// hodnocení, with the result the třídní chose.</summary>
    public ReportCardResult? Result(bool final) =>
        Summarised(final)
            ? SummaryConfirmed ? Note!.SummaryResult : null
            : Subjects.Count > 0 && Subjects.All(s => s.Confirmed)
                ? ReportCards.Result(Subjects.Select(s => (s.Kind!.Value, s.Value)), Behavior, final)
                : null;
}

/// <summary>The report card (vysvědčení) of a grading period: the pupil's subjects
/// (their timetable's, plus any they have a grade or a report card grade in),
/// the grades' averages, the report card grades, behaviour and missed
/// lessons.</summary>
public static class ReportCards
{
    private static readonly StringComparer Czech = StringComparer.Create(SchoolFormat.Culture, true);

    public static async Task<List<ReportCard>> BuildAsync(
        AppDbContext db, IReadOnlyCollection<int> studentIds, ReportPeriod period)
    {
        var (from, to) = (period.From, period.To);
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
                .Where(r => studentIds.Contains(r.StudentId) && r.GradingPeriodId == period.Id)
                .ToListAsync())
            .ToLookup(r => r.StudentId);
        var notes = await db.ReportCardNotes
            .Where(n => studentIds.Contains(n.StudentId) && n.GradingPeriodId == period.Id)
            .ToDictionaryAsync(n => n.StudentId);
        var modes = await AssessmentModes.LoadAsync(db, [.. students.Select(s => s.SchoolId).Distinct()]);
        var behavior = await db.BehaviorClassifications
            .Where(c => studentIds.Contains(c.StudentId) && c.GradingPeriodId == period.Id)
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
                        var assessment = modes.For(student, id);
                        return new ReportCardSubject(
                            id,
                            subjectNames[id],
                            weight > 0 ? inSubject.Sum(g => g.Value * g.Weight) / (double)weight : null,
                            inSubject.Count,
                            ownStored.GetValueOrDefault(id),
                            assessment.Mode,
                            assessment.MarkFormat);
                    })
                    .OrderBy(s => s.SubjectName, Czech)
                    .ToList();
                var missed = absences[student.Id].ToList();
                var behaviorAssessment = modes.ForBehavior(student);
                return new ReportCard(
                    student,
                    behavior.GetValueOrDefault(student.Id, BehaviorClassificationValue.VelmiDobre),
                    missed.Where(a => a.Status == AbsenceStatus.Excused).Sum(a => a.Periods.Count),
                    missed.Where(a => a.Status != AbsenceStatus.Excused).Sum(a => a.Periods.Count),
                    subjects,
                    behaviorAssessment.Mode,
                    notes.GetValueOrDefault(student.Id),
                    behaviorAssessment.MarkFormat);
            })];
    }

    /// <summary>Celkové hodnocení (§ 16 odst. 3 and 4 of vyhláška 48/2005
    /// Sb.): s vyznamenáním — nothing worse than 2, an average of at most 1.5
    /// and very good behaviour; prospěl/a — no 5; a 5 fails; nehodnocen/a in a
    /// subject leaves the first half not assessed and fails the year's final
    /// report card — a 5 together with nehodnocen/a fails either (MŠMT's
    /// guidance on report cards, 6.12). Exempt subjects don't count.</summary>
    public static ReportCardResult? Result(
        IEnumerable<(ReportCardGradeKind Kind, int? Value)> marks, BehaviorClassificationValue behavior, bool final)
    {
        var list = marks.ToList();
        var values = list.Where(m => m.Kind == ReportCardGradeKind.Graded).Select(m => m.Value!.Value).ToList();
        if (values.Contains(5))
        {
            return ReportCardResult.Failed;
        }
        if (list.Any(m => m.Kind == ReportCardGradeKind.NotGraded))
        {
            return final ? ReportCardResult.Failed : ReportCardResult.NotAssessed;
        }
        if (values.Count == 0)
        {
            return null;
        }
        return values.Max() <= 2 && values.Average() <= 1.5 && behavior == BehaviorClassificationValue.VelmiDobre
            ? ReportCardResult.WithHonours
            : ReportCardResult.Passed;
    }
}
