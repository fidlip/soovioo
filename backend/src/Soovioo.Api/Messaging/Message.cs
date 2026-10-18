using Soovioo.Api.Evidence;

namespace Soovioo.Api.Messaging;

public class Message
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public required string SenderName { get; set; }
    public required string Subject { get; set; }
    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }

    public List<MessageAttachment> Attachments { get; set; } = [];
}
