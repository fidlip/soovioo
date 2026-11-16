using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Support;

/// <summary>Hourly Hangfire job (registered in Program.cs) — a resolved
/// ticket nobody closed or returned is closed
/// <see cref="SupportTicket.AutoCloseAfter"/> after resolving; the school's
/// admins hear of it (they may still reopen it for a while).</summary>
public class SupportTicketAutoCloseJob(AppDbContext db, Notifier notifier, TimeProvider clock)
{
    public const string AuthorName = "automaticky";

    public async Task RunAsync()
    {
        var now = clock.GetUtcNow();
        var cutoff = now - SupportTicket.AutoCloseAfter;
        var tickets = await db.SupportTickets.IgnoreQueryFilters()
            .Where(t => t.Status == SupportTicketStatus.Resolved && t.StatusChangedAt <= cutoff)
            .ToListAsync();
        foreach (var ticket in tickets)
        {
            ticket.Status = SupportTicketStatus.Closed;
            ticket.StatusChangedAt = now;
            db.SupportTicketEntries.Add(new SupportTicketEntry
            {
                TicketId = ticket.Id,
                SchoolId = ticket.SchoolId,
                At = now,
                Kind = SupportTicketEntryKind.StatusChanged,
                AuthorName = AuthorName,
                Status = SupportTicketStatus.Closed,
            });
            await db.SaveChangesAsync();
            var admins = await db.Users.IgnoreQueryFilters()
                .Where(u => u.SchoolId == ticket.SchoolId && u.Roles.Any(r => r.BuiltIn == UserRole.Admin))
                .Select(u => u.Id)
                .ToListAsync();
            await notifier.NotifyAsync(
                ticket.SchoolId, admins, NotificationType.SupportTicketUpdate,
                $"Požadavek uzavřen automaticky: {ticket.Title}", null, "admin", ticket.Id);
            await db.SaveChangesAsync();
        }
    }
}
