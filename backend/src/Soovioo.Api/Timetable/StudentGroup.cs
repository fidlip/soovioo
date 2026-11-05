using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Pupils of one or more classes taught a subject apart from the
/// rest of their class, per timetable version: Aj in two groups, a seminar
/// of some pupils of 9.A and 9.B. A class with a group of a subject doesn't
/// get the subject as a whole class. On its own a group is scheduled on its
/// own; in a <see cref="GroupDivision"/> together with the other groups of it.
/// Its lessons are one <see cref="TimetableEntry"/> per class of the group,
/// each with <see cref="TimetableEntry.StudentGroupId"/>.</summary>
public class StudentGroup
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public required string Name { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    /// <summary>Null = as the učební plán of its classes has the subject;
    /// in a division always the division's.</summary>
    public decimal? HoursPerWeek { get; set; }

    /// <summary>Null = as the subject; in a division the division's.</summary>
    public bool? DoubleLessons { get; set; }

    /// <summary>Null = chosen automatically, see <see cref="TeachingPlanner"/>.</summary>
    public int? TeacherId { get; set; }
    public User? Teacher { get; set; }

    /// <summary>Null = from the subject's classrooms.</summary>
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }

    public int? GroupDivisionId { get; set; }
    public GroupDivision? GroupDivision { get; set; }

    /// <summary>In a division always the division's.</summary>
    public List<StudentGroupClass> Classes { get; set; } = [];

    /// <summary>Pupils of <see cref="Classes"/>; in a division at most one
    /// group of it each.</summary>
    public List<StudentGroupMember> Members { get; set; } = [];
}

/// <summary>Dělení: groups of the same classes with the same hours, always
/// at the same time — Tv 2.A and 2.B girls and boys in two gyms, Nj | Fj.
/// Pupils of its classes in none of its groups are free then.</summary>
public class GroupDivision
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public required string Name { get; set; }

    /// <summary>Null = as the učební plán of its classes has its groups' subjects.</summary>
    public decimal? HoursPerWeek { get; set; }

    public bool? DoubleLessons { get; set; }

    /// <summary>Its groups' (<see cref="StudentGroup.Classes"/>), kept here
    /// for a division without groups yet.</summary>
    public List<int> ClassIds { get; set; } = [];

    public List<StudentGroup> Groups { get; set; } = [];
}

public class StudentGroupClass
{
    public int StudentGroupId { get; set; }
    public StudentGroup? StudentGroup { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SchoolId { get; set; }
}

public class StudentGroupMember
{
    public int StudentGroupId { get; set; }
    public StudentGroup? StudentGroup { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SchoolId { get; set; }
}
