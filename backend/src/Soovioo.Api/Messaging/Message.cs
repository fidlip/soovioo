using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Messaging;

/// <summary>One post in a <see cref="Conversation"/>. Immutable once sent
/// (like an e-mail) — only its sender may delete it.</summary>
public class Message
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    /// <summary>Null only for messages migrated from the old per-student
    /// threads whose sender name matched no account.</summary>
    public int? SenderUserId { get; set; }
    public User? SenderUser { get; set; }

    /// <summary>Snapshot of the sender's name at send time.</summary>
    public required string SenderName { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MessageAttachment> Attachments { get; set; } = [];
}
