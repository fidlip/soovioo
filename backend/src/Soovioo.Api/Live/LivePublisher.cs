using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Support;

namespace Soovioo.Api.Live;

/// <summary>Sends <see cref="LiveEvents"/> to the open tabs. A user's events
/// go to the tabs whose token is theirs — in support mode as the user,
/// that tab too. Never fails the caller: a tab that misses an event catches
/// up when it reconnects or reloads.</summary>
public class LivePublisher(IHubContext<LiveHub> hub, ILogger<LivePublisher> logger)
{
    public Task CountsAsync(IReadOnlyCollection<int> userIds) =>
        userIds.Count == 0
            ? Task.CompletedTask
            : SendAsync(hub.Clients.Users(userIds.Select(id => id.ToString()).ToList()), LiveEvents.Counts);

    public Task TicketsAsync(int schoolId, IEnumerable<int> ticketIds) =>
        TicketsAsync(ticketIds.Select(id => (schoolId, id)));

    public async Task TicketsAsync(IEnumerable<(int SchoolId, int TicketId)> tickets)
    {
        foreach (var (schoolId, id) in tickets)
        {
            await SendAsync(hub.Clients.Group(LiveGroups.Ticket(schoolId, id)), LiveEvents.Ticket, id);
        }
    }

    /// <summary>Support sessions started, ended or switched: their tabs,
    /// their tickets' details and the banners (<see cref="SupportPresence"/>)
    /// of whom they are in as — and were, before <paramref name="sessions"/>
    /// switched — and of the tickets' authors.</summary>
    public async Task SessionsAsync(
        AppDbContext db, IReadOnlyCollection<(SupportSession Session, int? AsUserBefore)> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }
        var users = sessions
            .SelectMany(s => new[] { s.Session.AsUserId, s.AsUserBefore, s.Session.TicketAuthorUserId })
            .OfType<int>().ToHashSet();
        var tickets = sessions.Where(s => s.Session.TicketId is not null)
            .Select(s => (s.Session.SchoolId, s.Session.TicketId!.Value)).Distinct().ToList();
        await CountsAsync(users);
        await TicketsAsync(tickets);
        await SendAsync(
            hub.Clients.Groups(sessions.Select(s => LiveGroups.Session(s.Session.Id)).Distinct().ToList()),
            LiveEvents.Support);
    }

    private async Task SendAsync(IClientProxy clients, string method, params object?[] args)
    {
        try
        {
            await clients.SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live event {Method} not sent", method);
        }
    }
}
