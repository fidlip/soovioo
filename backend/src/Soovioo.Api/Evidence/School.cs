namespace Soovioo.Api.Evidence;

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

    public SchoolType Type { get; set; } = SchoolType.Elementary;

    /// <summary>Resortní identifikátor školy (RÚIAN/školský rejstřík) — identifies
    /// this school in the IZO item of every matrika record. Null until an admin
    /// fills it in; required before a matrika export can be generated.</summary>
    public string? Izo { get; set; }

    /// <summary>Resortní identifikátor právnické osoby — the login name for the
    /// matrika.msmt.cz/profa.msmt.cz portal and the RED_IZO item used in the
    /// anonymized "b" (podpůrná opatření) file. Null until an admin fills it in.</summary>
    public string? RedIzo { get; set; }

    // Institutional address/contact — not transmitted to matrika itself, but
    // needed on real documents (report card letterhead, official
    // correspondence) and for the school's own record-keeping.
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Ředitel/ředitelka — appears on official documents.</summary>
    public string? DirectorName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Used to build the SPAYD QR string for payments (see
    /// <c>Payments</c>) — the account recipients pay into. Null until an
    /// admin fills it in; without it, the frontend simply skips rendering a
    /// QR code.</summary>
    public string? BankAccountNumber { get; set; }
}
