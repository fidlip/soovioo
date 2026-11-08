using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>One timetable of the school, valid from a date. Published
/// versions follow one another: each is valid until the day before the next
/// published one starts (<see cref="ValidTo"/>, kept by
/// <see cref="TimetableVersions.RecomputeTimetableValidityAsync"/>), the last one
/// until further notice.</summary>
public class TimetableVersion
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public required string Name { get; set; }

    public DateOnly ValidFrom { get; set; }

    /// <summary>Only on a published version that a later one follows.</summary>
    public DateOnly? ValidTo { get; set; }

    public TimetableVersionStatus Status { get; set; }

    /// <summary>The version this one was copied from; null once it's deleted
    /// (only a draft can be), when <see cref="CopiedFromName"/> stays.</summary>
    public int? CopiedFromVersionId { get; set; }
    public TimetableVersion? CopiedFromVersion { get; set; }

    /// <summary>Its name when copied.</summary>
    public string? CopiedFromName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public int? PublishedByUserId { get; set; }

    /// <summary>The day at whose end <see cref="TimetableChangeNotice"/> tells
    /// those concerned; null = nothing waiting.</summary>
    public DateOnly? ChangeNoticeOn { get; set; }

    /// <summary>Moving the start of a version people already heard about tells them again.</summary>
    public DateTimeOffset? ChangeNoticeSentAt { get; set; }

    public List<TimetableEntry> Entries { get; set; } = [];
    public List<CurriculumItem> Curriculum { get; set; } = [];
    public List<ClassCurriculumItem> ClassCurriculum { get; set; } = [];
    public List<GradeMainBlock> GradeMainBlocks { get; set; } = [];
    public List<ClassMainBlock> ClassMainBlocks { get; set; } = [];
    public List<TeachingAssignment> Assignments { get; set; } = [];
    public List<StudentGroup> Groups { get; set; } = [];
    public List<GroupDivision> Divisions { get; set; } = [];
}

public enum TimetableVersionStatus
{
    Draft,
    Published,
}
