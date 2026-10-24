using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Retention;

/// <summary>Administrace → Archiv a výmaz údajů: pupils leaving the school, the former
/// pupils and staff still on record, and the school's rules for erasing
/// them (<see cref="RetentionPolicy"/>).</summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/retention")]
public class RetentionController(
    AppDbContext db, ICurrentSchoolContext currentSchool, StudentEraser studentEraser,
    AccountEraser accountEraser, LeaveNotifier notifier, Notifier notifications, TimeProvider clock) : ControllerBase
{
    private int SchoolId => currentSchool.SchoolId!.Value;
    private DateOnly Today => SchoolClock.Today(clock);

    [HttpGet("policy")]
    public async Task<ActionResult<RetentionPolicyDto>> GetPolicy() =>
        Ok(RetentionPolicyDto.From(await RetentionJob.PolicyOfAsync(db, SchoolId)));

    [HttpPut("policy")]
    public async Task<ActionResult<RetentionPolicyDto>> SetPolicy(RetentionPolicyDto request)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            return BadRequest("Neznámý způsob výmazu.");
        }
        if (request.AccountGraceDays is < 0 or > RetentionPolicy.MaxGraceDays)
        {
            return this.FieldError("accountGraceDays", $"Zadejte 0 až {RetentionPolicy.MaxGraceDays} dní.");
        }
        foreach (var (field, years) in request.Years())
        {
            if (years is < 0 or > RetentionPolicy.MaxYears)
            {
                return this.FieldError(field, $"Zadejte 0 až {RetentionPolicy.MaxYears} let.");
            }
        }
        foreach (var (field, years) in request.Years().Where(y => y.Field is not "profileYears" and not "staffYears"))
        {
            if (years > request.ProfileYears)
            {
                return this.FieldError(field, "Nejvýš tak dlouho jako celý profil.");
            }
        }

        var policy = await db.RetentionPolicies.FirstOrDefaultAsync(p => p.SchoolId == SchoolId);
        if (policy is null)
        {
            policy = new RetentionPolicy { SchoolId = SchoolId };
            db.RetentionPolicies.Add(policy);
        }
        request.ApplyTo(policy);
        await db.SaveChangesAsync();
        return Ok(RetentionPolicyDto.From(policy));
    }

    /// <summary>Pupils with a departure (announced or past) and deactivated
    /// staff not erased yet.</summary>
    [HttpGet("people")]
    public async Task<ActionResult<RetentionOverviewDto>> GetPeople()
    {
        var today = Today;
        var policy = await RetentionJob.PolicyOfAsync(db, SchoolId);
        db.IncludeFormerStudents = true;
        var students = await db.Students
            .Where(s => s.LeftAt != null)
            .OrderByDescending(s => s.LeftAt).ThenBy(s => s.LastName)
            .Select(s => new
            {
                Student = s,
                ClassName = s.SchoolClass!.Name,
                AccountActive = s.User != null && s.User.DeactivatedAt == null,
            })
            .ToListAsync();

        var staff = await RetentionJob.DeactivatedStaff(db, SchoolId)
            .Select(u => new { u.Id, u.Username, u.Person, u.DeactivatedAt })
            .ToListAsync();

        return Ok(new RetentionOverviewDto(
            students.Select(x => ToDto(x.Student, x.ClassName, x.AccountActive, policy, today)).ToList(),
            staff
                .Select(u =>
                {
                    var erasesOn = policy.StaffDueOn(DateOnly.FromDateTime(u.DeactivatedAt!.Value.UtcDateTime));
                    return new FormerStaffDto(u.Id, PersonNames.Display(u.Person, u.Username), u.DeactivatedAt.Value, erasesOn, erasesOn <= today);
                })
                .OrderBy(s => s.ErasesOn)
                .ToList()));
    }

    /// <summary>Records (or changes) a pupil's departure. Not once anything
    /// of theirs has been erased. A new departure, or a new last day, goes to
    /// the people in <see cref="LeaveRequest.Notices"/>.</summary>
    [HttpPut("students/{id:int}/leave")]
    public async Task<ActionResult<FormerStudentDto>> SetLeave(int id, LeaveRequest request)
    {
        db.IncludeFormerStudents = true;
        var student = await db.Students.Include(s => s.SchoolClass).FirstOrDefaultAsync(s => s.Id == id);
        if (student is null)
        {
            return NotFound();
        }
        if (student.ErasedCategories.Count > 0)
        {
            return BadRequest("Část údajů žáka už je vymazaná, ukončení vzdělávání nejde změnit.");
        }
        if (request.Reason is { } reason && !Enum.IsDefined(reason))
        {
            return this.FieldError("reason", "Neznámý důvod ukončení.");
        }
        var detail = string.IsNullOrWhiteSpace(request.ReasonDetail) ? null : request.ReasonDetail.Trim();
        if (request.Reason == StudentLeaveReason.Other && detail is null)
        {
            return this.FieldError("reasonDetail", "Uveďte odůvodnění.");
        }

        LeaveNoticeKind? kind = student.LeftAt is null ? LeaveNoticeKind.Set
            : student.LeftAt != request.LeftAt ? LeaveNoticeKind.DateChanged
            : null;
        student.LeftAt = request.LeftAt;
        student.LeaveReason = request.Reason;
        student.LeaveReasonDetail = request.Reason == StudentLeaveReason.Other ? detail : null;
        student.LeaveNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        List<Conversation> sent = kind is { } k
            ? await notifier.NotifyAsync(student, k, request.Notices ?? [], CurrentUserId())
            : [];
        await db.SaveChangesAsync();
        await notifications.MessagesAsync(sent);
        await db.SaveChangesAsync();
        return Ok(await DtoOfAsync(student));
    }

    /// <summary>Takes a departure back — the pupil (and their parents'
    /// accounts, unless erased) are back. Not once anything was erased.</summary>
    [HttpDelete("students/{id:int}/leave")]
    public async Task<IActionResult> CancelLeave(int id, [FromBody] CancelLeaveRequest? request = null)
    {
        db.IncludeFormerStudents = true;
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id);
        if (student is null || student.LeftAt is null)
        {
            return NotFound();
        }
        if (student.ErasedCategories.Count > 0)
        {
            return BadRequest("Část údajů žáka už je vymazaná, ukončení vzdělávání nejde vrátit.");
        }

        var sent = await notifier.NotifyAsync(student, LeaveNoticeKind.Cancelled, request?.Notices ?? [], CurrentUserId());
        student.LeftAt = null;
        student.LeaveReason = null;
        student.LeaveReasonDetail = null;
        student.LeaveNote = null;
        var accountIds = await db.ParentStudents
            .Where(ps => ps.StudentId == id)
            .Select(ps => ps.ParentProfile!.Person!.UserId)
            .ToListAsync();
        accountIds.Add(student.UserId);
        foreach (var user in await db.Users.Where(u => accountIds.Contains(u.Id)).ToListAsync())
        {
            accountEraser.Reactivate(user);
        }
        await db.SaveChangesAsync();
        await notifications.MessagesAsync(sent);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Erases what is due of a former pupil — or, with
    /// <see cref="EraseRequest.Everything"/>, all of it now (e.g. a request
    /// under čl. 17 GDPR).</summary>
    [HttpPost("students/{id:int}/erase")]
    public async Task<ActionResult<FormerStudentDto?>> EraseStudent(int id, EraseRequest request)
    {
        db.IncludeFormerStudents = true;
        var student = await db.Students.Include(s => s.SchoolClass).FirstOrDefaultAsync(s => s.Id == id);
        if (student is null)
        {
            return NotFound();
        }
        if (student.LeftAt is not { } leftAt || leftAt >= Today)
        {
            return BadRequest("Vymazat jde jen bývalého žáka.");
        }

        var policy = await RetentionJob.PolicyOfAsync(db, SchoolId);
        var categories = request.Everything ? [RetentionCategory.Profile] : StudentEraser.Due(student, policy, Today);
        if (categories.Count == 0)
        {
            return BadRequest("Nic není k výmazu.");
        }
        await studentEraser.EraseAsync(student, categories);
        return categories.Contains(RetentionCategory.Profile) ? NoContent() : Ok(await DtoOfAsync(student));
    }

    /// <summary>Carries out everything due — what the daily job does in a
    /// school that erases automatically.</summary>
    [HttpPost("erase-due")]
    public async Task<ActionResult<EraseDueResult>> EraseDue()
    {
        var today = Today;
        var policy = await RetentionJob.PolicyOfAsync(db, SchoolId);
        db.IncludeFormerStudents = true;
        var students = 0;
        foreach (var student in await db.Students.Where(s => s.LeftAt < today).ToListAsync())
        {
            var due = StudentEraser.Due(student, policy, today);
            if (due.Count > 0)
            {
                await studentEraser.EraseAsync(student, due);
                students++;
            }
        }
        var staff = await RetentionJob.StaffDueAsync(db, SchoolId, policy, today);
        foreach (var user in staff)
        {
            await accountEraser.EraseAsync(user);
        }
        return Ok(new EraseDueResult(students, staff.Count));
    }

    /// <summary>Erases a deactivated member of staff now.</summary>
    [HttpPost("staff/{userId:int}/erase")]
    public async Task<IActionResult> EraseStaff(int userId)
    {
        var user = await RetentionJob.DeactivatedStaff(db, SchoolId).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }
        await accountEraser.EraseAsync(user);
        return NoContent();
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<FormerStudentDto> DtoOfAsync(Student student)
    {
        var policy = await RetentionJob.PolicyOfAsync(db, SchoolId);
        var accountActive = student.UserId is { } userId
            && await db.Users.AnyAsync(u => u.Id == userId && u.DeactivatedAt == null);
        var className = student.SchoolClass?.Name
            ?? await db.SchoolClasses.Where(c => c.Id == student.SchoolClassId).Select(c => c.Name).FirstAsync();
        return ToDto(student, className, accountActive, policy, Today);
    }

    private static FormerStudentDto ToDto(Student s, string className, bool accountActive, RetentionPolicy policy, DateOnly today)
    {
        var leftAt = s.LeftAt!.Value;
        var pending = Enum.GetValues<RetentionCategory>()
            .Where(c => !s.ErasedCategories.Contains(c))
            .Select(c => new ScheduledErasureDto(c, policy.DueOn(c, leftAt)))
            .OrderBy(e => e.On)
            .ToList();
        return new FormerStudentDto(
            s.Id, s.FirstName, s.LastName, className, leftAt, s.LeaveReason, s.LeaveReasonDetail, s.LeaveNote,
            Former: leftAt < today, accountActive, s.ErasedCategories,
            Due: leftAt < today ? StudentEraser.Due(s, policy, today) : [],
            Pending: pending);
    }
}

