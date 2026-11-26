using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Support;

/// <summary>Hourly Hangfire job (registered in Program.cs) — the journal of
/// a closed or cancelled ticket (<see cref="SupportChange"/>, personal data)
/// goes once the backoffice no longer lets it be reopened
/// (<see cref="BackofficeTicket.ReopenableUntil"/>) or no longer has it, with
/// the files its sessions deleted that no row holds. While the backoffice
/// can't be reached, it stays.</summary>
public class SupportChangeCleanupJob(
    AppDbContext db, IBackofficeTickets tickets, MessageAttachmentStorageService storage, TimeProvider clock)
{
    public async Task RunAsync()
    {
        if (!tickets.Connected)
        {
            return;
        }
        var now = clock.GetUtcNow();
        var journaled = await db.SupportChanges.IgnoreQueryFilters()
            .Select(c => new { c.SchoolId, c.TicketId }).Distinct().ToListAsync();
        foreach (var ticket in journaled)
        {
            try
            {
                var current = await tickets.GetAsync(ticket.SchoolId, ticket.TicketId);
                if (current.Status is not (SupportTicketStatus.Closed or SupportTicketStatus.Cancelled)
                    || current.ReopenableUntil > now)
                {
                    continue;
                }
            }
            catch (TicketRefusedException e) when (e.Status == StatusCodes.Status404NotFound)
            {
                // Gone with its school in the backoffice.
            }
            catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
            {
                continue;
            }
            var changes = db.SupportChanges.IgnoreQueryFilters()
                .Where(c => c.SchoolId == ticket.SchoolId && c.TicketId == ticket.TicketId);
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
