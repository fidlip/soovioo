using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Messaging;

/// <summary>A topic with a fixed set of participants (users) and their
/// messages — anyone taking part may reply. Replaced the original "thread
/// per student" model, where only staff could write.</summary>
public class Conversation
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public required string Subject { get; set; }

    /// <summary>The pupil the conversation is about, if any — set when staff
    /// write to a pupil's family, or when a parent/pupil writes to the
    /// school (the child the parent picked). Drives the "about whom" label;
    /// access is decided by <see cref="Participants"/> only.</summary>
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Denormalized for ordering the conversation list.</summary>
    public DateTimeOffset LastMessageAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ConversationParticipant> Participants { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
}