public record RetentionPolicyDto(
    ErasureMode Mode,
    int AccountGraceDays,
    int PhotosYears,
    int ContactsYears,
    int MessagesYears,
    int HomeworkYears,
    int BehaviorYears,
    int AttendanceYears,
    int GradesYears,
    int PaymentsYears,
    int ProfileYears,
    int StaffYears)
{
    public static RetentionPolicyDto From(RetentionPolicy p) => new(
        p.Mode, p.AccountGraceDays, p.PhotosYears, p.ContactsYears, p.MessagesYears, p.HomeworkYears,
        p.BehaviorYears, p.AttendanceYears, p.GradesYears, p.PaymentsYears, p.ProfileYears, p.StaffYears);

    /// <summary>Every "years" field with its JSON name.</summary>
    public IEnumerable<(string Field, int Years)> Years() =>
    [
        ("photosYears", PhotosYears), ("contactsYears", ContactsYears), ("messagesYears", MessagesYears),
        ("homeworkYears", HomeworkYears), ("behaviorYears", BehaviorYears), ("attendanceYears", AttendanceYears),
        ("gradesYears", GradesYears), ("paymentsYears", PaymentsYears), ("profileYears", ProfileYears),
        ("staffYears", StaffYears),
    ];

    public void ApplyTo(RetentionPolicy p)
    {
        p.Mode = Mode;
        p.AccountGraceDays = AccountGraceDays;
        p.PhotosYears = PhotosYears;
        p.ContactsYears = ContactsYears;
        p.MessagesYears = MessagesYears;
        p.HomeworkYears = HomeworkYears;
        p.BehaviorYears = BehaviorYears;
        p.AttendanceYears = AttendanceYears;
        p.GradesYears = GradesYears;
        p.PaymentsYears = PaymentsYears;
        p.ProfileYears = ProfileYears;
        p.StaffYears = StaffYears;
    }
}

