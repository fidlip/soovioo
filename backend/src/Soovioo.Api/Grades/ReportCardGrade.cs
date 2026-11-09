using Soovioo.Api.Behavior;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

/// <summary>A stupeň on the report card as the word or as the digit.</summary>
public enum ReportCardMarkFormat
{
    /// <summary>"výborný", behaviour "velmi dobré".</summary>
    Words,

    /// <summary>"1", behaviour "1".</summary>
    Digits,
}

public enum ReportCardGradeKind
{
    /// <summary>A stupeň 1–5, <see cref="ReportCardGrade.Value"/>.</summary>
    Graded,

    /// <summary>Uvolněn/a.</summary>
    Exempt,

    /// <summary>Nehodnocen/a.</summary>
    NotGraded,
}

/// <summary>The grade on the report card (vysvědčení) in a subject for a
/// half-year, once a teacher changed or confirmed it — until then it is the
/// rounded average (<see cref="ReportCards"/>), not stored. One row per
/// (student, subject, school year, half), composite key in AppDbContext.
/// Confirmed = closed: only the school's management may reopen it.</summary>
public class ReportCardGrade
{
    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int SchoolYearStartYear { get; set; }
    public SchoolHalfYear HalfYear { get; set; }

    public int SchoolId { get; set; }

    public ReportCardGradeKind Kind { get; set; }

    /// <summary>1–5 for <see cref="ReportCardGradeKind.Graded"/>, else null.</summary>
    public int? Value { get; set; }

    public required string SetByName { get; set; }
    public DateTimeOffset SetAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ConfirmedByName { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
