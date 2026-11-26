using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Data;
using Soovioo.Api.Live;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Support;

/// <summary>The backoffice's <c>TicketChanged</c>: the support did something
/// on a ticket the school sees. Its admins hear of it (the bell, the e-mail)
/// and an open detail of it reloads.</summary>
public class SupportTicketChanges(
    AppDbContext db, IBackofficeTickets tickets, Notifier notifier, LivePublisher live,
    ILogger<SupportTicketChanges> logger, SupportKeys? keys = null)
{
    public const string Command = "TicketChanged";

    /// <summary>What changed, as the backoffice names it.</summary>
    public static class Changes
    {
        public const string Created = "Created";
        public const string Message = "Message";
        public const string MessageChanged = "MessageChanged";
        public const string StatusChanged = "StatusChanged";
        public const string Assigned = "Assigned";
        public const string AccessRequested = "AccessRequested";
        public const string Time = "Time";
        public const string Estimate = "Estimate";
    }

    private sealed record Payload(int TicketId, int SchoolId, string Change, SupportTicketStatus? Status, int? EntryId);

    public async Task<CommandAnswer> HandleAsync(JsonElement payload)
    {
        var change = payload.Deserialize<Payload>(BackofficeTickets.Json)
                     ?? throw new JsonException("No payload.");
        if (!await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Id == change.SchoolId))
        {
            return CommandAnswer.Refused($"School {change.SchoolId} doesn't exist.");
        }
        if (change.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed or SupportTicketStatus.Cancelled
            && keys is not null)
        {
            await keys.ExpireAsync(change.SchoolId, change.TicketId);
            await db.SaveChangesAsync();
        }
        await live.TicketsAsync(change.SchoolId, [change.TicketId]);
        if (change.Change is Changes.Time)
        {
            return CommandAnswer.Ok(new { change.TicketId });
        }

        BackofficeTicket? ticket = null;
        try
        {
            ticket = await tickets.GetAsync(change.SchoolId, change.TicketId);
        }
        catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
        {
            // Told without the details rather than not at all.
            logger.LogWarning(e, "Ticket {TicketId} changed, but the backoffice didn't give it.", change.TicketId);
        }
        var title = ticket?.Title ?? $"požadavek č. {change.TicketId}";
        var entry = change.EntryId is { } entryId ? ticket?.Entries.FirstOrDefault(e => e.Id == entryId) : null;
        var (heading, body) = change.Change switch
        {
            Changes.Created => ($"Podpora založila požadavek: {title}", ticket?.Description),
            Changes.Message or Changes.MessageChanged => ($"Zpráva od podpory: {title}", entry?.Text),
            Changes.StatusChanged => ($"{StatusTitle(change.Status ?? ticket?.Status)}: {title}", entry?.Text),
            Changes.Assigned => ($"Požadavek převzal(a) {ticket?.AssigneeName ?? "podpora"}: {title}", null),
            Changes.AccessRequested => ($"Podpora žádá o vstup do školy: {title}", null),
            Changes.Estimate => ($"Podpora poslala odhad práce: {title}", entry?.Text),
            _ => ($"Změna požadavku: {title}", null),
        };
        var admins = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == change.SchoolId && u.DeactivatedAt == null && u.ErasedAt == null
                        && u.Roles.Any(r => r.BuiltIn == UserRole.Admin))
            .Select(u => u.Id)
            .ToListAsync();
        await notifier.NotifyAsync(
            change.SchoolId, admins, NotificationType.SupportTicketUpdate, heading,
            string.IsNullOrWhiteSpace(body) ? null : Notifier.Preview(body), "admin", change.TicketId);
        await db.SaveChangesAsync();
        return CommandAnswer.Ok(new { change.TicketId });
    }

    private static string StatusTitle(SupportTicketStatus? status) => status switch
    {
        SupportTicketStatus.Assigned => "Požadavek je přiřazený",
        SupportTicketStatus.InProgress => "Požadavek se řeší",
        SupportTicketStatus.WaitingForSchool => "Podpora čeká na vaši odpověď",
        SupportTicketStatus.Resolved => "Požadavek vyřešen",
        SupportTicketStatus.Closed => "Požadavek uzavřen",
        SupportTicketStatus.Cancelled => "Požadavek zrušen",
        _ => "Změněn stav požadavku",
    };
}
