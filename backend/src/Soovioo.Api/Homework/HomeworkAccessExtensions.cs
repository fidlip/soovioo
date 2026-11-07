using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Homework;

/// <summary>"Can this caller see/act on this one Homework row" — staff
/// when its class (or a class or pupil of its group) is within their
/// <see cref="Permission.HomeworkRead"/>; a Parent/Student only if at
/// least one of their accessible students is actually targeted by it
/// (whole class / group, or specifically listed — see Homework.Students'
/// doc comment).</summary>
public static class HomeworkAccessExtensions
{
    public static async Task<bool> CanAccessHomeworkAsync(this ClaimsPrincipal user, AppDbContext db, int homeworkId) =>
        await (await user.VisibleHomeworkAsync(db)).AnyAsync(h => h.Id == homeworkId);

    /// <summary>The homework the caller sees.</summary>
    public static async Task<IQueryable<Homework>> VisibleHomeworkAsync(this ClaimsPrincipal user, AppDbContext db)
    {
        var query = db.Homework.AsQueryable();
        if (user.IsStaff())
        {
            if (await user.ClassIdsAsync(db, Permission.HomeworkRead) is not { } classIds)
            {
                return query;
            }
            // Staff see their classes' and groups' homework, pupils or not.
            var studentIds = await user.StudentIdsAsync(db, Permission.HomeworkRead) ?? [];
            return query.Where(h =>
                (h.SchoolClassId != null && classIds.Contains(h.SchoolClassId.Value))
                || (h.StudentGroupId != null && (h.StudentGroup!.Classes.Any(c => classIds.Contains(c.SchoolClassId))
                    || h.StudentGroup.Members.Any(m => studentIds.Contains(m.StudentId)))));
        }
        if (await user.StudentIdsAsync(db, Permission.HomeworkRead) is not { } allowed)
        {
            return query;
        }
        return query.Where(h =>
            h.Students.Count == 0
                ? (h.SchoolClassId != null && db.Students.Any(s => s.SchoolClassId == h.SchoolClassId && allowed.Contains(s.Id)))
                  || (h.StudentGroupId != null && h.StudentGroup!.Members.Any(m => allowed.Contains(m.StudentId)))
                : h.Students.Any(hs => allowed.Contains(hs.StudentId)));
    }

    /// <summary>Who the homework is for: the listed pupils, otherwise the
    /// group's (its members now) or the class's.</summary>
    public static async Task<List<int>> TargetStudentIdsAsync(
        this AppDbContext db, int? schoolClassId, int? studentGroupId, IReadOnlyCollection<int> listed) =>
        listed.Count > 0
            ? [.. listed]
            : studentGroupId is { } groupId
                ? await db.StudentGroupMembers.Where(m => m.StudentGroupId == groupId).Select(m => m.StudentId).ToListAsync()
                : await db.Students.Where(s => s.SchoolClassId == schoolClassId).Select(s => s.Id).ToListAsync();

    /// <summary>Like <see cref="PermissionAccess.ReachesClassSubjectAsync"/>
    /// for a group: "my classes" means teaching the group — or, for a group
    /// of an earlier timetable version, a group of the subject with some of
    /// its pupils; the class teacher's when all its classes are theirs.</summary>
    public static async Task<bool> ReachesGroupAsync(
        this ClaimsPrincipal user, AppDbContext db, Permission permission, StudentGroup group) =>
        await user.ScopeAsync(db, permission) switch
        {
            PermissionScope.School => true,
            PermissionScope.MyClasses => await db.TimetableEntriesOn().AnyAsync(t =>
                    t.TeacherId == user.UserId() && t.StudentGroupId != null
                    && (t.StudentGroupId == group.Id
                        || (t.SubjectId == group.SubjectId && t.StudentGroup!.Members.Any(m =>
                            db.StudentGroupMembers.Any(o => o.StudentGroupId == group.Id && o.StudentId == m.StudentId)))))
                || (await db.SubstitutingTodayAsync(user.UserId())).Any(l => l.Key.GroupId == group.Id),
            PermissionScope.ClassTeacher => await db.StudentGroupClasses.Where(c => c.StudentGroupId == group.Id)
                .AllAsync(c => c.SchoolClass!.ClassTeacherId == user.UserId()),
            _ => false,
        };

    /// <summary>The class and subject or the group within the caller's
    /// <see cref="Permission.HomeworkWrite"/>.</summary>
    public static async Task<bool> MayWriteHomeworkAsync(
        this ClaimsPrincipal user, AppDbContext db, int? schoolClassId, int? studentGroupId, int subjectId) =>
        studentGroupId is { } groupId
            ? await db.StudentGroups.FirstOrDefaultAsync(g => g.Id == groupId) is { } group
              && await user.ReachesGroupAsync(db, Permission.HomeworkWrite, group)
            : schoolClassId is { } classId && await user.ReachesClassSubjectAsync(db, Permission.HomeworkWrite, classId, subjectId);
}
