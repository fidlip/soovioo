namespace Soovioo.Api.Demo;

/// <summary>What a demo visitor answered in the feedback dialog. Kept after
/// the demo is deleted (it's about the product, not the visitor), but the
/// e-mail only with <see cref="Consent"/>.</summary>
public class DemoFeedback
{
    public int Id { get; set; }

    /// <summary>No foreign key on purpose — the row outlives the school.</summary>
    public int? SchoolId { get; set; }
    public required string DemoSlug { get; set; }

    /// <summary>1 (worst) – 5 (best); null when only text was sent.</summary>
    public int? Rating { get; set; }
    public string? Text { get; set; }
    public string? Email { get; set; }
    public bool Consent { get; set; }

    /// <summary>Role the visitor was looking through (Admin, Teacher…).</summary>
    public string? Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
