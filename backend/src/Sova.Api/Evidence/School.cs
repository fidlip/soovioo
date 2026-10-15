namespace Sova.Api.Evidence;

/// <summary>The tenant root. Every other entity belongs to exactly one School
/// (except SuperAdmin users, whose SchoolId is null).</summary>
public class School
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>URL-safe identifier, used to resolve the tenant at login
    /// (and later from a subdomain). Unique.</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
