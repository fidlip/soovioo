namespace Soovioo.Api.Support;

public enum SupportAccessKeyStatus
{
    /// <summary>The support asks; the school's admin decides.</summary>
    Pending,
    Approved,
    Rejected,

    /// <summary>The school took it back; entering with it stops at once.</summary>
    Revoked,

    /// <summary>The ticket ended: the key is no use any more.</summary>
    Expired,
}

/// <summary>A ticket's public key the support asked to enter the school with, as SSH's
/// <c>authorized_keys</c>: the backoffice keeps the private key, the school approves the public
/// one (till when, as whom) and may revoke it any time. Without an approved key nobody gets in.</summary>
public class SupportAccessKey
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    /// <summary>The backoffice's ticket.</summary>
    public int TicketId { get; set; }

    public required string TicketTitle { get; set; }

    /// <summary>The ticket's author in the school, if a user of it.</summary>
    public int? TicketAuthorUserId { get; set; }

    /// <summary>SubjectPublicKeyInfo (P-256).</summary>
    public required byte[] PublicKey { get; set; }

    /// <summary><c>SHA256:…</c>, as the backoffice shows it.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>The backoffice's operator who asked; their name shows in the banner and Logy.</summary>
    public int OperatorId { get; set; }

    public required string OperatorName { get; set; }

    /// <summary>Whom the operator asked to be; empty: the school chooses.</summary>
    public List<int> RequestedAsUserIds { get; set; } = [];

    public string? Reason { get; set; }
    public SupportAccessKeyStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public int? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }

    /// <summary>Approved: entering works till then.</summary>
    public DateTimeOffset? ApprovedUntil { get; set; }

    /// <summary>Whom the school lets the support be (<see cref="SupportAsScope.Users"/>);
    /// empty: the school's admin (<see cref="SupportAsScope.Admin"/>).</summary>
    public List<int> ApprovedAsUserIds { get; set; } = [];

    public string? DecisionNote { get; set; }

    public bool Usable(DateTimeOffset now) => Status == SupportAccessKeyStatus.Approved && ApprovedUntil > now;
}

/// <summary>A one-time challenge for entering with a key (<c>SupportChallenge</c>): random,
/// valid <see cref="SupportKeys.ChallengeLifetime"/>, deleted when used.</summary>
public class SupportEntryChallenge
{
    public int Id { get; set; }
    public int KeyId { get; set; }
    public SupportAccessKey Key { get; set; } = null!;

    /// <summary>SHA-256 of the challenge.</summary>
    public required string Hash { get; set; }

    /// <summary>Null: the school's admin.</summary>
    public int? AsUserId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
