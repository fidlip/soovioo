using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>What the caller may do, by the permissions of their active role.
/// The token names the role (<see cref="RoleIdClaim"/>); its permissions are
/// read from the database (through <see cref="RolePermissionCache"/>), so a
/// change to a role — or taking it away from someone — applies to their next
/// request, not only to their next token.</summary>
public static class PermissionAccess
{
    public const string RoleIdClaim = "roleId";

    private static readonly ConditionalWeakTable<ClaimsPrincipal, Task<IReadOnlyDictionary<Permission, PermissionScope>>> Loaded = new();

    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>The kind of the active role (the token's role claim); null
    /// for SuperAdmin.</summary>
    public static RoleKind? ActiveKind(this ClaimsPrincipal user) =>
        Enum.TryParse<RoleKind>(user.FindFirstValue(ClaimTypes.Role), out var kind) ? kind : null;

    public static bool IsStaff(this ClaimsPrincipal user) => user.ActiveKind() == RoleKind.Staff;

    public static Task<IReadOnlyDictionary<Permission, PermissionScope>> PermissionsAsync(
        this ClaimsPrincipal user, AppDbContext db) =>
        Loaded.GetValue(user, _ => LoadAsync(user, db));

    public static async Task<PermissionScope> ScopeAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission) =>
        (await user.PermissionsAsync(db)).GetValueOrDefault(permission);

    public static async Task<bool> HasPermissionAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission) =>
        await user.ScopeAsync(db, permission) != PermissionScope.None;

    /// <summary>Classes the permission reaches; null = the whole school.</summary>
    public static async Task<HashSet<int>?> ClassIdsAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission)
    {
        switch (await user.ScopeAsync(db, permission))
        {
            case PermissionScope.School:
                return null;
            case PermissionScope.ClassTeacher:
                return (await db.GetOwnClassIdsAsClassTeacherAsync(user.UserId())).ToHashSet();
            case PermissionScope.MyClasses:
                return (await db.GetTeacherClassIdsAsync(user.UserId())).ToHashSet();
            case PermissionScope.Own:
                var own = await user.OwnStudentIdsAsync(db);
                return (await db.Students.Where(s => own.Contains(s.Id)).Select(s => s.SchoolClassId).ToListAsync()).ToHashSet();
            default:
                return [];
        }
    }

    public static async Task<bool> ReachesClassAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission, int schoolClassId) =>
        await user.ClassIdsAsync(db, permission) is not { } ids || ids.Contains(schoolClassId);

    /// <summary>Like <see cref="ReachesClassAsync"/>, but "my classes" means
    /// teaching that subject there (homework).</summary>
    public static async Task<bool> ReachesClassSubjectAsync(
        this ClaimsPrincipal user, AppDbContext db, Permission permission, int schoolClassId, int subjectId) =>
        await user.ScopeAsync(db, permission) switch
        {
            PermissionScope.School => true,
            PermissionScope.MyClasses => await db.TeachesClassInSubjectAsync(user.UserId(), schoolClassId, subjectId),
            PermissionScope.ClassTeacher => await db.IsClassTeacherOfAsync(user.UserId(), schoolClassId),
            _ => false,
        };

    /// <summary>Pupils the permission reaches; null = the whole school.</summary>
    public static async Task<HashSet<int>?> StudentIdsAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission)
    {
        var scope = await user.ScopeAsync(db, permission);
        if (scope == PermissionScope.Own)
        {
            return await user.OwnStudentIdsAsync(db);
        }
        if (await user.ClassIdsAsync(db, permission) is not { } classIds)
        {
            return null;
        }
        return (await db.Students.Where(s => classIds.Contains(s.SchoolClassId)).Select(s => s.Id).ToListAsync()).ToHashSet();
    }

    public static async Task<bool> ReachesStudentAsync(this ClaimsPrincipal user, AppDbContext db, Permission permission, int studentId) =>
        await user.StudentIdsAsync(db, permission) is not { } ids || ids.Contains(studentId);

    /// <summary>The role's permissions as they apply — stored rows plus
    /// Admin's administration, which isn't stored.</summary>
    public static async Task<IReadOnlyDictionary<Permission, PermissionScope>> EffectivePermissionsAsync(AppDbContext db, int roleId)
    {
        var role = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Id == roleId)
            .Select(r => new { r.BuiltIn, Permissions = r.Permissions.Select(p => new { p.Permission, p.Scope }).ToList() })
            .FirstOrDefaultAsync();
        return role is null ? new Dictionary<Permission, PermissionScope>() : Effective(role.BuiltIn, role.Permissions.Select(p => (p.Permission, p.Scope)));
    }

    public static IReadOnlyDictionary<Permission, PermissionScope> Effective(
        UserRole? builtIn, IEnumerable<(Permission Permission, PermissionScope Scope)> stored)
    {
        var permissions = stored
            .Where(p => p.Scope != PermissionScope.None && p.Permission != Permission.Administration)
            .ToDictionary(p => p.Permission, p => p.Scope);
        if (builtIn == UserRole.Admin)
        {
            permissions[Permission.Administration] = PermissionScope.School;
        }
        return permissions;
    }

    private static async Task<IReadOnlyDictionary<Permission, PermissionScope>> LoadAsync(ClaimsPrincipal user, AppDbContext db)
    {
        if (!int.TryParse(user.FindFirstValue(RoleIdClaim), out var roleId)
            || !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return new Dictionary<Permission, PermissionScope>();
        }
        // Support mode: the SuperAdmin acts as the school's Admin without
        // being one of its accounts.
        var support = user.HasClaim(c => c.Type == Support.SupportClaims.SessionId);
        var cache = RolePermissionCache.Of(db);
        if (cache is not null && cache.TryGet((userId, roleId, support), out var cached))
        {
            return cached;
        }
        var stillHasIt = await db.Roles.AnyAsync(r => r.Id == roleId
            && (support ? r.BuiltIn == UserRole.Admin : r.Users.Any(u => u.Id == userId)));
        var permissions = stillHasIt ? await EffectivePermissionsAsync(db, roleId) : new Dictionary<Permission, PermissionScope>();
        cache?.Set((userId, roleId, support), permissions);
        return permissions;
    }
}

/// <summary>The endpoint needs <paramref name="permission"/> in some scope
/// (403 otherwise) — narrower scopes are checked inside, per class or pupil.
/// Goes together with <c>[Authorize]</c>; a SuperAdmin is left to
/// <see cref="SuperAdminScopeFilter"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute(Permission permission) : Attribute, IAsyncAuthorizationFilter
{
    public Permission Permission { get; } = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true || user.IsInRole(nameof(UserRole.SuperAdmin)))
        {
            return;
        }
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if (!await user.HasPermissionAsync(db, Permission))
        {
            context.Result = new ForbidResult();
        }
    }
}
