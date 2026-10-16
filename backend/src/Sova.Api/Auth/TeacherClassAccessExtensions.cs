using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Auth;

/// <summary>"Which classes does this Teacher actually teach" — originally
/// lived only in <c>MessageBroadcastController</c> (message fan-out scoping);
/// pulled out here once the Events audience feature needed the exact same
/// resolution (třídní učitel or has a timetable entry in the class).</summary>
public static class TeacherClassAccessExtensions
{
    public static async Task<bool> TeacherOwnsClassAsync(this AppDbContext db, int teacherId, int schoolClassId) =>
        await db.SchoolClasses.AnyAsync(c => c.Id == schoolClassId && c.ClassTeacherId == teacherId)
            || await db.TimetableEntries.AnyAsync(t => t.SchoolClassId == schoolClassId && t.TeacherId == teacherId);

    public static async Task<List<int>> GetTeacherClassIdsAsync(this AppDbContext db, int teacherId) =>
        await db.SchoolClasses.Where(c => c.ClassTeacherId == teacherId).Select(c => c.Id)
            .Union(db.TimetableEntries.Where(t => t.TeacherId == teacherId).Select(t => t.SchoolClassId))
            .ToListAsync();
}
