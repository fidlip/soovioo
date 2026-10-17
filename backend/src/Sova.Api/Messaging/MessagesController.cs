using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Messaging;

[Authorize]
[ApiController]
[Route("api/students/{studentId}/messages")]
public class MessagesController(
    AppDbContext db, ICurrentSchoolContext currentSchool, MessageAttachmentStorageService storage) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetMessages(int studentId)
    {
        if (!await User.CanAccessStudentAsync(db, studentId))
        {
            return Forbid();
        }

        var messages = await db.Messages
            .Where(m => m.StudentId == studentId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new MessageDto(
                m.Id, m.StudentId, m.SenderName, m.Subject, m.Body, m.SentAt, m.ReadAt,
                m.Attachments.Select(a => new MessageAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList()))
            .ToListAsync();

        return Ok(messages);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<MessageDto>> SendMessage(int studentId, SendMessageRequest request)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return BadRequest("Unknown studentId.");
        }

        var message = new Message
        {
            StudentId = studentId,
            // Taken from the authenticated token, not from client input —
            // otherwise any logged-in user (e.g. a parent) could put any
            // name here and send a message that looks like it came from
            // the teacher.
            SenderName = User.Identity?.Name ?? "Neznámý uživatel",
            Subject = request.Subject,
            Body = request.Body,
            SchoolId = currentSchool.SchoolId!.Value,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var dto = new MessageDto(
            message.Id, message.StudentId, message.SenderName, message.Subject,
            message.Body, message.SentAt, message.ReadAt, []);
        return CreatedAtAction(nameof(GetMessages), new { studentId }, dto);
    }

    /// <summary>A separate step after SendMessage/broadcast rather than
    /// bundled into those (which stay plain JSON) — keeps the common
    /// no-attachment send path, including the mobile app's, untouched by
    /// multipart/form-data handling.</summary>
    [HttpPost("{messageId:int}/attachments")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<MessageAttachmentDto>> UploadAttachment(int studentId, int messageId, IFormFile file)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.StudentId == studentId);
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

        var schoolId = currentSchool.SchoolId!.Value;
        var storagePath = await storage.SaveAsync(schoolId, file);

        var attachment = new MessageAttachment
        {
            MessageId = messageId,
            SchoolId = schoolId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storagePath,
        };
        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync();

        return Ok(new MessageAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
    }

    [HttpGet("{messageId:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int studentId, int messageId, int attachmentId)
    {
        if (!await User.CanAccessStudentAsync(db, studentId))
        {
            return Forbid();
        }

        var attachment = await db.MessageAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.MessageId == messageId && a.Message!.StudentId == studentId);
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

    [HttpPost("{id}/read")]
    public async Task<ActionResult> MarkRead(int studentId, int id)
    {
        if (!await User.CanAccessStudentAsync(db, studentId))
        {
            return Forbid();
        }

        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id && m.StudentId == studentId);
        if (message is null)
        {
            return NotFound();
        }

        message.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>No edit action exists for messages on purpose — a sent
    /// message is immutable, like an email. Delete (e.g. sent by mistake)
    /// is restricted to Teacher/Admin, same as sending.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteMessage(int studentId, int id)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id && m.StudentId == studentId);
        if (message is null)
        {
            return NotFound();
        }

        var attachments = await db.MessageAttachments.Where(a => a.MessageId == id).ToListAsync();
        db.Messages.Remove(message); // cascades to the MessageAttachment rows in the DB
        await db.SaveChangesAsync();

        // Each attachment owns its file exclusively (see MessageAttachment's
        // doc comment), so this is always safe to delete unconditionally.
        foreach (var attachment in attachments)
        {
            storage.Delete(attachment.StoragePath);
        }

        return NoContent();
    }
}

public record SendMessageRequest(string Subject, string Body);

public record MessageAttachmentDto(int Id, string FileName, string ContentType, long SizeBytes);

public record MessageDto(
    int Id,
    int StudentId,
    string SenderName,
    string Subject,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt,
    IReadOnlyList<MessageAttachmentDto> Attachments);
