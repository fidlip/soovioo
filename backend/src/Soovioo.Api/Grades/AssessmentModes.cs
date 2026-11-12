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
/// consent) on how a ročník or a single pupil is assessed: in all subjects
/// (<see cref="SubjectId"/> null), one subject, or behaviour. Exactly one of
/// <see cref="Rocnik"/> and <see cref="StudentId"/>. Without a rule it's
/// classification.</summary>
public class AssessmentModeRule
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int? Rocnik { get; set; }

    public int? StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    /// <summary>Behaviour, not subjects; <see cref="SubjectId"/> is null.</summary>
    public bool Behavior { get; set; }

    public AssessmentMode Mode { get; set; }
}

/// <summary>A pupil's assessment mode — the most specific rule wins: the
/// pupil's in the subject, the pupil's in all subjects, the ročník's in the
/// subject, the ročník's in all subjects; behaviour: the pupil's, the
/// ročník's.</summary>
public sealed class AssessmentModes(IReadOnlyList<AssessmentModeRule> rules)
{
    public static async Task<AssessmentModes> LoadAsync(AppDbContext db) =>
        new(await db.AssessmentModeRules.AsNoTracking().ToListAsync());

    public AssessmentMode For(StudentProfile student, int subjectId) =>
        Find(student, r => !r.Behavior && r.SubjectId == subjectId, r => !r.Behavior && r.SubjectId is null);

    public AssessmentMode ForBehavior(StudentProfile student) =>
        Find(student, r => r.Behavior, r => false);

    private AssessmentMode Find(
        StudentProfile student, Func<AssessmentModeRule, bool> exact, Func<AssessmentModeRule, bool> all)
    {
        var rocnik = student.SchoolClass?.Rocnik;
        var own = rules.Where(r => r.StudentId == student.Id).ToList();
        var year = rocnik is null ? [] : rules.Where(r => r.StudentId is null && r.Rocnik == rocnik).ToList();
        return (own.FirstOrDefault(exact) ?? own.FirstOrDefault(all) ?? year.FirstOrDefault(exact) ?? year.FirstOrDefault(all))
            ?.Mode ?? AssessmentMode.Classification;
    }
}
