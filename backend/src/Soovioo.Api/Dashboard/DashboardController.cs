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
            return Ok(new DashboardDto([], null, [], [], 0, [], [], [], null));
        }

        var schoolId = currentSchool.SchoolId.Value;
        var userId = CurrentUserId();
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var isTeacher = User.IsInRole(nameof(UserRole.Teacher));
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Today).DayOfWeek;

        var todayLessons = new List<TimetableEntryDto>();
        string? todayLessonsContext = null;
        if (isTeacher)
        {
            todayLessons = await TodayLessonsForTeacherAsync(userId, today);
        }
        else if (!isAdmin)
        {
            (todayLessons, todayLessonsContext) = await TodayLessonsForOwnClassAsync(allowedStudentIds, today);
        }

        var recentGrades = new List<GradeDto>();
        if (!isAdmin && !isTeacher)
        {
            recentGrades = await db.Grades
                .Where(g => allowedStudentIds!.Contains(g.StudentId))
                .OrderByDescending(g => g.GradedOn)
                .ThenByDescending(g => g.CreatedAt)
                .Take(5)
                .Select(g => new GradeDto(
                    g.Id, g.StudentId, g.Student!.FirstName + " " + g.Student.LastName,
                    g.SubjectId, g.Subject!.Name, g.Value, g.Weight, g.Note, g.GradedOn, g.CreatedAt))
                .ToListAsync();
        }

        var absencesQuery = db.Absences.AsQueryable();
        if (allowedStudentIds is not null)
        {
            absencesQuery = absencesQuery.Where(a => allowedStudentIds.Contains(a.StudentId));
        }
        // Teacher/Admin: what's awaiting their review. Parent/Student: what's
        // still unresolved for them to act on or wait out — the same two
        // meanings "ke schválení"/"nevyřízené" carry on the Absences page
        // itself (see STATUS_LABEL in absences-page.ts).
        absencesQuery = isAdmin || isTeacher
            ? absencesQuery.Where(a => a.Status == AbsenceStatus.PendingExcuse)
            : absencesQuery.Where(a => a.Status == AbsenceStatus.Unexcused || a.Status == AbsenceStatus.PendingExcuse);
        var pendingAbsences = await absencesQuery
            .OrderByDescending(a => a.Date)
            .Take(5)
            .Select(a => new AbsenceDto(
                a.Id, a.StudentId, a.Student!.FirstName + " " + a.Student.LastName, a.Date, a.Status, a.ExcuseReason, a.Periods))
            .ToListAsync();

        var unreadMessageCount = await db.CountUnreadConversationsAsync(userId);
        var unreadConversations = (await db.UnreadConversations(userId)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(5)
            .Select(c => new
            {
                c.Id,
                c.Subject,
                c.StudentId,
                StudentName = c.Student == null ? null : c.Student.FirstName + " " + c.Student.LastName,
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
        if (allowedStudentIds is not null)
        {
            behaviorRecordsQuery = behaviorRecordsQuery.Where(r => allowedStudentIds.Contains(r.StudentId));
        }
        var unreadBehaviorRecords = await behaviorRecordsQuery
            .Where(r => r.ReadAt == null)
            .OrderByDescending(r => r.Date)
            .Take(5)
            .Select(r => new BehaviorRecordDto(
                r.Id, r.StudentId, r.Student!.FirstName + " " + r.Student.LastName,
                r.Type, r.Text, r.Date, r.RecordedByName, r.CreatedAt, r.ReadAt))
            .ToListAsync();

        var eventsQuery = isAdmin
            ? db.VisibleToAdmin(schoolId)
            : isTeacher
                ? await db.VisibleToTeacherAsync(schoolId, userId)
                : User.IsInRole(nameof(UserRole.Student))
                    ? await db.VisibleToStudentAsync(schoolId, userId)
                    : await db.VisibleToParentAsync(schoolId, userId);
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
                await db.Users.CountAsync(u => u.Roles.Contains(UserRole.Teacher)),
                await db.SchoolClasses.CountAsync());
        }

        return Ok(new DashboardDto(
            todayLessons, todayLessonsContext, recentGrades, pendingAbsences,
            unreadMessageCount, unreadConversations, upcomingEvents, unreadBehaviorRecords, schoolCounts));
    }

    /// <summary>Filtered by day of week only, not <see cref="WeekParity"/> —
    /// the rest of the app doesn't resolve "is this calendar week odd or
    /// even" anywhere yet either (timetable-page.ts just lists every entry
    /// regardless of parity); that belongs to the Rozvrh redesign (see
    /// ROADMAP), not this summary widget.</summary>
    private async Task<List<TimetableEntryDto>> TodayLessonsForTeacherAsync(int teacherId, DayOfWeek today) =>
        await db.TimetableEntries
            .Where(e => e.TeacherId == teacherId && e.DayOfWeek == today)
            .OrderBy(e => e.PeriodNumber)
            .Select(e => new TimetableEntryDto(
                e.Id, e.SchoolClassId, e.SubjectId, e.Subject!.Name, e.Subject!.Abbreviation,
                e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId, e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name, Array.Empty<string>()))
            .ToListAsync();

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
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.SchoolClassId, SchoolClassName = s.SchoolClass!.Name })
            .FirstOrDefaultAsync();
        if (student is null)
        {
            return ([], null);
        }

        var lessons = await db.TimetableEntries
            .Where(e => e.SchoolClassId == student.SchoolClassId && e.DayOfWeek == today)
            .OrderBy(e => e.PeriodNumber)
            .Select(e => new TimetableEntryDto(
                e.Id, e.SchoolClassId, e.SubjectId, e.Subject!.Name, e.Subject!.Abbreviation,
                e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId, e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.ClassroomId, e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name, Array.Empty<string>()))
            .ToListAsync();

        return (lessons, $"{student.FirstName} {student.LastName} ({student.SchoolClassName})");
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
    SchoolCountsDto? SchoolCounts);

public record UpcomingEventDto(int Id, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, bool AllDay);

public record SchoolCountsDto(int StudentCount, int TeacherCount, int ClassCount);
