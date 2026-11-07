using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Auth;

/// <summary>"Which classes and pupils does this Teacher actually teach" —
/// originally lived only in <c>MessageBroadcastController</c> (message fan-out
/// scoping); pulled out here once the Events audience feature needed the exact
/// same resolution (třídní učitel or has a timetable entry in the class).
/// A class is the teacher's only by its whole-class lessons; a lesson of a
/// <see cref="StudentGroup"/> makes them teach just the group's pupils.</summary>
public static class TeacherClassAccessExtensions
{
    public static async Task<List<int>> GetTeacherClassIdsAsync(this AppDbContext db, int teacherId)
    {
        var ids = await db.SchoolClasses.Where(c => c.ClassTeacherId == teacherId).Select(c => c.Id)
            .Union(db.TimetableEntriesOn().Where(t => t.TeacherId == teacherId && t.StudentGroupId == null).Select(t => t.SchoolClassId))
            .ToListAsync();
        return [.. ids.Union((await db.SubstitutingTodayAsync(teacherId)).Where(l => l.Key.GroupId is null).SelectMany(l => l.ClassIds))];
    }

    /// <summary>Pupils of <see cref="GetTeacherClassIdsAsync"/> and of the
    /// groups the teacher teaches.</summary>
    public static async Task<HashSet<int>> GetTeacherStudentIdsAsync(this AppDbContext db, int teacherId)
    {
        var classIds = await db.GetTeacherClassIdsAsync(teacherId);
        var groupIds = (await db.SubstitutingTodayAsync(teacherId)).Where(l => l.Key.GroupId is not null)
            .Select(l => l.Key.GroupId!.Value).ToList();
        return (await db.Students.Where(s => classIds.Contains(s.SchoolClassId)).Select(s => s.Id)
            .Union(db.TimetableEntriesOn().Where(t => t.TeacherId == teacherId && t.StudentGroupId != null)
                .SelectMany(t => t.StudentGroup!.Members).Select(m => m.StudentId))
            .Union(db.StudentGroupMembers.Where(m => groupIds.Contains(m.StudentGroupId)).Select(m => m.StudentId))
            .ToListAsync()).ToHashSet();
    }

    /// <summary>Stricter than "my classes" — assigning homework for a subject
    /// requires an actual whole-class TimetableEntry for that exact
    /// class+subject combination; being the class's třídní učitel alone isn't
    /// enough if they don't teach this particular subject to it.</summary>
    public static async Task<bool> TeachesClassInSubjectAsync(
        this AppDbContext db, int teacherId, int schoolClassId, int subjectId) =>
        await db.TimetableEntriesOn().AnyAsync(t =>
            t.SchoolClassId == schoolClassId && t.SubjectId == subjectId && t.TeacherId == teacherId && t.StudentGroupId == null)
        || (await db.SubstitutingTodayAsync(teacherId)).Any(l =>
            l.Key.GroupId is null && l.ClassIds.Contains(schoolClassId) && l.SubjectId == subjectId);

    /// <summary>A whole-class lesson of the pupil's class or a lesson of one
    /// of the pupil's groups, in the subject.</summary>
    public static async Task<bool> TeachesStudentInSubjectAsync(
        this AppDbContext db, int teacherId, int studentId, int subjectId)
    {
        if (await db.TimetableEntriesOn().LessonsOf(db, studentId).AnyAsync(t => t.SubjectId == subjectId && t.TeacherId == teacherId))
        {
            return true;
        }
        var substituting = (await db.SubstitutingTodayAsync(teacherId)).Where(l => l.SubjectId == subjectId).ToList();
        if (substituting.Count == 0)
        {
            return false;
        }
        var classId = await db.Students.Where(s => s.Id == studentId).Select(s => s.SchoolClassId).FirstOrDefaultAsync();
        var groupIds = await db.StudentGroupMembers.Where(m => m.StudentId == studentId).Select(m => m.StudentGroupId).ToListAsync();
        return substituting.Any(l => l.Key.GroupId is { } g ? groupIds.Contains(g) : l.ClassIds.Contains(classId));
    }

    /// <summary>The lessons the teacher substitutes today (published), which
    /// make the classes and groups theirs for the day.</summary>
    public static Task<List<EffectiveLesson>> SubstitutingTodayAsync(this AppDbContext db, int teacherId) =>
        LessonChanges.SubstitutingAsync(db, teacherId, db.Today);

    /// <summary>The pupil's lessons: their class's whole-class ones and their
    /// groups'.</summary>
    public static IQueryable<TimetableEntry> LessonsOf(this IQueryable<TimetableEntry> entries, AppDbContext db, int studentId) =>
        entries.Where(t => db.Students.Any(s => s.Id == studentId && s.SchoolClassId == t.SchoolClassId)
            && (t.StudentGroupId == null || t.StudentGroup!.Members.Any(m => m.StudentId == studentId)));
}
