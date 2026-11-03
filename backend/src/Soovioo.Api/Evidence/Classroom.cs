namespace Soovioo.Api.Evidence;

public class Classroom
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Short label shown in timetable cells (e.g. "Tv" for
    /// "Tělocvična"); falls back to <see cref="Name"/> when empty.</summary>
    public string? ShortName { get; set; }

    /// <summary>Preset kind; a subject can take any classroom of it (see
    /// <see cref="SubjectClassroom.Type"/>).</summary>
    public ClassroomType? Type { get; set; }

    public int? Capacity { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
