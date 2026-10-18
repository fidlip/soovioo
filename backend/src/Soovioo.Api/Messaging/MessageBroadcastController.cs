using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Messaging;

/// <summary>Sends the same message into every targeted student's thread at
/// once (fan-out — still just one <see cref="Message"/> row per student, the
/// same model <see cref="MessagesController"/> uses for a single recipient).
/// Admin can target any class or the whole school; Teacher can only target a
/// class they actually teach (třídní učitel or has a timetable entry in it)
/// — unlike single-student messaging, which stays unrestricted for both
/// roles, broadcast reaches many parents at once, so it's scoped to classes
/// the teacher is actually responsible for.</summary>
[Authorize(Roles = "Teacher,Admin")]
[ApiController]
public class MessageBroadcastController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <summary>Classes the caller is allowed to broadcast to — every class
    /// for Admin, only owned ones for Teacher. Drives the class picker so
    /// the frontend never offers a class that would 403.</summary>
    [HttpGet("api/messages/broadcastable-classes")]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetBroadcastableClasses()
    {
        if (User.IsInRole("Admin"))
        {
            return Ok(await db.SchoolClasses.OrderBy(c => c.Name).ToListAsync());
        }

        var classIds = await db.GetTeacherClassIdsAsync(CurrentUserId());
        var classes = await db.SchoolClasses
            .Where(c => classIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(classes);
    }

    [HttpPost("api/school-classes/{schoolClassId:int}/messages/broadcast")]
    public async Task<ActionResult<BroadcastResultDto>> BroadcastToClass(int schoolClassId, SendMessageRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(schoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Unknown schoolClassId.");
        }

        if (!User.IsInRole("Admin") && !await db.TeacherOwnsClassAsync(CurrentUserId(), schoolClassId))
        {
            return Forbid();
        }

        var studentIds = await db.Students.Where(s => s.SchoolClassId == schoolClassId).Select(s => s.Id).ToListAsync();
        return Ok(await SendToStudentsAsync(studentIds, request));
    }

    /// <summary>Whole school — Admin only (not combined with the class-level
    /// Teacher,Admin authorize on this controller, the method's own Admin
    /// role requirement is ANDed on top of it).</summary>
    [HttpPost("api/messages/broadcast")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BroadcastResultDto>> BroadcastToSchool(SendMessageRequest request)
    {
        var studentIds = await db.Students.Select(s => s.Id).ToListAsync();
        return Ok(await SendToStudentsAsync(studentIds, request));
    }

    private async Task<BroadcastResultDto> SendToStudentsAsync(List<int> studentIds, SendMessageRequest request)
    {
        var senderName = User.Identity?.Name ?? "Neznámý uživatel";
        var sentAt = DateTimeOffset.UtcNow;
        var schoolId = currentSchool.SchoolId!.Value;

        var messages = studentIds.Select(studentId => new Message
        {
            StudentId = studentId,
            SenderName = senderName,
            Subject = request.Subject,
            Body = request.Body,
            SentAt = sentAt,
            SchoolId = schoolId,
        }).ToList();

        db.Messages.AddRange(messages);
        await db.SaveChangesAsync();

        // Lets the caller attach a file to every fan-out copy afterwards
        // (MessagesController.UploadAttachment, one call per message) —
        // see MessageAttachment's doc comment for why each copy gets its
        // own physical file rather than a shared one.
        return new BroadcastResultDto(
            studentIds.Count,
            messages.Select(m => new BroadcastMessageRef(m.Id, m.StudentId)).ToList());
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record BroadcastMessageRef(int MessageId, int StudentId);

public record BroadcastResultDto(int RecipientCount, IReadOnlyList<BroadcastMessageRef> Messages);
