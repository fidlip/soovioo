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

    // Staff/contact info — real schools track how to reach a teacher or
    // admin directly, separate from their login username.
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
