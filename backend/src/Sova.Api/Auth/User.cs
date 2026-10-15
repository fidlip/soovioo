using Sova.Api.Evidence;

namespace Sova.Api.Auth;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }

    /// <summary>Null only for SuperAdmin — every other role belongs to one School.</summary>
    public int? SchoolId { get; set; }
    public School? School { get; set; }
}
