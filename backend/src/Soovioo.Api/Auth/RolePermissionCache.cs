using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>What <see cref="PermissionAccess"/> read for an account in a
/// role, kept for a minute — most requests then ask the database nothing.
/// Changing a role or someone's roles clears it, so a change still applies
/// to the very next request (one API instance).</summary>
public class RolePermissionCache(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(int UserId, int RoleId, bool Support),
        (DateTimeOffset At, IReadOnlyDictionary<Permission, PermissionScope> Permissions)> entries = new();

    public bool TryGet((int UserId, int RoleId, bool Support) key, out IReadOnlyDictionary<Permission, PermissionScope> permissions)
    {
        if (entries.TryGetValue(key, out var entry) && clock.GetUtcNow() - entry.At < Lifetime)
        {
            permissions = entry.Permissions;
            return true;
        }
        permissions = null!;
        return false;
    }

    public void Set((int UserId, int RoleId, bool Support) key, IReadOnlyDictionary<Permission, PermissionScope> permissions) =>
        entries[key] = (clock.GetUtcNow(), permissions);

    public void Clear() => entries.Clear();

    /// <summary>The app's cache for a context made by DI; null in tests,
    /// which then always read the database.</summary>
    public static RolePermissionCache? Of(AppDbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<RolePermissionCache>();
}
