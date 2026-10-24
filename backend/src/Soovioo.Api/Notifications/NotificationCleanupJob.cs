using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Notifications;

/// <summary>Daily Hangfire job (registered in Program.cs) — notifications
/// keep 90 days, read or not.</summary>
public class NotificationCleanupJob(AppDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public Task RunAsync()
    {
        var cutoff = clock.GetUtcNow() - Retention;
        return db.DeleteAsync(db.Notifications.IgnoreQueryFilters().Where(n => n.CreatedAt < cutoff));
    }
}
