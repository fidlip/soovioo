namespace Soovioo.Api.ProductFeedback;

/// <summary>What a user (or a demo visitor) thinks of Soovioo. Kept after
/// the school is deleted (it's about the product, not the school), but a
/// demo visitor's e-mail only with <see cref="Consent"/>.</summary>
public class Feedback
{
    public int Id { get; set; }

    /// <summary>No foreign key on purpose — the row outlives the school.</summary>
    public int? SchoolId { get; set; }
    public string? SchoolName { get; set; }

    /// <summary>Set for feedback from a demo school.</summary>
    public string? DemoSlug { get; set; }

    /// <summary>Who sent it from a real school; no foreign key, like <see cref="SchoolId"/>.</summary>
    public int? UserId { get; set; }

    /// <summary>1 (worst) – 5 (best); null when only text was sent.</summary>
    public int? Rating { get; set; }
    public string? Text { get; set; }
    public string? Email { get; set; }

    /// <summary>May the team write back — replies need it.</summary>
    public bool Consent { get; set; }

    /// <summary>Role the user was looking through (Admin, Teacher…).</summary>
    public string? Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When a SuperAdmin first opened it; cleared by "Označit jako
    /// neviděné".</summary>
    public DateTimeOffset? SeenAt { get; set; }
    public bool Flagged { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public List<FeedbackReply> Replies { get; set; } = [];
}

/// <summary>A SuperAdmin's answer, e-mailed to <see cref="Feedback.Email"/>.</summary>
public class FeedbackReply
{
    public int Id { get; set; }
    public int FeedbackId { get; set; }
    public required string Text { get; set; }
    public required string SentBy { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
