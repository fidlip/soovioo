using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>Administrace → Role: the school's roles and what each may do.
/// Built-in roles can be renamed and given other permissions, not deleted;
/// Admin's administration is fixed (<see cref="Permission.Administration"/>
/// isn't in the matrix). A change applies to everyone with the role on their
/// next request (<see cref="PermissionAccess"/>).</summary>
[Authorize]
[RequirePermission(Permission.Administration)]
[ApiController]
[Route("api/roles")]
[AuditArea("Role a práva")]
public class RolesController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetRoles()
    {
        var roles = await db.Roles.Include(r => r.Permissions).ToListAsync();
        var counts = await db.Users.Where(u => u.ErasedAt == null)
            .SelectMany(u => u.Roles.Select(r => r.Id))
            .GroupBy(id => id)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);
        return Ok(RoleRefDto.ListOf(roles)
            .Select(r => ToDto(roles.First(x => x.Id == r.Id), counts.GetValueOrDefault(r.Id))));
    }

    [HttpGet("catalog")]
    public ActionResult<IEnumerable<PermissionInfo>> GetCatalog() => Ok(PermissionCatalog.All);

    /// <summary>A new role, with the permissions of <see cref="CreateRoleRequest.CopyFromRoleId"/>
    /// (as far as its kind allows them) or none.</summary>
    [HttpPost]
    [AuditAction("Vytvořena role")]
    public async Task<ActionResult<RoleDto>> AddRole(CreateRoleRequest request)
    {
        if (await NameErrorAsync(request.Name, null) is { } nameError)
        {
            return nameError;
        }
        Role? source = null;
        if (request.CopyFromRoleId is { } sourceId
            && (source = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == sourceId)) is null)
        {
            return BadRequest("Neznámá role.");
        }

        var role = new Role
        {
            SchoolId = currentSchool.SchoolId!.Value,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            Permissions = PermissionCatalog.All.Select(i =>
            {
                var scope = source?.Permissions.FirstOrDefault(p => p.Permission == i.Permission)?.Scope ?? PermissionScope.None;
                return new RolePermission
                {
                    Permission = i.Permission,
                    Scope = PermissionCatalog.Allows(i.Permission, request.Kind, scope) ? scope : PermissionScope.None,
                };
            }).ToList(),
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetRoles), ToDto(role, 0));
    }

    /// <summary>Name and the whole matrix; a permission left out is none.</summary>
    [HttpPut("{id:int}")]
    [AuditAction("Upravena role a její práva")]
    public async Task<ActionResult<RoleDto>> UpdateRole(int id, UpdateRoleRequest request)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return NotFound();
        }
        if (await NameErrorAsync(request.Name, id) is { } nameError)
        {
            return nameError;
        }
        var permissions = request.Permissions ?? new Dictionary<Permission, PermissionScope>();
        foreach (var (permission, scope) in permissions)
        {
            if (!PermissionCatalog.Allows(permission, role.Kind, scope))
            {
                return BadRequest($"{PermissionCatalog.Info(permission).Label}: tento rozsah role nemůže mít.");
            }
        }

        role.Name = request.Name.Trim();
        foreach (var info in PermissionCatalog.All)
        {
            var scope = permissions.GetValueOrDefault(info.Permission);
            if (role.Permissions.FirstOrDefault(p => p.Permission == info.Permission) is { } row)
            {
                row.Scope = scope;
            }
            else
            {
                role.Permissions.Add(new RolePermission { Permission = info.Permission, Scope = scope });
            }
        }
        await db.SaveChangesAsync();
        return Ok(ToDto(role, await db.Users.CountAsync(u => u.ErasedAt == null && u.Roles.Any(r => r.Id == id))));
    }

    /// <summary>Only a school's own role. Whoever has it moves to
    /// <paramref name="reassignToRoleId"/> (same kind) in the same step —
    /// without one, a role somebody has can't be deleted.</summary>
    [HttpDelete("{id:int}")]
    [AuditAction("Smazána role")]
    public async Task<IActionResult> DeleteRole(int id, int? reassignToRoleId = null)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return NotFound();
        }
        if (role.BuiltIn is not null)
        {
            return BadRequest("Vestavěnou roli nelze smazat.");
        }

        var users = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == role.SchoolId && u.Roles.Any(r => r.Id == id))
            .ToListAsync();
        if (users.Count > 0)
        {
            if (reassignToRoleId is not { } targetId)
            {
                return BadRequest(
                    $"Role je přiřazená {users.Count} {(users.Count == 1 ? "účtu" : "účtům")} — vyberte, na jakou roli je přeřadit.");
            }
            var target = await db.Roles.FirstOrDefaultAsync(r => r.Id == targetId);
            if (target is null || target.Id == id || target.Kind != role.Kind)
            {
                return BadRequest("Přeřadit jde jen na jinou roli stejného druhu.");
            }
            foreach (var user in users)
            {
                var wasActive = user.ActiveRoleId == id;
                user.Roles.Remove(role);
                if (!user.Roles.Contains(target))
                {
                    user.Roles.Add(target);
                }
                if (wasActive)
                {
                    user.ActiveRole = target;
                }
            }
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult?> NameErrorAsync(string? name, int? id)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return this.FieldError("name", "Název je povinný.");
        }
        var trimmed = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Id != id && r.Name == trimmed))
        {
            return this.FieldError("name", "Role s tímto názvem už existuje.");
        }
        return null;
    }

    private static RoleDto ToDto(Role role, int userCount) => new(
        role.Id, role.Name, role.Kind, role.BuiltIn, userCount,
        PermissionCatalog.All.ToDictionary(
            i => i.Permission,
            i => role.Permissions.FirstOrDefault(p => p.Permission == i.Permission)?.Scope ?? PermissionScope.None));
}

/// <param name="Permissions">Every permission of the catalog with its scope
/// (Admin's administration isn't one of them).</param>
public record RoleDto(
    int Id, string Name, RoleKind Kind, UserRole? BuiltIn, int UserCount,
    IReadOnlyDictionary<Permission, PermissionScope> Permissions);

public record CreateRoleRequest(string Name, RoleKind Kind, int? CopyFromRoleId = null);

public record UpdateRoleRequest(string Name, Dictionary<Permission, PermissionScope>? Permissions);
