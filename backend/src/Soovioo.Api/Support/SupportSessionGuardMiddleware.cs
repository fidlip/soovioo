using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>A support-mode token works only while its session runs — ended
/// by "Opustit", by the school taking the approval back, or expired — not
/// for the rest of the token's 15 minutes. Ended by someone else, the 401
/// says who and why (<see cref="SupportSessionEndedDto"/>). Each request marks the session
/// as seen (<see cref="SupportSession.LastSeenAt"/>, once a minute at most)
/// and what it saves goes to the session's journal (<see cref="SupportJournal"/>).</summary>
public class SupportSessionGuardMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider clock)
    {
        if (int.TryParse(context.User.FindFirst(SupportClaims.SessionId)?.Value, out var sessionId))
        {
            var now = clock.GetUtcNow();
            var session = await db.SupportSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is null || session.EndedAt is not null || session.ExpiresAt <= now)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                if (session is { EndedAt: { } at, EndedBy: { } by })
                {
                    await context.Response.WriteAsJsonAsync(
                        new { SupportEnded = new SupportSessionEndedDto(at, by, session.EndReason) });
                }
                return;
            }
            // Checking whether it still runs isn't working in it.
            if (!(session.LastSeenAt > now - SeenEvery) && context.Request.Path != SupportController.CurrentPath)
            {
                session.LastSeenAt = now;
                await db.SaveChangesAsync();
            }
            db.SupportJournal = SupportJournalScope.Of(session, clock);
        }
        await next(context);
    }
}

/// <param name="Reason">Markdown.</param>
public record SupportSessionEndedDto(DateTimeOffset At, string By, string? Reason);
