using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>Whether a support session's tab is open: connected to the live
/// hub (<see cref="Live.LiveHub"/>) — a tab closed without "Opustit" shows
/// on the ticket as inactive since then (<see cref="SupportSession.DisconnectedAt"/>)
/// and ends a while later (<see cref="SupportSessionIdleJob"/>). A reload
/// or a short outage reconnects before it counts. Connections are counted
/// in this instance's memory.</summary>
public class SupportLiveness(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SupportLiveness> logger)
{
    /// <summary>A reload or a dropped connection reconnects within this.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    private readonly Dictionary<int, int> connections = [];
    private readonly SemaphoreSlim writing = new(1, 1);

    public async Task ConnectedAsync(int sessionId)
    {
        await writing.WaitAsync();
        try
        {
            lock (connections)
            {
                connections[sessionId] = connections.GetValueOrDefault(sessionId) + 1;
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.SupportSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is { DisconnectedAt: not null })
            {
                session.DisconnectedAt = null;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Support session {SessionId} not marked connected", sessionId);
        }
        finally
        {
            writing.Release();
        }
    }

    public void Disconnected(int sessionId)
    {
        var at = clock.GetUtcNow();
        lock (connections)
        {
            var left = connections.GetValueOrDefault(sessionId) - 1;
            if (left > 0)
            {
                connections[sessionId] = left;
                return;
            }
            connections.Remove(sessionId);
        }
        _ = MarkDisconnectedAsync(sessionId, at);
    }

    private async Task MarkDisconnectedAsync(int sessionId, DateTimeOffset at)
    {
        await Task.Delay(Settle, clock);
        await writing.WaitAsync();
        try
        {
            lock (connections)
            {
                if (connections.ContainsKey(sessionId))
                {
                    return;
                }
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.SupportSessions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session is { DisconnectedAt: null, EndedAt: null } && session.ExpiresAt > at)
            {
                session.DisconnectedAt = at;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Support session {SessionId} not marked disconnected", sessionId);
        }
        finally
        {
            writing.Release();
        }
    }

    /// <summary>On start nothing is connected yet: the running sessions count
    /// as disconnected until their tabs reconnect.</summary>
    public static void MarkAllDisconnected(AppDbContext db, DateTimeOffset now)
    {
        var running = db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.EndedAt == null && s.ExpiresAt > now && s.DisconnectedAt == null)
            .ToList();
        foreach (var session in running)
        {
            session.DisconnectedAt = now;
        }
        if (running.Count > 0)
        {
            db.SaveChanges();
        }
    }
}

/// <summary>Ends the support sessions no tab has been connected to, nor
/// made a request with, for <see cref="EndAfter"/>: they expire as of when
/// they went quiet. Runs every minute — and tells about the sessions that
/// just ran out.</summary>
public class SupportSessionIdleJob(AppDbContext db, Live.LivePublisher live, TimeProvider clock)
{
    public static readonly TimeSpan EndAfter = TimeSpan.FromMinutes(15);

    public async Task RunAsync()
    {
        var now = clock.GetUtcNow();
        var quietSince = now - EndAfter;
        var idle = await db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.EndedAt == null && s.ExpiresAt > now
                        && s.DisconnectedAt != null && s.DisconnectedAt <= quietSince
                        && (s.LastSeenAt ?? s.StartedAt) <= quietSince)
            .ToListAsync();
        foreach (var session in idle)
        {
            var lastSeen = session.LastSeenAt ?? session.StartedAt;
            session.ExpiresAt = session.DisconnectedAt > lastSeen ? session.DisconnectedAt.Value : lastSeen;
        }
        if (idle.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        // Run out on their own since the last run (the runs may be late):
        // nothing is saved, the tabs and the banners are told here.
        var expired = await db.SupportSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.EndedAt == null && s.ExpiresAt <= now && s.ExpiresAt > now - ExpiredWindow)
            .ToListAsync();
        await live.SessionsAsync(db, expired.Select(s => (s, (int?)null)).ToList());
    }

    private static readonly TimeSpan ExpiredWindow = TimeSpan.FromMinutes(2);
}
