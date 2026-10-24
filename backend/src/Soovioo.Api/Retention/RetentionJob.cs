using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Retention;

/// <summary>Daily Hangfire job (registered in Program.cs) — switches off the
/// accounts of people who left and, in schools that erase automatically,
/// erases what is due. Demo schools are left alone: they go as a whole.</summary>
public class RetentionJob(AppDbContext db, AccountEraser accounts, StudentEraser students, TimeProvider clock)
{
    public async Task RunAsync()
    {
        var schoolIds = await db.Schools.IgnoreQueryFilters()
            .Where(s => !s.IsDemo)
            .Select(s => s.Id)
            .ToListAsync();
        foreach (var schoolId in schoolIds)
        {
            await RunForSchoolAsync(schoolId);
        }
    }

    public async Task RunForSchoolAsync(int schoolId)
    {
        var today = SchoolClock.Today(clock);
        var policy = await PolicyOfAsync(db, schoolId);
        await DeactivateAccountsAsync(schoolId, policy, today);
        if (policy.Mode != ErasureMode.Automatic)
        {
            return;
        }

        var former = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && s.LeftAt < today)
            .ToListAsync();
        foreach (var student in former)
        {
            var due = StudentEraser.Due(student, policy, today);
            if (due.Count > 0)
            {
                await students.EraseAsync(student, due);
            }
        }

        foreach (var user in await StaffDueAsync(db, schoolId, policy, today))
        {
            await accounts.EraseAsync(user);
        }
    }

    /// <summary>A former pupil's account, and the accounts of parents with
    /// no pupil left at the school, once the grace period is over.</summary>
    private async Task DeactivateAccountsAsync(int schoolId, RetentionPolicy policy, DateOnly today)
    {
        var lastDayWithAccess = today.AddDays(-policy.AccountGraceDays - 1);
        var pupilAccountIds = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && s.LeftAt <= lastDayWithAccess && s.UserId != null)
            .Select(s => s.UserId!.Value)
            .ToListAsync();
        var leftParentIds = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == schoolId && ps.Student!.LeftAt <= lastDayWithAccess)
            .Select(ps => ps.ParentProfile!.Person!.UserId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync();
        // Someone who still has a pupil here (or one in the grace period)
        // keeps the account.
        var stillParentIds = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == schoolId && (ps.Student!.LeftAt == null || ps.Student.LeftAt > lastDayWithAccess))
            .Select(ps => ps.ParentProfile!.Person!.UserId)
            .ToListAsync();

        var candidates = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == schoolId && u.DeactivatedAt == null
                && (pupilAccountIds.Contains(u.Id) || (leftParentIds.Contains(u.Id) && !stillParentIds.Contains(u.Id))))
            .ToListAsync();
        foreach (var user in candidates)
        {
            var isPupil = pupilAccountIds.Contains(user.Id);
            // Staff keep their account for that.
            if (isPupil ? user.Roles.All(r => r == UserRole.Student) : user.Roles.All(r => r == UserRole.Parent))
            {
                await accounts.DeactivateAsync(user);
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task<RetentionPolicy> PolicyOfAsync(AppDbContext db, int schoolId) =>
        await db.RetentionPolicies.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.SchoolId == schoolId)
        ?? new RetentionPolicy { SchoolId = schoolId };

    /// <summary>Deactivated staff accounts whose time is up.</summary>
    public static async Task<List<User>> StaffDueAsync(AppDbContext db, int schoolId, RetentionPolicy policy, DateOnly today)
    {
        var deactivated = await DeactivatedStaff(db, schoolId).ToListAsync();
        return deactivated
            .Where(u => policy.StaffDueOn(DateOnly.FromDateTime(u.DeactivatedAt!.Value.UtcDateTime)) <= today)
            .ToList();
    }

    public static IQueryable<User> DeactivatedStaff(AppDbContext db, int schoolId) =>
        db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == schoolId && u.DeactivatedAt != null && u.ErasedAt == null
                && (u.Roles.Contains(UserRole.Teacher) || u.Roles.Contains(UserRole.Admin)));
}
