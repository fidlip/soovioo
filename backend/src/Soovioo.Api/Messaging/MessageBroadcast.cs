using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Messaging;

/// <summary>One bulk send (to a class or the whole school) — the fan-out
/// conversations point back here, so the sender sees the broadcast once in
/// their list instead of one thread per family. Recipients see their own
/// conversation as usual.</summary>
public class MessageBroadcast
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int? SenderUserId { get; set; }
    public User? SenderUser { get; set; }

    /// <summary>Snapshot at send time, like <see cref="Message.SenderName"/>.</summary>
    public required string SenderName { get; set; }

    public required string Subject { get; set; }
    public required string Body { get; set; }

    /// <summary>Who it went to, for display ("3.A", "celá škola").</summary>
    public required string AudienceLabel { get; set; }

    /// <summary>Sent to the pupils' own accounts only, without parents.</summary>
    public bool StudentsOnly { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Conversation> Conversations { get; set; } = [];
}
