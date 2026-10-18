using Soovioo.Api.Auth;

namespace Soovioo.Api.Messaging;

/// <summary>Who takes part in a <see cref="Conversation"/> — and, per user,
/// how far they've read it (unread = a message from someone else newer than
/// <see cref="LastReadAt"/>).</summary>
public class ConversationParticipant
{
    public int ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int SchoolId { get; set; }

    public DateTimeOffset? LastReadAt { get; set; }
}
