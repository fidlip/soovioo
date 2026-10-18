using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

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

    /// <summary>Stricter than TeacherOwnsClassAsync above — that one is
    /// subject-agnostic (any timetable entry, or being the class teacher,
    /// is enough to broadcast a message). Assigning homework for a subject
    /// requires an actual TimetableEntry for that exact class+subject
    /// combination; being the class's třídní učitel alone isn't enough if
    /// they don't teach this particular subject to it.</summary>
    public static async Task<bool> TeachesClassInSubjectAsync(
        this AppDbContext db, int teacherId, int schoolClassId, int subjectId) =>
        await db.TimetableEntries.AnyAsync(t =>
            t.SchoolClassId == schoolClassId && t.SubjectId == subjectId && t.TeacherId == teacherId);
}
