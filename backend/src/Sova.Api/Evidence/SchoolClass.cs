namespace Sova.Api.Evidence;

public class SchoolClass
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();

    /// <summary>ROCNIK item for matrika export (1-9 for ZŠ). Null until an admin
    /// fills it in; required before students in this class can be exported.</summary>
    public int? Rocnik { get; set; }
}
