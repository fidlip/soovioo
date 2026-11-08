using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Grades;
using Soovioo.Api.Messaging;
using Soovioo.Api.Timetable;
using Soovioo.Api.People;

namespace Soovioo.Api.Dashboard;

/// <summary>One aggregated summary for the Home page, instead of the
/// frontend calling five existing agenda endpoints separately — reuses their
/// DTOs (<see cref="GradeDto"/>/<see cref="AbsenceDto"/>/<see cref="TimetableEntryDto"/>)
/// and the same access-control helpers every agenda already goes through
/// (<see cref="StudentAccessExtensions.GetAccessibleStudentIdsAsync"/>,
/// <see cref="EventVisibilityExtensions"/>), so a widget here can never show
/// more than the equivalent full agenda page would.</summary>
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard()
    {
        if (currentSchool.SchoolId is null)
        {
            // SuperAdmin isn't tied to a school and has no agenda pages to
            // summarize — same "nothing to show" stance as EventsController.
            return Ok(new DashboardDto([], null, [], [], 0, [], [], [], null, []));
        }

        var schoolId = currentSchool.SchoolId.Value;
        var userId = CurrentUserId();
        var isStaff = User.IsStaff();
        var isAdmin = await User.HasPermissionAsync(db, Permission.Administration);
        var isTeacher = isStaff && !isAdmin;
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);
        var today = db.Today.DayOfWeek;

        var todayLessons = new List<TimetableEntryDto>();
        string? todayLessonsContext = null;
        if (isTeacher)
        {
            todayLessons = await WithChangesAsync(await TodayLessonsForTeacherAsync(userId, today), new LessonScope(TeacherId: userId));
        }
        else if (!isAdmin)
        {
            (todayLessons, todayLessonsContext) = await TodayLessonsForOwnClassAsync(allowedStudentIds, today);
        }

        var recentGrades = new List<GradeDto>();
        if (!isStaff)
        {
            var gradeStudentIds = await User.StudentIdsAsync(db, Permission.GradesRead) ?? [];
            recentGrades = await db.Grades
                .Where(g => gradeStudentIds.Contains(g.StudentId))
                .OrderByDescending(g => g.GradedOn)
                .ThenByDescending(g => g.CreatedAt)
                .Take(5)
                .Select(g => new GradeDto(
                    g.Id, g.StudentId, g.Student!.Person!.FirstName + " " + g.Student.Person!.LastName,
                    g.SubjectId, g.Subject!.Name, g.Value, g.Weight, g.Note, g.GradedOn, g.CreatedAt))
                .ToListAsync();
        }

        var absencesQuery = db.Absences.AsQueryable();
        if (await User.StudentIdsAsync(db, Permission.AbsencesRead) is { } absenceStudentIds)
        {
            absencesQuery = absencesQuery.Where(a => absenceStudentIds.Contains(a.StudentId));
        }
        // Staff: what's awaiting their review. Parent/Student: what's
        // still unresolved for them to act on or wait out — the same two
        // meanings "ke schválení"/"nevyřízené" carry on the Absences page
        // itself (see STATUS_LABEL in absences-page.ts).
        absencesQuery = isStaff
            ? absencesQuery.Where(a => a.Status == AbsenceStatus.PendingExcuse)
            : absencesQuery.Where(a => a.Status == AbsenceStatus.Unexcused || a.Status == AbsenceStatus.PendingExcuse);
        var pendingAbsences = await absencesQuery
            .OrderByDescending(a => a.Date)
            .Take(5)
            .Select(AbsenceDto.Projection)
            .ToListAsync();
        var excusesQuery = db.Excuses.Where(e => e.Absences.Any(a => a.Status == AbsenceStatus.PendingExcuse));
        if (await User.StudentIdsAsync(db, Permission.AbsencesRead) is { } excuseStudentIds)
        {
            excusesQuery = excusesQuery.Where(e => excuseStudentIds.Contains(e.StudentId));
        }
        var pendingExcuses = await ExcusesController.DtosAsync(excusesQuery.OrderByDescending(e => e.DateTo).Take(5));

        var unreadMessageCount = await db.CountUnreadConversationsAsync(userId);
        var unreadConversations = (await db.UnreadConversations(userId)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(5)
            .Select(c => new
            {
                c.Id,
                c.Subject,
                c.StudentId,
                StudentName = c.Student == null ? null : c.Student.Person!.FirstName + " " + c.Student.Person!.LastName,
                Participants = c.Participants.Select(p => PersonNames.Display(p.User!.Person, p.User.Username)).ToList(),
                c.LastMessageAt,
                LastMessage = c.Messages.OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.SenderName, m.Body }).FirstOrDefault(),
            })
            .ToListAsync())
            .Select(c => new ConversationSummaryDto(
                c.Id, c.Subject, c.StudentId, c.StudentName, c.Participants, c.LastMessageAt,
                c.LastMessage?.SenderName, ConversationsController.Preview(c.LastMessage?.Body), Unread: true))
            .ToList();

        var behaviorRecordsQuery = db.BehaviorRecords.AsQueryable();
        if (await User.StudentIdsAsync(db, Permission.BehaviorRead) is { } behaviorStudentIds)
        {
            behaviorRecordsQuery = behaviorRecordsQuery.Where(r => behaviorStudentIds.Contains(r.StudentId));
        }
        var unreadBehaviorRecords = await behaviorRecordsQuery
            .Where(r => r.ReadAt == null)
            .OrderByDescending(r => r.Date)
            .Take(5)
            .Select(r => new BehaviorRecordDto(
                r.Id, r.StudentId, r.Student!.Person!.FirstName + " " + r.Student.Person!.LastName,
                r.Type, r.Text, r.Date, r.RecordedByName, r.CreatedAt, r.ReadAt))
            .ToListAsync();

        var eventsQuery = await User.ScopeAsync(db, Permission.EventsWrite) == PermissionScope.School
            ? db.VisibleToAdmin(schoolId)
            : User.ActiveKind() switch
            {
                RoleKind.Staff => await db.VisibleToTeacherAsync(schoolId, userId),
                RoleKind.Student => await db.VisibleToStudentAsync(schoolId, userId),
                _ => await db.VisibleToParentAsync(schoolId, userId),
            };
        var now = DateTimeOffset.UtcNow;
        var upcomingEvents = await eventsQuery
            .Where(e => e.EndAt >= now)
            .OrderBy(e => e.StartAt)
            .Take(5)
            .Select(e => new UpcomingEventDto(e.Id, e.Title, e.StartAt, e.EndAt, e.AllDay))
            .ToListAsync();

        SchoolCountsDto? schoolCounts = null;
        if (isAdmin)
        {
            schoolCounts = new SchoolCountsDto(
                await db.Students.CountAsync(),
                await db.Users.CountAsync(u => u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)),
                await db.SchoolClasses.CountAsync());
        }

        return Ok(new DashboardDto(
            todayLessons, todayLessonsContext, recentGrades, pendingAbsences,
            unreadMessageCount, unreadConversations, upcomingEvents, unreadBehaviorRecords, schoolCounts, pendingExcuses));
    }

    /// <summary>Filtered by day of week only, not <see cref="WeekParity"/> —
    /// the rest of the app doesn't resolve "is this calendar week odd or
    /// even" anywhere yet either (timetable-page.ts just lists every entry
    /// regardless of parity); that belongs to the Rozvrh redesign (see
    /// ROADMAP), not this summary widget.</summary>
    private async Task<List<TimetableEntryDto>> TodayLessonsForTeacherAsync(int teacherId, DayOfWeek today) =>
        // A group's lesson once, not once per class of the group.
        [.. (await db.TimetableEntriesOn()
            .Where(e => e.TeacherId == teacherId && e.DayOfWeek == today)
            .OrderBy(e => e.PeriodNumber).ThenBy(e => e.Id)
            .Select(e => new TimetableEntryDto(
                e.Id, e.SchoolClassId, e.SubjectId, e.Subject!.Name, e.Subject!.Abbreviation,
                e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId, e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name, Array.Empty<string>(), false,
                e.StudentGroupId, e.StudentGroup == null ? null : e.StudentGroup.Name))
            .ToListAsync())
            .DistinctBy(e => e.StudentGroupId is { } g ? (g, e.PeriodNumber, e.WeekParity, 0) : (0, e.PeriodNumber, e.WeekParity, e.Id))];

    /// <summary>Parent/Student: the first accessible student's own class
    /// schedule for today. "First" (lowest id) rather than a picker — a real
    /// per-child switcher belongs to the Rozvrh redesign (see ROADMAP), not
    /// this summary widget. Empty/null when the account isn't linked to any
    /// student yet (e.g. a freshly created Student-role account).</summary>
    private async Task<(List<TimetableEntryDto> Lessons, string? Context)> TodayLessonsForOwnClassAsync(
        HashSet<int>? allowedStudentIds, DayOfWeek today)
    {
        if (allowedStudentIds is not { Count: > 0 })
        {
            return ([], null);
        }

        var student = await db.Students
            .Where(s => allowedStudentIds.Contains(s.Id))
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, SchoolClassName = s.SchoolClass!.Name })
            .FirstOrDefaultAsync();
        if (student is null)
        {
            return ([], null);
        }

        var lessons = await db.TimetableEntriesOn()
            .Where(e => e.SchoolClassId == student.SchoolClassId && e.DayOfWeek == today)
            .Where(e => e.StudentGroupId == null || e.StudentGroup!.Members.Any(m => m.StudentId == student.Id))
            .OrderBy(e => e.PeriodNumber)
            .Select(e => new TimetableEntryDto(
                e.Id, e.SchoolClassId, e.SubjectId, e.Subject!.Name, e.Subject!.Abbreviation,
                e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId, e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name, Array.Empty<string>(), false,
                e.StudentGroupId, e.StudentGroup == null ? null : e.StudentGroup.Name))
            .ToListAsync();

        var groups = await db.StudentGroupMembers.Where(m => m.StudentId == student.Id).Select(m => m.StudentGroupId).ToListAsync();
        lessons = await WithChangesAsync(lessons, new LessonScope(ClassId: student.SchoolClassId, GroupIds: groups.ToHashSet()));
        return (lessons, $"{student.FirstName} {student.LastName} ({student.SchoolClassName})");
    }

    /// <summary>Today's published changes of the lessons.</summary>
    private async Task<List<TimetableEntryDto>> WithChangesAsync(List<TimetableEntryDto> lessons, LessonScope scope)
    {
        var effective = await LessonChanges.OnAsync(db, db.Today, db.Today);
        var views = LessonChangeViews.For(effective, scope, await LessonNames.LoadAsync(db, effective));
        if (views.Count == 0)
        {
            return lessons;
        }
        static bool Same(TimetableEntryDto e, LessonChangeDto v) => e.PeriodNumber == v.PeriodNumber
            && (v.StudentGroupId is { } g ? e.StudentGroupId == g : e.StudentGroupId == null && e.SchoolClassId == v.SchoolClassId);
        var id = 0;
        return [.. lessons.Where(e => !views.Any(v => v.Replaces && Same(e, v)))
            .Concat(views.Select(v => new TimetableEntryDto(
                --id, v.SchoolClassId ?? 0, v.SubjectId, v.SubjectName, v.SubjectAbbreviation, v.DayOfWeek, v.PeriodNumber,
                WeekParity.Every, v.TeacherId, v.TeacherName, v.ClassroomId, v.ClassroomName, [], false,
                v.StudentGroupId, v.StudentGroupName, v.Label.Length > 0 ? v.Label : null, v.Gone,
                v.WasSubject, v.WasTeacher, v.WasClassroom)))
            .OrderBy(e => e.PeriodNumber)];
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record DashboardDto(
    IReadOnlyList<TimetableEntryDto> TodayLessons,
    /// <summary>Which student <see cref="TodayLessons"/> is for — only set
    /// for Parent/Student (Teacher's own schedule needs no label, Admin gets
    /// no lessons at all). Null while unset (e.g. Parent with no linked
    /// child yet, or an unlinked Student account).</summary>
    string? TodayLessonsContext,
    IReadOnlyList<GradeDto> RecentGrades,
    IReadOnlyList<AbsenceDto> PendingAbsences,
    int UnreadMessageCount,
    /// <summary>Up to 5 most recent of the <see cref="UnreadMessageCount"/>
    /// conversations.</summary>
    IReadOnlyList<ConversationSummaryDto> UnreadConversations,
    IReadOnlyList<UpcomingEventDto> UpcomingEvents,
    /// <summary>Up to 5 most recent, same shared-read-state as
    /// <see cref="UnreadMessageCount"/> — see <see cref="BehaviorRecord.ReadAt"/>.</summary>
    IReadOnlyList<BehaviorRecordDto> UnreadBehaviorRecords,
    /// <summary>Admin only — null for every other role.</summary>
    SchoolCountsDto? SchoolCounts,
    /// <summary>Up to 5 omluvenky with days still waiting for approval.</summary>
    IReadOnlyList<ExcuseDto> PendingExcuses);

public record UpcomingEventDto(int Id, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, bool AllDay);

public record SchoolCountsDto(int StudentCount, int TeacherCount, int ClassCount);
