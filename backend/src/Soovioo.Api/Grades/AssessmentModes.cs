using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

/// <summary>How a subject or behaviour is assessed on the report card
/// (§ 15 of vyhláška 48/2005 Sb.).</summary>
public enum AssessmentMode
{
    /// <summary>A stupeň.</summary>
    Classification,

    /// <summary>Slovní hodnocení (kriteriální counts as slovní). The teacher
    /// also picks the stupeň it corresponds to, for the overall result and
    /// a transfer to a school that classifies (§ 51 odst. 3 školského
    /// zákona) — never printed, families don't see it.</summary>
    Verbal,

    /// <summary>A stupeň with a slovní hodnocení added (§ 15 odst. 6).</summary>
    ClassificationWithVerbal,
}

/// <summary>The school's decision (the director's, with the school board's
/// consent) on how a ročník, a class or a single pupil is assessed: in all
/// subjects (<see cref="SubjectId"/> null), one subject, or behaviour.
/// Exactly one of <see cref="Rocnik"/>, <see cref="SchoolClassId"/> and
/// <see cref="StudentId"/>. Without a rule it's classification, written as
/// <see cref="School.ReportCardMarkFormat"/>.</summary>
public class AssessmentModeRule
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int? Rocnik { get; set; }

    public int? SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int? StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    /// <summary>Behaviour, not subjects; <see cref="SubjectId"/> is null.</summary>
    public bool Behavior { get; set; }

    public AssessmentMode Mode { get; set; }

    /// <summary>How the stupeň is written (§ 15 odst. 3 of vyhláška 48/2005
    /// Sb.); null = as a less specific rule, or the school, has it. Always
    /// null with <see cref="AssessmentMode.Verbal"/>.</summary>
    public ReportCardMarkFormat? MarkFormat { get; set; }
}

public readonly record struct Assessment(AssessmentMode Mode, ReportCardMarkFormat MarkFormat);

/// <summary>A pupil's assessment — the most specific rule wins: the pupil's
/// in the subject, the pupil's in all subjects, then the class's and the
/// ročník's the same way; behaviour: the pupil's, the class's, the
/// ročník's. The mark format is looked up on its own, from the most specific
/// rule that sets one.</summary>
public sealed class AssessmentModes(
    IReadOnlyList<AssessmentModeRule> rules, IReadOnlyDictionary<int, ReportCardMarkFormat> schoolFormats)
{
    public static async Task<AssessmentModes> LoadAsync(AppDbContext db, IReadOnlyCollection<int> schoolIds) =>
        new(await db.AssessmentModeRules.AsNoTracking().Where(r => schoolIds.Contains(r.SchoolId)).ToListAsync(),
            await db.Schools.Where(s => schoolIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ReportCardMarkFormat));

    public Assessment For(StudentProfile student, int subjectId) =>
        Find(student, r => !r.Behavior && r.SubjectId == subjectId, r => !r.Behavior && r.SubjectId is null);

    public Assessment ForBehavior(StudentProfile student) =>
        Find(student, r => r.Behavior, r => false);

    private Assessment Find(
        StudentProfile student, Func<AssessmentModeRule, bool> exact, Func<AssessmentModeRule, bool> all)
    {
        var rocnik = student.SchoolClass?.Rocnik;
        var levels = new[]
        {
            rules.Where(r => r.StudentId == student.Id).ToList(),
            rules.Where(r => r.SchoolClassId == student.SchoolClassId).ToList(),
            rocnik is null ? [] : rules.Where(r => r.StudentId is null && r.SchoolClassId is null && r.Rocnik == rocnik).ToList(),
        };
        var applying = levels.SelectMany(level => level.Where(exact).Concat(level.Where(all))).ToList();
        return new(
            applying.FirstOrDefault()?.Mode ?? AssessmentMode.Classification,
            applying.FirstOrDefault(r => r.MarkFormat is not null)?.MarkFormat
                ?? schoolFormats.GetValueOrDefault(student.SchoolId, ReportCardMarkFormat.Words));
    }
}
