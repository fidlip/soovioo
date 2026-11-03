using System.Text.Json.Serialization;

namespace Soovioo.Api.Evidence;

/// <summary>One of a <see cref="Subject"/>'s classrooms: a particular one,
/// or any of a type.</summary>
public class SubjectClassroom
{
    public int Position { get; set; }
    public int? ClassroomId { get; set; }
    [JsonIgnore]
    public Classroom? Classroom { get; set; }
    public ClassroomType? Type { get; set; }
}
