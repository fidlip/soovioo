using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Přidělení výuky: who teaches a subject in a class, per timetable
/// version. A class and subject the school hasn't assigned get a teacher
/// chosen automatically, see <see cref="TeachingPlanner"/>.</summary>
public class TeachingAssignment
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int TeacherId { get; set; }
    public User? Teacher { get; set; }
}
