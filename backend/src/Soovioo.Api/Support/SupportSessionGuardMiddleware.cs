using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>A support-mode token works only while its session runs — ended
/// by "Opustit", by the school taking the approval back, or expired — not
/// for the rest of the token's 15 minutes.</summary>
public class SupportSessionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider clock)
    {
        if (int.TryParse(context.User.FindFirst(SupportClaims.SessionId)?.Value, out var sessionId))
        {
            var now = clock.GetUtcNow();
            var running = await db.SupportSessions.IgnoreQueryFilters()
                .AnyAsync(s => s.Id == sessionId && s.EndedAt == null && s.ExpiresAt > now);
            if (!running)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }
        await next(context);
    }
}
