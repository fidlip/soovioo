using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Events;

/// <summary>Resolves which events a given user may see — shared by
/// <see cref="EventsController"/> (authenticated, ambient tenant filter
/// already scopes `db.Events` to the caller's school) and
/// <see cref="CalendarFeedController"/> (unauthenticated ICS request, no
/// tenant context at all). To work identically in both, every query here
/// uses <c>IgnoreQueryFilters()</c> plus an explicit <c>SchoolId</c> check
/// rather than relying on the ambient filter.</summary>
public static class EventVisibilityExtensions
{
    /// <summary>Admin sees every event in the school — a deliberate
    /// oversight exception, not a bug: Admin needs the full picture for
    /// school management regardless of who an event was addressed to.</summary>
    public static IQueryable<Event> VisibleToAdmin(this AppDbContext db, int schoolId) =>
        db.Events.IgnoreQueryFilters().Where(e => e.SchoolId == schoolId);

    /// <summary>Teacher sees: their own creations, Everyone-audience events,
    /// and Selected-audience events touching anything "theirs" — targeted at
    /// them directly, at a class they teach, at one of their students (also of
    /// their groups), or at a parent of one of their students. Also a deliberate oversight
    /// exception (not just plain recipient matching), scoped to the
    /// teacher's own classes rather than the whole school like Admin's.</summary>
    public static async Task<IQueryable<Event>> VisibleToTeacherAsync(this AppDbContext db, int schoolId, int teacherUserId)
    {
        var classIds = await db.GetTeacherClassIdsAsync(teacherUserId);
        var studentIds = await db.GetTeacherStudentIdsAsync(teacherUserId);
        var parentUserIds = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == schoolId && studentIds.Contains(ps.StudentId))
            .ParentAccountLinks()
            .Select(l => l.UserId)
            .ToListAsync();

        return db.Events.IgnoreQueryFilters()
            .Where(e => e.SchoolId == schoolId && (
                e.CreatedByUserId == teacherUserId ||
                e.Audience == EventAudience.Everyone ||
                (e.Audience == EventAudience.Selected && (
                    e.Teachers.Any(t => t.UserId == teacherUserId) ||
                    e.SchoolClasses.Any(c => classIds.Contains(c.SchoolClassId)) ||
                    e.Students.Any(s => studentIds.Contains(s.StudentId)) ||
                    e.Parents.Any(p => parentUserIds.Contains(p.UserId))))));
    }

    /// <summary>Parent sees: their own creations (a class's parent
    /// representative may make events), Everyone-audience events, and Selected-audience
    /// events targeted at them directly, at a class one of their children is
    /// in, or at one of their children directly. No oversight exception —
    /// "Pouze já" from a Teacher is genuinely invisible to every Parent.</summary>
    public static async Task<IQueryable<Event>> VisibleToParentAsync(this AppDbContext db, int schoolId, int parentUserId)
    {
        var myStudentIds = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == schoolId && ps.ParentProfile!.Person!.UserId == parentUserId)
            .Select(ps => ps.StudentId)
            .ToListAsync();
        var myClassIds = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && myStudentIds.Contains(s.Id))
            .Select(s => s.SchoolClassId)
            .ToListAsync();

        return db.Events.IgnoreQueryFilters()
            .Where(e => e.SchoolId == schoolId && (
                e.CreatedByUserId == parentUserId ||
                e.Audience == EventAudience.Everyone ||
                (e.Audience == EventAudience.Selected && (
                    e.Parents.Any(p => p.UserId == parentUserId) ||
                    e.SchoolClasses.Any(c => myClassIds.Contains(c.SchoolClassId)) ||
                    e.Students.Any(s => myStudentIds.Contains(s.StudentId))))));
    }

    /// <summary>Whom the event is addressed to (its notifications): Everyone =
    /// every account of the school; Selected = the teachers and parents
    /// picked, the picked classes' and pupils' own accounts and their
    /// parents. Not the oversight exceptions above — those see it, but it
    /// isn't for them.</summary>
    public static async Task<List<int>> AudienceUserIdsAsync(this AppDbContext db, Event e)
    {
        switch (e.Audience)
        {
            case EventAudience.Everyone:
                return await db.Users.IgnoreQueryFilters()
                    .Where(u => u.SchoolId == e.SchoolId && u.DeactivatedAt == null)
                    .Select(u => u.Id)
                    .ToListAsync();
            case EventAudience.Selected:
                var classIds = e.SchoolClasses.Select(c => c.SchoolClassId).ToList();
                var pickedIds = e.Students.Select(s => s.StudentId).ToList();
                var students = await db.Students.IgnoreQueryFilters()
                    .Where(s => s.SchoolId == e.SchoolId && (pickedIds.Contains(s.Id) || classIds.Contains(s.SchoolClassId)))
                    .Select(s => new { s.Id, s.Person!.UserId })
                    .ToListAsync();
                var studentIds = students.Select(s => s.Id).ToList();
                var parents = await db.ParentStudents
                    .Where(ps => ps.SchoolId == e.SchoolId && studentIds.Contains(ps.StudentId))
                    .ParentAccountLinks()
                    .Select(l => l.UserId)
                    .ToListAsync();
                return e.Teachers.Select(t => t.UserId)
                    .Concat(e.Parents.Select(p => p.UserId))
                    .Concat(students.Where(s => s.UserId != null).Select(s => s.UserId!.Value))
                    .Concat(parents)
                    .Distinct()
                    .ToList();
            default:
                return [];
        }
    }

    /// <summary>Student sees: Everyone-audience events, and Selected-audience
    /// events targeted at their own class or at themselves directly — same
    /// shape as Parent, just resolved via the pupil's own Person
    /// (<see cref="Person.UserId"/>) instead of the <see cref="ParentStudent"/> join table.
    /// No oversight exception, same as Parent. Empty query, not an error,
    /// for an account not yet linked to any Student row.</summary>
    public static async Task<IQueryable<Event>> VisibleToStudentAsync(this AppDbContext db, int schoolId, int studentUserId)
    {
        var myStudent = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && s.Person!.UserId == studentUserId)
            .Select(s => new { s.Id, s.SchoolClassId })
            .FirstOrDefaultAsync();
        // Nullable locals, not an early return on "not linked yet" — Everyone
        // audience must still show (same as Parent with no linked children),
        // only the Selected-audience class/student matching has nothing to
        // match against.
        int? myStudentId = myStudent?.Id;
        int? myClassId = myStudent?.SchoolClassId;

        return db.Events.IgnoreQueryFilters()
            .Where(e => e.SchoolId == schoolId && (
                e.Audience == EventAudience.Everyone ||
                (e.Audience == EventAudience.Selected && (
                    (myClassId != null && e.SchoolClasses.Any(c => c.SchoolClassId == myClassId)) ||
                    (myStudentId != null && e.Students.Any(s => s.StudentId == myStudentId))))));
    }
}
