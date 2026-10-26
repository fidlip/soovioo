using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

/// <summary>What sort of person a role is for. Fixed once the role exists —
/// visibility of personal data, the directory, notifications, photos and
/// retention go by it, and so does what "own" means in a
/// <see cref="PermissionScope"/> (a parent's children, a pupil themselves,
/// a member of staff's classes).</summary>
public enum RoleKind
{
    Staff,
    Parent,
    Student,
}

/// <summary>A school's role — the four built-in ones every school gets
/// (<see cref="BuiltIn"/> set) and any the school adds. What it may do is
/// <see cref="Permissions"/>; see <see cref="PermissionCatalog"/>.</summary>
public class Role
{
    public int Id { get; set; }
    public int SchoolId { get; set; }
    public School? School { get; set; }
    public required string Name { get; set; }
    public RoleKind Kind { get; set; }

    /// <summary>Which built-in role this is, null for the school's own.
    /// Built-in roles can be renamed and (except Admin's administration)
    /// given other permissions, not deleted. Teacher is who "teachers" are
    /// (the timetable, event recipients) and needs a teacher's profile;
    /// Admin always keeps <see cref="Permission.Administration"/>.</summary>
    public UserRole? BuiltIn { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
    public List<User> Users { get; set; } = [];
}

public record RoleRefDto(int Id, string Name, RoleKind Kind, UserRole? BuiltIn)
{
    public static RoleRefDto Of(Role role) => new(role.Id, role.Name, role.Kind, role.BuiltIn);

    /// <summary>Built-in ones first (Admin, Teacher, Parent, Student), then
    /// the school's own by name.</summary>
    public static IReadOnlyList<RoleRefDto> ListOf(IEnumerable<Role> roles) =>
        roles.OrderBy(r => r.BuiltIn is null).ThenBy(r => r.BuiltIn).ThenBy(r => r.Name).Select(Of).ToList();
}

/// <summary>One cell of the permission matrix. A permission without a row
/// is <see cref="PermissionScope.None"/>.</summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public Permission Permission { get; set; }
    public PermissionScope Scope { get; set; }
}
