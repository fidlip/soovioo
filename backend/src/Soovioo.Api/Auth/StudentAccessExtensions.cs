using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Whose pupils the caller deals with by the kind of their active
/// role — for what no permission governs (the pupil lists, the directory,
/// conversations, photos). Agendas with a permission go through
/// <see cref="PermissionAccess.StudentIdsAsync"/>.</summary>
public static class StudentAccessExtensions
{
    /// <summary>
    /// Null means "no restriction" (staff see everyone). A non-null set means
    /// the caller (a parent, or a pupil seeing only themselves) may only
    /// see/act on those student ids.
    /// </summary>
    public static async Task<HashSet<int>?> GetAccessibleStudentIdsAsync(
        this ClaimsPrincipal user, AppDbContext db)
    {
        var kind = user.ActiveKind();
        if (kind == RoleKind.Staff)
        {
            return null;
        }
        return kind is null ? [] : await user.OwnStudentIdsAsync(db);
    }

    /// <summary>A parent's children, a pupil themselves — empty for staff.</summary>
    public static async Task<HashSet<int>> OwnStudentIdsAsync(this ClaimsPrincipal user, AppDbContext db)
    {
        var userId = user.UserId();
        switch (user.ActiveKind())
        {
            case RoleKind.Student:
                // The pupil's own Person carries the account (Person.UserId), not
                // a join table like ParentStudent — a student only ever has at
                // most one own account. Empty set, not null, when unlinked/not-yet-found —
                // "no restriction" must never be the fallback for a bad lookup.
                var ownStudentId = await db.Students
                    .Where(s => s.Person!.UserId == userId)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync();
                return ownStudentId is { } id ? [id] : [];
            case RoleKind.Parent:
                return (await db.ChildIdsOf(userId).ToListAsync()).ToHashSet();
            default:
                return [];
        }
    }

    public static async Task<bool> CanAccessStudentAsync(
        this ClaimsPrincipal user, AppDbContext db, int studentId)
    {
        var allowed = await user.GetAccessibleStudentIdsAsync(db);
        return allowed is null || allowed.Contains(studentId);
    }
}
