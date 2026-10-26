using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

public static class StudentAccessExtensions
{
    /// <summary>
    /// Null means "no restriction" (Teacher/Admin see everything). A non-null
    /// set means the caller (a Parent, or a Student seeing only themselves)
    /// may only see/act on those student ids.
    /// </summary>
    public static async Task<HashSet<int>?> GetAccessibleStudentIdsAsync(
        this ClaimsPrincipal user, AppDbContext db)
    {
        if (user.IsInRole(nameof(UserRole.Teacher)) || user.IsInRole(nameof(UserRole.Admin)))
        {
            return null;
        }

        var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (user.IsInRole(nameof(UserRole.Student)))
        {
            // The pupil's own Person carries the account (Person.UserId), not
            // a join table like ParentStudent — a student only ever has at
            // most one own account. Empty set, not null, when unlinked/not-yet-found —
            // "no restriction" must never be the fallback for a bad lookup.
            var ownStudentId = await db.Students
                .Where(s => s.Person!.UserId == userId)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
            return ownStudentId is { } id ? [id] : [];
        }

        var studentIds = await db.ChildIdsOf(userId).ToListAsync();

        return studentIds.ToHashSet();
    }

    public static async Task<bool> CanAccessStudentAsync(
        this ClaimsPrincipal user, AppDbContext db, int studentId)
    {
        var allowed = await user.GetAccessibleStudentIdsAsync(db);
        return allowed is null || allowed.Contains(studentId);
    }
}
