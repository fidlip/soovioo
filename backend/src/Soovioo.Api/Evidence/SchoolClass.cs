using System.Text.Json.Serialization;
using Soovioo.Api.Auth;

namespace Soovioo.Api.Evidence;

public class SchoolClass
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public ICollection<StudentProfile> Students { get; set; } = new List<StudentProfile>();

    /// <summary>ROCNIK item for matrika export (1-9 for ZŠ). Null until an admin
    /// fills it in; required before students in this class can be exported.</summary>
    public int? Rocnik { get; set; }

    /// <summary>Třídní učitel — real schools track this per class. Nullable; no
    /// FK constraint enforcing the referenced user is actually a Teacher (not
    /// worth the extra validation complexity for a display-only field).</summary>
    public int? ClassTeacherId { get; set; }
    public User? ClassTeacher { get; set; }

    /// <summary>JAZYK_O item for matrika — vyučovací jazyk třídy. Free text
    /// (e.g. "čeština") rather than a matrika language code, since we don't
    /// have that číselník yet; null defaults to Czech in practice.</summary>
    public string? TeachingLanguage { get; set; }

    /// <summary>Kmenová učebna — where the class has subjects without their own
    /// classrooms and what "any homeroom" on a subject means for it.</summary>
    public int? HomeroomId { get; set; }
    [JsonIgnore]
    public Classroom? Homeroom { get; set; }
}
