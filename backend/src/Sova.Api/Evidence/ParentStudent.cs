using Sova.Api.Auth;

namespace Sova.Api.Evidence;

/// <summary>Links a Parent-role user to the student(s) they may see/act on.</summary>
public class ParentStudent
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }
}
