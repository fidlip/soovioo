using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Audit;

/// <summary>Daily Hangfire job (registered in Program.cs) — the audit log
/// keeps one year.</summary>
public class AuditLogCleanupJob(AppDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);

    public async Task<int> RunAsync()
    {
        var cutoff = clock.GetUtcNow() - Retention;
        // Daily, so only about a day's worth of rows at a time.
        var expired = await db.AuditLogEntries.Where(e => e.At < cutoff).ToListAsync();
        db.AuditLogEntries.RemoveRange(expired);
        await db.SaveChangesAsync();
        return expired.Count;
    }
}
