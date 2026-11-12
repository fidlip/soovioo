namespace Soovioo.Api.Audit;

/// <summary>One audited request — every data-changing call (anything but
/// GET/HEAD/OPTIONS) plus logins and logouts, written by
/// <see cref="AuditMiddleware"/>. Who/where are snapshots without foreign
/// keys, so the log outlives deleted accounts, classes and schools. Not
/// tenant-filtered: <see cref="AuditLogController"/> scopes by
/// <see cref="SchoolId"/> explicitly. Kept for a year
/// (<see cref="AuditLogCleanupJob"/>).</summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null for the SuperAdmin area and anonymous requests that
    /// named no school.</summary>
    public int? SchoolId { get; set; }

    public int? UserId { get; set; }

    /// <summary>For a failed login the username that was tried.</summary>
    public string? Username { get; set; }
    public string? UserDisplayName { get; set; }

    /// <summary>Active role of the token.</summary>
    public string? Role { get; set; }

    /// <summary>Set when made in support mode — <see cref="UserId"/> is then
    /// the SuperAdmin.</summary>
    public int? SupportSessionId { get; set; }
    public string? SupportBy { get; set; }

    public required string Method { get; set; }
    public required string Path { get; set; }

    /// <summary>Agenda label ("Známky", "Uživatelé"…), see <see cref="AuditAreaAttribute"/>.</summary>
    public required string Area { get; set; }

    /// <summary>What was done, without whom/what to ("Vytvořen uživatel",
    /// "Neúspěšné přihlášení"); "METHOD /route/{template}" for an action
    /// without a description — the values of the "Akce" filter.</summary>
    public string? Action { get; set; }

    /// <summary>Human-readable summary ("Vytvořen uživatel Karel Nový");
    /// null when the action has none — the UI then shows method + path.</summary>
    public string? Description { get; set; }

    public int StatusCode { get; set; }

    /// <summary>Request payload with passwords/tokens masked, truncated.</summary>
    public string? RequestBody { get; set; }

    /// <summary>Not the response body — the error message for a failure,
    /// the new record's id for a creation.</summary>
    public string? Result { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>The class the change concerns, when the request names it or
    /// a student of it — for the class filter.</summary>
    public int? SchoolClassId { get; set; }
}
