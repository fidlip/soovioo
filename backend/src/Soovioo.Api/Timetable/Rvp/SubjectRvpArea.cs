using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable.Rvp;

/// <summary>The school's own choice of the area a subject counts towards in a
/// program, where it isn't what <see cref="RvpSubjects"/> recognises.</summary>
public class SubjectRvpArea
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public required string ProgramCode { get; set; }

    /// <summary>Null = only towards the disponibilní hours.</summary>
    public string? AreaKey { get; set; }
}
