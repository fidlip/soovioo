namespace Soovioo.Api.Demo;

/// <summary>The visitor's e-mail left in the demo wizard or the feedback
/// dialog: the demo's link goes there, and a reminder 7 days and 1 day
/// before the demo is deleted. Outlives the demo only with
/// <see cref="Consent"/> to be contacted (GDPR) — <see cref="SchoolId"/> is
/// then cleared, the slug stays as a reference.</summary>
public class DemoContact
{
    public int Id { get; set; }

    /// <summary>No foreign key on purpose — the row may outlive the school.</summary>
    public int? SchoolId { get; set; }
    public required string DemoSlug { get; set; }
    public required string Email { get; set; }
    public bool Consent { get; set; }

    /// <summary>"wizard" or "feedback".</summary>
    public required string Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Which expiry reminders already went out (7, then 1 day before).</summary>
    public bool Warned7Days { get; set; }
    public bool Warned1Day { get; set; }
}
