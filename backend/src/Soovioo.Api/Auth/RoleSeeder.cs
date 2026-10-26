using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

/// <summary>Every school has the built-in roles, and every role a row for
/// every permission of <see cref="PermissionCatalog"/> — so a permission
/// added to the catalog later starts at the built-in default (or none for a
/// school's own role) instead of being silently missing.</summary>
public static class RoleSeeder
{
    /// <summary>At startup, for every school.</summary>
    public static void Ensure(AppDbContext db)
    {
        var schools = db.Schools.IgnoreQueryFilters()
            .Include(s => s.Roles).ThenInclude(r => r.Permissions)
            .AsSplitQuery()
            .ToList();
        foreach (var school in schools)
        {
            AddMissing(school);
        }
        db.SaveChanges();
    }

    /// <summary>A new school gets its roles here (saved with it).</summary>
    public static void AddMissing(School school)
    {
        foreach (var builtIn in PermissionCatalog.BuiltInRoles)
        {
            if (school.Roles.Any(r => r.BuiltIn == builtIn))
            {
                continue;
            }
            var name = PermissionCatalog.DefaultName(builtIn, school.Type);
            school.Roles.Add(new Role
            {
                Name = school.Roles.Any(r => r.Name == name) ? $"{name} (vestavěná)" : name,
                Kind = PermissionCatalog.KindOf(builtIn),
                BuiltIn = builtIn,
            });
        }
        foreach (var role in school.Roles)
        {
            var defaults = role.BuiltIn is { } builtIn
                ? PermissionCatalog.Defaults(builtIn)
                : new Dictionary<Permission, PermissionScope>();
            foreach (var info in PermissionCatalog.All)
            {
                if (role.Permissions.All(p => p.Permission != info.Permission))
                {
                    role.Permissions.Add(new RolePermission
                    {
                        Permission = info.Permission,
                        Scope = defaults.GetValueOrDefault(info.Permission),
                    });
                }
            }
        }
    }
}
