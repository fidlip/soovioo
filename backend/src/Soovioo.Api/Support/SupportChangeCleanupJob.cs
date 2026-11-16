using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Support;

/// <summary>Hourly Hangfire job (registered in Program.cs) — the journal of
/// a closed or cancelled ticket (<see cref="SupportChange"/>, personal data)
/// goes once the ticket can't be reopened
/// (<see cref="SupportTicket.ReopenableUntil"/>), with the files its sessions
/// deleted that no row holds.</summary>
public class SupportChangeCleanupJob(AppDbContext db, MessageAttachmentStorageService storage, TimeProvider clock)
{
    public async Task RunAsync()
    {
        var now = clock.GetUtcNow();
        var journaled = await db.SupportChanges.IgnoreQueryFilters().Select(c => c.TicketId).Distinct().ToListAsync();
        var tickets = await db.SupportTickets.IgnoreQueryFilters()
            .Where(t => journaled.Contains(t.Id)
                        && (t.Status == SupportTicketStatus.Closed || t.Status == SupportTicketStatus.Cancelled))
            .ToListAsync();
        foreach (var ticket in tickets.Where(t => t.ReopenableUntil <= now))
        {
            var changes = db.SupportChanges.IgnoreQueryFilters().Where(c => c.TicketId == ticket.Id);
            var files = await changes.Where(c => c.Operation == SupportChangeOperation.FileDeleted)
                .Select(c => c.KeyJson).Distinct().ToListAsync();
            foreach (var path in files)
            {
                if (!await SupportChangeLabels.IsHeldAsync(db, path))
                {
                    storage.Delete(path);
                }
            }
            await db.DeleteAsync(changes);
        }
    }
}
