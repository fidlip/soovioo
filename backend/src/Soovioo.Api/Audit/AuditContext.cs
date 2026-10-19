namespace Soovioo.Api.Audit;

/// <summary>What an endpoint knows about its own audit entry that the
/// token doesn't say — anonymous endpoints (login, logout) name the user
/// here, and may replace the generic description.</summary>
public class AuditContext
{
    private static readonly object Key = new();

    public int? UserId { get; set; }
    public int? SchoolId { get; set; }
    public string? Username { get; set; }
    public string? Description { get; set; }

    /// <summary>Entering a school in support mode happens before (or
    /// without) a support token — the endpoint names the session here.</summary>
    public int? SupportSessionId { get; set; }
    public string? SupportBy { get; set; }

    /// <summary>The current request's context (created on first use).</summary>
    public static AuditContext Of(HttpContext? http)
    {
        if (http is null)
        {
            return new AuditContext();
        }
        if (http.Items[Key] is not AuditContext context)
        {
            context = new AuditContext();
            http.Items[Key] = context;
        }
        return context;
    }

    public static AuditContext? Find(HttpContext http) => http.Items[Key] as AuditContext;
}
