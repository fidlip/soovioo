using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Messaging;

/// <summary>Messaging as conversations: a subject, participants and their
/// messages. Everyone sees only conversations they take part in (Admin
/// included — no reading other people's correspondence). Staff start one
/// with a pupil's family; a parent/pupil with the class's teachers or the
/// school's leadership (see <see cref="GetContacts"/>). Bulk sends live in
/// <see cref="MessageBroadcastController"/>.</summary>
[Authorize]
[ApiController]
[Route("api/conversations")]
public class ConversationsController(
    AppDbContext db, ICurrentSchoolContext currentSchool, MessageAttachmentStorageService storage,
    Notifier notifier) : ControllerBase
{
    /// <summary>The caller's conversations, newest first. A bulk send the
    /// caller made shows up once (<c>BroadcastId</c> set, counts of families,
    /// replies and unread ones) instead of one row per family — see
    /// <see cref="GetBroadcast"/> for its conversations.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationSummaryDto>>> GetConversations()
    {
        var me = CurrentUserId();
        var conversations = await db.Conversations
            .Where(c => c.Participants.Any(p => p.UserId == me))
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new
            {
                c.Id,
                c.Subject,
                c.StudentId,
                StudentName = c.Student == null ? null : c.Student.FirstName + " " + c.Student.LastName,
                c.LastMessageAt,
                Participants = c.Participants.Select(p => PersonNames.Display(p.User!.Person, p.User.Username)).ToList(),
                LastMessage = c.Messages.OrderByDescending(m => m.SentAt)
                    .Select(m => new { m.SenderName, m.Body }).FirstOrDefault(),
                Unread = c.Messages.Any(m =>
                    (m.SenderUserId == null || m.SenderUserId != me)
                    && c.Participants.Any(p => p.UserId == me && (p.LastReadAt == null || m.SentAt > p.LastReadAt))),
                HasReply = c.Messages.Any(m => m.SenderUserId == null || m.SenderUserId != me),
                c.BroadcastId,
                BroadcastIsMine = c.Broadcast != null && c.Broadcast.SenderUserId == me,
                BroadcastSubject = c.Broadcast == null ? null : c.Broadcast.Subject,
                BroadcastAudience = c.Broadcast == null ? null : c.Broadcast.AudienceLabel,
            })
            .ToListAsync();

        var result = new List<ConversationSummaryDto>();
        foreach (var c in conversations.Where(c => !(c.BroadcastId is not null && c.BroadcastIsMine)))
        {
            result.Add(new ConversationSummaryDto(
                c.Id, c.Subject, c.StudentId, c.StudentName, c.Participants, c.LastMessageAt,
                c.LastMessage?.SenderName, Preview(c.LastMessage?.Body), c.Unread));
        }
        foreach (var group in conversations.Where(c => c.BroadcastId is not null && c.BroadcastIsMine).GroupBy(c => c.BroadcastId!.Value))
        {
            // Already newest first, so First() carries the latest message.
            var latest = group.First();
            result.Add(new ConversationSummaryDto(
                latest.Id, latest.BroadcastSubject ?? latest.Subject, null, latest.BroadcastAudience, [],
                latest.LastMessageAt, latest.LastMessage?.SenderName, Preview(latest.LastMessage?.Body),
                group.Any(c => c.Unread),
                BroadcastId: group.Key,
                BroadcastConversationCount: group.Count(),
                BroadcastRepliedCount: group.Count(c => c.HasReply),
                BroadcastUnreadCount: group.Count(c => c.Unread)));
        }
        return Ok(result.OrderByDescending(c => c.LastMessageAt));
    }

    /// <summary>A bulk send of the caller: the original message plus every
    /// family's conversation (unread first, then those with replies, newest
    /// first). Only the sender sees it.</summary>
    [HttpGet("broadcasts/{id:int}")]
    public async Task<ActionResult<BroadcastDto>> GetBroadcast(int id)
    {
        var me = CurrentUserId();
        var broadcast = await db.MessageBroadcasts.FirstOrDefaultAsync(b => b.Id == id && b.SenderUserId == me);
        if (broadcast is null)
        {
            return NotFound();
        }

        var conversations = await db.Conversations
            .Where(c => c.BroadcastId == id && c.Participants.Any(p => p.UserId == me))
            .Select(c => new
            {
                c.Id,
                StudentName = c.Student == null ? null : c.Student.FirstName + " " + c.Student.LastName,
                Participants = c.Participants.Where(p => p.UserId != me)
                    .Select(p => PersonNames.Display(p.User!.Person, p.User.Username)).ToList(),
                ReplyCount = c.Messages.Count(m => m.SenderUserId == null || m.SenderUserId != me),
                c.LastMessageAt,
                Unread = c.Messages.Any(m =>
                    (m.SenderUserId == null || m.SenderUserId != me)
                    && c.Participants.Any(p => p.UserId == me && (p.LastReadAt == null || m.SentAt > p.LastReadAt))),
            })
            .ToListAsync();

        // The file attached to every copy — the same on each, list it once.
        var firstConversationId = conversations.Select(c => c.Id).DefaultIfEmpty().Min();
        var attachments = await db.MessageAttachments
            .Where(a => a.Message!.ConversationId == firstConversationId && a.Message.SenderUserId == me)
            .OrderBy(a => a.Message!.SentAt)
            .Select(a => new BroadcastAttachmentDto(
                a.Message!.ConversationId, a.MessageId,
                new MessageAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)))
            .Take(10)
            .ToListAsync();

        return Ok(new BroadcastDto(
            broadcast.Id, broadcast.Subject, broadcast.Body, broadcast.AudienceLabel, broadcast.StudentsOnly,
            broadcast.CreatedAt, attachments,
            conversations
                .OrderByDescending(c => c.Unread)
                .ThenByDescending(c => c.ReplyCount > 0)
                .ThenByDescending(c => c.ReplyCount > 0 ? c.LastMessageAt : DateTimeOffset.MinValue)
                .ThenBy(c => c.StudentName)
                .Select(c => new BroadcastConversationDto(
                    c.Id, c.StudentName, c.Participants, c.ReplyCount, c.LastMessageAt, c.Unread))
                .ToList()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConversationDto>> GetConversation(int id, [FromServices] PhotoAccess? photoAccess = null)
    {
        var conversation = await LoadForParticipantAsync(id);
        if (conversation is null)
        {
            return NotFound();
        }
        var dto = ToDto(conversation);
        if (photoAccess is not null)
        {
            var photos = await photoAccess.VisibleUserPhotosAsync(
                User, conversation.Participants.Where(p => p.User is not null).Select(p => p.User!));
            dto = dto with
            {
                Participants = dto.Participants.Select(p => p with { Photo = photos.GetValueOrDefault(p.UserId) }).ToList(),
            };
        }
        return Ok(dto);
    }

    /// <summary>Staff: <c>StudentId</c> required — the pupil's parents and
    /// the pupil's own account become the other participants. Parent/pupil:
    /// <c>StudentId</c> = which (own) child it's about, recipients picked
    /// from <see cref="GetContacts"/>.</summary>
    [HttpPost]
    public async Task<ActionResult<ConversationDto>> StartConversation(StartConversationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest("Předmět i text jsou povinné.");
        }

        var me = CurrentUserId();
        List<int> recipients;
        if (User.IsInRole(nameof(UserRole.Teacher)) || User.IsInRole(nameof(UserRole.Admin)))
        {
            if (request.StudentId is not { } studentId || !await db.Students.AnyAsync(s => s.Id == studentId))
            {
                return BadRequest("Neznámý žák.");
            }
            if (request.StudentOnly)
            {
                // Just the pupil (no parents) — e.g. a reminder that would only bother the family.
                recipients = await db.Students.Where(s => s.Id == studentId && s.UserId != null)
                    .Select(s => s.UserId!.Value).ToListAsync();
                if (recipients.Count == 0)
                {
                    return BadRequest("Žák nemá vlastní účet — zprávu by nikdo nedostal.");
                }
            }
            else
            {
                recipients = await db.FamilyUserIdsAsync(studentId);
                if (recipients.Count == 0)
                {
                    return BadRequest("Žák nemá účet ani přiřazené rodiče — zprávu by nikdo nedostal.");
                }
            }
        }
        else
        {
            if (request.StudentId is not { } studentId || !await User.CanAccessStudentAsync(db, studentId))
            {
                return Forbid();
            }
            var allowed = (await ContactsForStudentAsync(studentId)).Select(c => c.UserId).ToHashSet();
            recipients = (request.RecipientUserIds ?? []).Distinct().ToList();
            if (recipients.Count == 0 || recipients.Any(r => !allowed.Contains(r)))
            {
                return BadRequest("Vyberte příjemce ze seznamu.");
            }
        }

        var conversation = await NewConversationAsync(request.Subject, request.StudentId, me, recipients, request.Body);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        await notifier.MessagesAsync([conversation]);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id },
            ToDto((await LoadForParticipantAsync(conversation.Id))!));
    }

    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<MessageDto>> Reply(int id, ReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest("Text je povinný.");
        }
        var conversation = await db.Conversations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id && c.Participants.Any(p => p.UserId == CurrentUserId()));
        if (conversation is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            ConversationId = id,
            SenderUserId = CurrentUserId(),
            SenderName = await SenderNameAsync(),
            Body = request.Body,
            SentAt = now,
            SchoolId = conversation.SchoolId,
        };
        db.Messages.Add(message);
        conversation.LastMessageAt = now;
        // Your own reply never counts as unread for you.
        conversation.Participants.First(p => p.UserId == CurrentUserId()).LastReadAt = now;
        await db.SaveChangesAsync();
        await notifier.MessagesAsync([conversation]);
        await db.SaveChangesAsync();

        return Ok(ToDto(message));
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var participant = await db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == CurrentUserId());
        if (participant is null)
        {
            return NotFound();
        }
        participant.LastReadAt = DateTimeOffset.UtcNow;
        // Read here, read under the bell too.
        foreach (var notification in await db.Notifications
            .Where(n => n.UserId == participant.UserId && n.Type == NotificationType.Message && n.TargetId == id && n.ReadAt == null)
            .ToListAsync())
        {
            notification.ReadAt = participant.LastReadAt;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Who a parent/pupil may write to about <paramref name="studentId"/>:
    /// the class teacher, everyone teaching the class (per timetable, with
    /// their subjects) and the school's admins.</summary>
    [HttpGet("contacts")]
    public async Task<ActionResult<IEnumerable<ContactDto>>> GetContacts(int studentId)
    {
        if (!await User.CanAccessStudentAsync(db, studentId))
        {
            return Forbid();
        }
        return Ok(await ContactsForStudentAsync(studentId));
    }

    /// <summary>Separate step after sending (which stays plain JSON) — only
    /// the message's own sender may attach to it.</summary>
    [HttpPost("{id:int}/messages/{messageId:int}/attachments")]
    public async Task<ActionResult<MessageAttachmentDto>> UploadAttachment(int id, int messageId, IFormFile file)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m =>
            m.Id == messageId && m.ConversationId == id && m.SenderUserId == CurrentUserId());
        if (message is null)
        {
            return NotFound();
        }

        if (file.Length == 0)
        {
            return BadRequest("Prázdný soubor.");
        }
        if (file.Length > MessageAttachmentRules.MaxSizeBytes)
        {
            return BadRequest("Soubor je příliš velký (max 10 MB).");
        }
        if (!MessageAttachmentRules.IsAllowedFileName(file.FileName))
        {
            return BadRequest("Nepovolený typ souboru.");
        }
        if (await MessageAttachmentRules.DemoLimitErrorAsync(db, currentSchool.SchoolId!.Value, file.Length) is { } demoLimit)
        {
            return BadRequest(demoLimit);
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var attachment = new MessageAttachment
        {
            MessageId = messageId,
            SchoolId = schoolId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = await storage.SaveAsync(schoolId, file),
        };
        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync();

        return Ok(new MessageAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
    }

    [HttpGet("{id:int}/messages/{messageId:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int id, int messageId, int attachmentId)
    {
        var me = CurrentUserId();
        var attachment = await db.MessageAttachments.FirstOrDefaultAsync(a =>
            a.Id == attachmentId && a.MessageId == messageId && a.Message!.ConversationId == id
            && a.Message.Conversation!.Participants.Any(p => p.UserId == me));
        if (attachment is null)
        {
            return NotFound();
        }

        var fullPath = storage.GetFullPath(attachment.StoragePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }
        return PhysicalFile(fullPath, attachment.ContentType, attachment.FileName);
    }

    /// <summary>Only the sender may delete a message (e.g. sent by mistake);
    /// a conversation left without messages disappears with it.</summary>
    [HttpDelete("{id:int}/messages/{messageId:int}")]
    public async Task<IActionResult> DeleteMessage(int id, int messageId)
    {
        var message = await db.Messages.Include(m => m.Attachments).FirstOrDefaultAsync(m =>
            m.Id == messageId && m.ConversationId == id && m.SenderUserId == CurrentUserId());
        if (message is null)
        {
            return NotFound();
        }

        var storagePaths = message.Attachments.Select(a => a.StoragePath).ToList();
        db.Messages.Remove(message);
        await db.SaveChangesAsync();

        var conversation = await db.Conversations.Include(c => c.Messages).FirstAsync(c => c.Id == id);
        if (conversation.Messages.Count == 0)
        {
            db.Conversations.Remove(conversation);
        }
        else
        {
            conversation.LastMessageAt = conversation.Messages.Max(m => m.SentAt);
        }
        await db.SaveChangesAsync();

        // Each attachment owns its file exclusively (see MessageAttachment).
        foreach (var path in storagePaths)
        {
            storage.Delete(path);
        }
        return NoContent();
    }

    public static Conversation NewConversation(
        string subject, int? studentId, int senderId, string senderName, IEnumerable<int> recipientIds,
        string body, int schoolId, DateTimeOffset now)
    {
        var conversation = new Conversation
        {
            Subject = subject,
            StudentId = studentId,
            CreatedByUserId = senderId,
            CreatedAt = now,
            LastMessageAt = now,
            SchoolId = schoolId,
        };
        conversation.Participants.Add(new ConversationParticipant { UserId = senderId, SchoolId = schoolId, LastReadAt = now });
        foreach (var recipient in recipientIds.Where(r => r != senderId).Distinct())
        {
            conversation.Participants.Add(new ConversationParticipant { UserId = recipient, SchoolId = schoolId });
        }
        conversation.Messages.Add(new Message
        {
            SenderUserId = senderId,
            SenderName = senderName,
            Body = body,
            SentAt = now,
            SchoolId = schoolId,
        });
        return conversation;
    }

    private async Task<Conversation> NewConversationAsync(
        string subject, int? studentId, int me, List<int> recipients, string body) =>
        NewConversation(subject, studentId, me, await SenderNameAsync(), recipients, body,
            currentSchool.SchoolId!.Value, DateTimeOffset.UtcNow);

    private async Task<List<ContactDto>> ContactsForStudentAsync(int studentId)
    {
        var classId = await db.Students.Where(s => s.Id == studentId).Select(s => s.SchoolClassId).FirstAsync();
        var classTeacherId = await db.SchoolClasses.Where(c => c.Id == classId).Select(c => c.ClassTeacherId).FirstOrDefaultAsync();
        var teaching = await db.TimetableEntries
            .Where(t => t.SchoolClassId == classId && t.TeacherId != null)
            .Select(t => new { TeacherId = t.TeacherId!.Value, t.Subject!.Name })
            .Distinct()
            .ToListAsync();
        var adminIds = await db.Users.Where(u => u.Roles.Contains(UserRole.Admin)).Select(u => u.Id).ToListAsync();

        var userIds = teaching.Select(t => t.TeacherId).Concat(adminIds).ToHashSet();
        if (classTeacherId is { } ct)
        {
            userIds.Add(ct);
        }
        var names = (await db.Users.Include(u => u.Person).Where(u => userIds.Contains(u.Id)).ToListAsync())
            .ToDictionary(u => u.Id, PersonNames.Display);

        return userIds
            .Where(names.ContainsKey)
            .Select(id =>
            {
                var notes = new List<string>();
                if (id == classTeacherId)
                {
                    notes.Add("třídní učitel");
                }
                notes.AddRange(teaching.Where(t => t.TeacherId == id).Select(t => t.Name).Order());
                if (adminIds.Contains(id))
                {
                    notes.Add("vedení školy");
                }
                return new ContactDto(id, names[id], string.Join(", ", notes));
            })
            // Class teacher first, then teachers, leadership last.
            .OrderBy(c => c.UserId == classTeacherId ? 0 : adminIds.Contains(c.UserId) && !teaching.Any(t => t.TeacherId == c.UserId) ? 2 : 1)
            .ThenBy(c => c.Name)
            .ToList();
    }

    private async Task<Conversation?> LoadForParticipantAsync(int id)
    {
        var me = CurrentUserId();
        return await db.Conversations
            .Include(c => c.Student)
            .Include(c => c.Participants).ThenInclude(p => p.User).ThenInclude(u => u!.Person)
            .Include(c => c.Messages).ThenInclude(m => m.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id && c.Participants.Any(p => p.UserId == me));
    }

    private static ConversationDto ToDto(Conversation c) => new(
        c.Id,
        c.Subject,
        c.StudentId,
        c.Student is null ? null : $"{c.Student.FirstName} {c.Student.LastName}",
        c.Participants.Select(p => new ParticipantDto(p.UserId, p.User is null ? "?" : PersonNames.Display(p.User))).OrderBy(p => p.Name).ToList(),
        c.Messages.OrderBy(m => m.SentAt).Select(ToDto).ToList());

    private static MessageDto ToDto(Message m) => new(
        m.Id, m.SenderUserId, m.SenderName, m.Body, m.SentAt,
        m.Attachments.Select(a => new MessageAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList());

    private static string? Preview(string? body) =>
        body is null ? null : body.Length <= 120 ? body : body[..120] + "…";

    /// <summary>Snapshot of the sender's display name at send time.</summary>
    private async Task<string> SenderNameAsync()
    {
        var me = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        return me is null ? User.Identity?.Name ?? "Neznámý uživatel" : PersonNames.Display(me);
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public static class ConversationQueries
{
    /// <summary>Everyone who should hear about a pupil: the pupil's own
    /// account and all linked parents.</summary>
    public static async Task<List<int>> FamilyUserIdsAsync(this AppDbContext db, int studentId)
    {
        var parents = await db.ParentStudents.Where(ps => ps.StudentId == studentId)
            .ParentAccountLinks().Select(l => l.UserId).ToListAsync();
        var own = await db.Students.Where(s => s.Id == studentId && s.UserId != null).Select(s => s.UserId!.Value).ToListAsync();
        return parents.Concat(own).Distinct().ToList();
    }

    public static Task<int> CountUnreadConversationsAsync(this AppDbContext db, int userId) =>
        db.ConversationParticipants
            .Where(p => p.UserId == userId)
            .CountAsync(p => p.Conversation!.Messages.Any(m =>
                (m.SenderUserId == null || m.SenderUserId != userId)
                && (p.LastReadAt == null || m.SentAt > p.LastReadAt)));
}

/// <param name="StudentOnly">Staff: only the pupil's own account, not the parents.</param>
public record StartConversationRequest(
    string Subject, string Body, int? StudentId = null, IReadOnlyList<int>? RecipientUserIds = null,
    bool StudentOnly = false);

public record ReplyRequest(string Body);

public record MessageAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes);

public record MessageDto(
    int Id, int? SenderUserId, string SenderName, string Body, DateTimeOffset SentAt,
    IReadOnlyList<MessageAttachmentDto> Attachments);

/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
public record ParticipantDto(int UserId, string Name, string? Photo = null);

public record ConversationDto(
    int Id, string Subject, int? StudentId, string? StudentName,
    IReadOnlyList<ParticipantDto> Participants, IReadOnlyList<MessageDto> Messages);

/// <param name="BroadcastId">Set on the one row standing for a bulk send of
/// the caller (then StudentName = who it went to, and the counts are set).</param>
public record ConversationSummaryDto(
    int Id, string Subject, int? StudentId, string? StudentName, IReadOnlyList<string> Participants,
    DateTimeOffset LastMessageAt, string? LastSenderName, string? LastMessagePreview, bool Unread,
    int? BroadcastId = null, int? BroadcastConversationCount = null, int? BroadcastRepliedCount = null,
    int? BroadcastUnreadCount = null);

public record BroadcastAttachmentDto(int ConversationId, int MessageId, MessageAttachmentDto Attachment);

public record BroadcastConversationDto(
    int ConversationId, string? StudentName, IReadOnlyList<string> Participants, int ReplyCount,
    DateTimeOffset LastMessageAt, bool Unread);

public record BroadcastDto(
    int Id, string Subject, string Body, string AudienceLabel, bool StudentsOnly, DateTimeOffset CreatedAt,
    IReadOnlyList<BroadcastAttachmentDto> Attachments, IReadOnlyList<BroadcastConversationDto> Conversations);

/// <param name="Note">Why this person is offered — "třídní učitel",
/// subjects taught, "vedení školy".</param>
public record ContactDto(int UserId, string Name, string Note);
