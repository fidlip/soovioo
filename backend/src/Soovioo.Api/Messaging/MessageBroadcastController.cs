using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Messaging;

/// <summary>Sends the same message to every targeted pupil's family at once —
/// fan-out into one <see cref="Conversation"/> per pupil (sender + the
/// pupil's account + parents), so every family can reply privately; the
/// same shape <see cref="ConversationsController"/> creates for one pupil.
/// Who may target which classes, and the whole school, are permissions —
/// unlike single-student messaging, which stays unrestricted for staff,
/// broadcast reaches many parents at once.</summary>
[Authorize(Roles = nameof(RoleKind.Staff))]
[ApiController]
public class MessageBroadcastController(AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier) : ControllerBase
{
    /// <summary>Classes the caller is allowed to broadcast to. Drives the
    /// class picker so the frontend never offers a class that would 403.</summary>
    [HttpGet("api/messages/broadcastable-classes")]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetBroadcastableClasses()
    {
        var classIds = await User.ClassIdsAsync(db, Permission.MessagesBroadcastClass);
        if (classIds is null)
        {
            return Ok(await db.SchoolClasses.OrderBy(c => c.Name).ToListAsync());
        }

        var classes = await db.SchoolClasses
            .Where(c => classIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(classes);
    }

    [HttpPost("api/school-classes/{schoolClassId:int}/messages/broadcast")]
    [RequirePermission(Permission.MessagesBroadcastClass)]
    public async Task<ActionResult<BroadcastResultDto>> BroadcastToClass(int schoolClassId, BroadcastRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(schoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        if (!await User.ReachesClassAsync(db, Permission.MessagesBroadcastClass, schoolClassId))
        {
            return Forbid();
        }

        var studentIds = await db.Students.Where(s => s.SchoolClassId == schoolClassId).Select(s => s.Id).ToListAsync();
        return Ok(await SendToStudentsAsync(studentIds, request, schoolClass.Name));
    }

    [HttpPost("api/messages/broadcast")]
    [RequirePermission(Permission.MessagesBroadcastSchool)]
    public async Task<ActionResult<BroadcastResultDto>> BroadcastToSchool(BroadcastRequest request)
    {
        var studentIds = await db.Students.Select(s => s.Id).ToListAsync();
        return Ok(await SendToStudentsAsync(studentIds, request, "celá škola"));
    }

    private async Task<BroadcastResultDto> SendToStudentsAsync(
        List<int> studentIds, BroadcastRequest request, string audienceLabel)
    {
        var senderId = CurrentUserId();
        var senderName = (await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == senderId)) is { } sender
            ? PersonNames.Display(sender)
            : User.Identity?.Name ?? "Neznámý uživatel";
        var now = DateTimeOffset.UtcNow;
        var schoolId = currentSchool.SchoolId!.Value;

        // StudentsOnly: just the pupils' own accounts, no parents.
        var studentsOnly = request.StudentsOnly;
        var parentLinks = await db.ParentStudents
            .Where(ps => !studentsOnly && studentIds.Contains(ps.StudentId))
            .ParentAccountLinks()
            .Select(l => new { l.StudentId, l.UserId })
            .ToListAsync();
        var ownAccounts = await db.Students
            .Where(s => studentIds.Contains(s.Id) && s.Person!.UserId != null)
            .Select(s => new { StudentId = s.Id, UserId = s.Person!.UserId!.Value })
            .ToListAsync();
        var familyByStudent = parentLinks.Concat(ownAccounts)
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());

        // Pupils without any account in the family would get a conversation
        // nobody can read — skip them (the count tells the sender).
        var conversations = studentIds
            .Where(familyByStudent.ContainsKey)
            .Select(studentId => ConversationsController.NewConversation(
                request.Subject, studentId, senderId, senderName, familyByStudent[studentId],
                request.Body, schoolId, now))
            .ToList();

        if (conversations.Count > 0)
        {
            var broadcast = new MessageBroadcast
            {
                SchoolId = schoolId,
                SenderUserId = senderId,
                SenderName = senderName,
                Subject = request.Subject,
                Body = request.Body,
                AudienceLabel = request.StudentsOnly ? $"{audienceLabel} — jen žáci" : audienceLabel,
                StudentsOnly = request.StudentsOnly,
                CreatedAt = now,
            };
            db.MessageBroadcasts.Add(broadcast);
            foreach (var conversation in conversations)
            {
                conversation.Broadcast = broadcast;
            }
        }

        db.Conversations.AddRange(conversations);
        await db.SaveChangesAsync();
        await notifier.MessagesAsync(conversations);
        await db.SaveChangesAsync();

        // Lets the caller attach a file to every fan-out copy afterwards
        // (ConversationsController.UploadAttachment, one call per message) —
        // see MessageAttachment's doc comment for why each copy gets its
        // own physical file rather than a shared one.
        return new BroadcastResultDto(
            conversations.Count,
            studentIds.Count - conversations.Count,
            conversations.Select(c => new BroadcastMessageRef(c.Id, c.Messages[0].Id)).ToList());
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <param name="StudentsOnly">Only to the pupils' own accounts (no
/// parents) — for things that would just bother the parents.</param>
public record BroadcastRequest(string Subject, string Body, bool StudentsOnly = false);

public record BroadcastMessageRef(int ConversationId, int MessageId);

/// <param name="SkippedCount">Pupils with nobody to receive it (no own
/// account, and — unless StudentsOnly — no linked parent).</param>
public record BroadcastResultDto(int RecipientCount, int SkippedCount, IReadOnlyList<BroadcastMessageRef> Messages);
