using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Messaging;

[Authorize]
[ApiController]
[Route("api/students/{studentId}/messages")]
public class MessagesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetMessages(int studentId)
    {
        var messages = await db.Messages
            .Where(m => m.StudentId == studentId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.StudentId, m.SenderName, m.Subject, m.Body, m.SentAt, m.ReadAt))
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
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var dto = new MessageDto(
            message.Id, message.StudentId, message.SenderName, message.Subject,
            message.Body, message.SentAt, message.ReadAt);
        return CreatedAtAction(nameof(GetMessages), new { studentId }, dto);
    }

    [HttpPost("{id}/read")]
    public async Task<ActionResult> MarkRead(int studentId, int id)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id && m.StudentId == studentId);
        if (message is null)
        {
            return NotFound();
        }

        message.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record SendMessageRequest(string Subject, string Body);

public record MessageDto(
    int Id,
    int StudentId,
    string SenderName,
    string Subject,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);
