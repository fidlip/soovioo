using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

/// <summary>A SuperAdmin entering a school in support mode ("Vstoupit do
/// školy") — who, where and when. What was changed meanwhile is in the
/// audit log (<see cref="Audit.AuditLogEntry.SupportSessionId"/>). Not tenant-scoped: only SuperAdmins
/// ever read it.</summary>
public class SupportSession
{
    public int Id { get; set; }

    public int? SuperAdminUserId { get; set; }
    public User? SuperAdminUser { get; set; }

    /// <summary>Snapshot, survives the account's deletion.</summary>
    public required string SuperAdminUsername { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The support session can't outlive this (2 h from start).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>SHA-256 of the one-time code that carries the session over to
    /// the school's page (its own subdomain in production) — redeemable once,
    /// within <see cref="HandoffExpiresAt"/>.</summary>
    public string? HandoffCodeHash { get; set; }
    public DateTimeOffset? HandoffExpiresAt { get; set; }

    /// <summary>SHA-256 of the secret in the <c>soovioo_support</c> cookie set
    /// when the code is redeemed — lets a reload of the school's page get a
    /// fresh support token. Null until redeemed.</summary>
    public string? RefreshSecretHash { get; set; }

    /// <summary>Set by "Opustit"; null = left by letting the token expire.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}
