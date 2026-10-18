using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Homework;

/// <summary>"Can this caller see/act on this one Homework row" — Teacher/
/// Admin always can (GetAccessibleStudentIdsAsync returns null for them,
/// same convention as everywhere else); a Parent/Student can only if at
/// least one of their accessible students is actually targeted by it
/// (whole class, or specifically listed — see Homework.Students' doc
/// comment).</summary>
public static class HomeworkAccessExtensions
{
    public static async Task<bool> CanAccessHomeworkAsync(this ClaimsPrincipal user, AppDbContext db, int homeworkId)
    {
        var allowed = await user.GetAccessibleStudentIdsAsync(db);
        if (allowed is null)
        {
            return true;
        }

        var homework = await db.Homework
            .Where(h => h.Id == homeworkId)
            .Select(h => new { h.SchoolClassId, StudentIds = h.Students.Select(s => s.StudentId).ToList() })
            .FirstOrDefaultAsync();
        if (homework is null)
        {
            return false;
        }

        if (homework.StudentIds.Count > 0)
        {
            return homework.StudentIds.Any(allowed.Contains);
        }

        return await db.Students.AnyAsync(s => s.SchoolClassId == homework.SchoolClassId && allowed.Contains(s.Id));
    }
}
