using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Auth;

public static class StudentAccessExtensions
{
    /// <summary>
    /// Null means "no restriction" (Teacher/Admin see everything). A non-null
    /// set means the caller (a Parent) may only see/act on those student ids.
    /// </summary>
    public static async Task<HashSet<int>?> GetAccessibleStudentIdsAsync(
        this ClaimsPrincipal user, AppDbContext db)
    {
        if (user.IsInRole(nameof(UserRole.Teacher)) || user.IsInRole(nameof(UserRole.Admin)))
        {
            return null;
        }

        var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var studentIds = await db.ParentStudents
            .Where(ps => ps.UserId == userId)
            .Select(ps => ps.StudentId)
            .ToListAsync();

        return studentIds.ToHashSet();
    }

    public static async Task<bool> CanAccessStudentAsync(
        this ClaimsPrincipal user, AppDbContext db, int studentId)
    {
        var allowed = await user.GetAccessibleStudentIdsAsync(db);
        return allowed is null || allowed.Contains(studentId);
    }
}
