using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>A support-mode token works only while its session runs — ended
/// by "Opustit", by the school taking the approval back, or expired — not
/// for the rest of the token's 15 minutes. Each request marks the session
/// as seen (<see cref="SupportSession.LastSeenAt"/>, once a minute at most).</summary>
public class SupportSessionGuardMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider clock)
    {
        if (int.TryParse(context.User.FindFirst(SupportClaims.SessionId)?.Value, out var sessionId))
        {
            var now = clock.GetUtcNow();
            var session = await db.SupportSessions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.EndedAt == null && s.ExpiresAt > now);
            if (session is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!(session.LastSeenAt > now - SeenEvery))
            {
                session.LastSeenAt = now;
                await db.SaveChangesAsync();
            }
        }
        await next(context);
    }
}
