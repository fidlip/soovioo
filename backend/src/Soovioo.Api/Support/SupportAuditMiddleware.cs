using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>Records every data-changing request (anything but GET/HEAD/
/// OPTIONS) made with a support-mode token — see <see cref="SupportSession"/>.</summary>
public class SupportAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        await next(context);

        var sessionClaim = context.User.FindFirst(SupportClaims.SessionId)?.Value;
        if (sessionClaim is null || !int.TryParse(sessionClaim, out var sessionId)
            || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            // Leaving support mode isn't a change in the school.
            || context.Request.Path.StartsWithSegments("/api/support-sessions"))
        {
            return;
        }

        db.SupportActions.Add(new SupportAction
        {
            SupportSessionId = sessionId,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            StatusCode = context.Response.StatusCode,
        });
        await db.SaveChangesAsync();
    }
}

public static class SupportClaims
{
    /// <summary>Id of the <see cref="SupportSession"/> the token belongs to.</summary>
    public const string SessionId = "supportSessionId";

    /// <summary>Username of the SuperAdmin behind a support-mode token.</summary>
    public const string By = "supportBy";

    /// <summary>The school's display name, for the "Režim podpory" banner.</summary>
    public const string SchoolName = "supportSchool";
}
