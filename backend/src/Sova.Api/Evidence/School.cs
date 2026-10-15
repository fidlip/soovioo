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

    /// <summary>Resortní identifikátor školy (RÚIAN/školský rejstřík) — identifies
    /// this school in the IZO item of every matrika record. Null until an admin
    /// fills it in; required before a matrika export can be generated.</summary>
    public string? Izo { get; set; }

    /// <summary>Resortní identifikátor právnické osoby — the login name for the
    /// matrika.msmt.cz/profa.msmt.cz portal and the RED_IZO item used in the
    /// anonymized "b" (podpůrná opatření) file. Null until an admin fills it in.</summary>
    public string? RedIzo { get; set; }
}
