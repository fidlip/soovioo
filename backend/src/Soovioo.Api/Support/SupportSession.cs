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

    /// <summary>Who is inside: the operator's name from the key's request (snapshot).</summary>
    public required string SuperAdminUsername { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>The support ticket (the backoffice's id) it was entered for
    /// (null only for sessions from before tickets).</summary>
    public int? TicketId { get; set; }

    /// <summary>The ticket's title when it was entered — for the banners.</summary>
    public string? TicketTitle { get; set; }

    /// <summary>The ticket's author (the school's user) — they see the
    /// support in the school as someone else for their ticket.</summary>
    public int? TicketAuthorUserId { get; set; }

    /// <summary>Whom the school let the support be for the ticket
    /// (<see cref="SupportAsScope.Users"/>).</summary>
    public List<int> SupportAsUserIds { get; set; } = [];

    /// <summary>Entered as one of the school's users ("Vstoupit jako…"),
    /// in one of their roles — null = as the school's admin. Changes when
    /// the SuperAdmin switches within the session.</summary>
    public int? AsUserId { get; set; }
    public int? AsRoleId { get; set; }

    /// <summary>Snapshot, "učitel Jan Novák".</summary>
    public string? AsName { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The support session can't outlive this (2 h from start).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The key the school approved that it was entered with.</summary>
    public int? AccessKeyId { get; set; }

    /// <summary>The backoffice's operator inside (their name is <see cref="SuperAdminUsername"/>).</summary>
    public int? OperatorId { get; set; }

    /// <summary>SHA-256 of the secret in the <c>soovioo_support</c> cookie set
    /// on entering — lets a reload of the school's page get a fresh support token.</summary>
    public string? RefreshSecretHash { get; set; }

    /// <summary>The last request made with it (to the minute, see
    /// <see cref="SupportSessionGuardMiddleware"/>).</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>No tab of the session is connected since then (none yet:
    /// since the start) — the ticket shows it as inactive, and it ends after
    /// a while (<see cref="SupportLiveness"/>). Null while one is.</summary>
    public DateTimeOffset? DisconnectedAt { get; set; }

    /// <summary>Set by "Opustit"; null = left by letting the token expire.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Ended by someone else (the school, another SuperAdmin) — who.
    /// The SuperAdmin inside learns it with <see cref="EndReason"/>
    /// (<see cref="SupportSessionGuardMiddleware"/>).</summary>
    public string? EndedBy { get; set; }

    /// <summary>Markdown.</summary>
    public string? EndReason { get; set; }

    /// <summary>What was done — the SuperAdmin may write it on "Opustit".</summary>
    public string? Result { get; set; }
}
