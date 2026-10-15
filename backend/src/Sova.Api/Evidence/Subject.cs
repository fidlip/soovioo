namespace Sova.Api.Evidence;

public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