/// <param name="LeftAt">The pupil's last day at the school.</param>
/// <param name="ReasonDetail">Required with <see cref="StudentLeaveReason.Other"/>.</param>
/// <param name="Note">For the school only.</param>
/// <param name="Notices">Who's told (only of a new departure or a new last day).</param>
public record LeaveRequest(
    DateOnly LeftAt,
    StudentLeaveReason? Reason = null,
    string? Note = null,
    string? ReasonDetail = null,
    IReadOnlyList<LeaveNotice>? Notices = null);

public record CancelLeaveRequest(IReadOnlyList<LeaveNotice>? Notices = null);

/// <param name="Everything">Erase the whole profile now instead of what is due.</param>
public record EraseRequest(bool Everything = false);

public record EraseDueResult(int Students, int Staff);

public record ScheduledErasureDto(RetentionCategory Category, DateOnly On);

/// <param name="Former">The last day is over (false = departure announced).</param>
/// <param name="AccountActive">The pupil's own account still works.</param>
/// <param name="Due">Erasable now.</param>
/// <param name="Pending">Not erased yet, with the day each becomes due.</param>
public record FormerStudentDto(
    int Id,
    string FirstName,
    string LastName,
    string SchoolClassName,
    DateOnly LeftAt,
    StudentLeaveReason? Reason,
    string? ReasonDetail,
    string? Note,
    bool Former,
    bool AccountActive,
    IReadOnlyList<RetentionCategory> Erased,
    IReadOnlyList<RetentionCategory> Due,
    IReadOnlyList<ScheduledErasureDto> Pending);

public record FormerStaffDto(int UserId, string Name, DateTimeOffset DeactivatedAt, DateOnly ErasesOn, bool Due);

public record RetentionOverviewDto(IReadOnlyList<FormerStudentDto> Students, IReadOnlyList<FormerStaffDto> Staff);
