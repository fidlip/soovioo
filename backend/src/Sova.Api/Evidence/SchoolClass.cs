namespace Sova.Api.Evidence;

public class SchoolClass
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();
}
