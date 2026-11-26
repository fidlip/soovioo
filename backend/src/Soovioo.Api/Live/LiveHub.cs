using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Support;

namespace Soovioo.Api.Live;

/// <summary>One connection per open tab for everything live: the server
/// only says what changed (<see cref="LiveEvents"/>), the page loads it
/// through the API as before. A user's tabs get their own events (by the
/// token's user, see <see cref="LivePublisher"/>), a SuperAdmin's the
/// SuperAdmins' ones, a tab in support mode its session's, and a ticket's
/// detail the ticket's (within the school) while it watches it. A tab in support mode keeps its
/// session alive (<see cref="SupportLiveness"/>).</summary>
[Authorize]
public class LiveHub(AppDbContext db, SupportLiveness liveness) : Hub
{
    public const string Path = "/api/live";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        if (SessionOf(user) is { } sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Session(sessionId));
            await liveness.ConnectedAsync(sessionId);
        }
        else if (user.IsInRole(nameof(UserRole.SuperAdmin)))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.SuperAdmins);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (SessionOf(Context.User!) is { } sessionId)
        {
            liveness.Disconnected(sessionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>The ticket's detail is open: tell it when the ticket changes.
    /// Only a ticket the caller may read — any for a SuperAdmin, the
    /// session's in support mode, the school's own for its admins.</summary>
    public async Task WatchTicket(int ticketId)
    {
        if (await SchoolOfAsync(ticketId) is { } schoolId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Ticket(schoolId, ticketId));
        }
    }

    public async Task UnwatchTicket(int ticketId)
    {
        if (await SchoolOfAsync(ticketId) is { } schoolId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Ticket(schoolId, ticketId));
        }
    }

    /// <summary>The school whose ticket the caller may watch: the session's
    /// in support mode, else the caller's school for its admins — the
    /// tickets are the backoffice's, the group is the school's.</summary>
    private async Task<int?> SchoolOfAsync(int ticketId)
    {
        var user = Context.User!;
        if (SessionOf(user) is { } sessionId)
        {
            return await db.SupportSessions.IgnoreQueryFilters()
                .Where(s => s.Id == sessionId && s.TicketId == ticketId)
                .Select(s => (int?)s.SchoolId)
                .FirstOrDefaultAsync();
        }
        // The token's permissions: a hub call has no request for the
        // per-school query filters to go by.
        return int.TryParse(user.FindFirstValue("schoolId"), out var schoolId) && HasAdministration(user)
            ? schoolId
            : null;
    }

    private static bool HasAdministration(ClaimsPrincipal user) =>
        (user.FindFirstValue("perms") ?? "").Split(',').Any(p =>
            p.Split(':') is [var name, var scope]
            && name == nameof(Permission.Administration) && scope != nameof(PermissionScope.None));

    private static int? SessionOf(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(SupportClaims.SessionId), out var id) ? id : null;
}

public static class LiveGroups
{
    public const string SuperAdmins = "superadmins";

    public static string Session(int sessionId) => $"session:{sessionId}";

    public static string Ticket(int schoolId, int ticketId) => $"ticket:{schoolId}:{ticketId}";
}

/// <summary>What the client is told — names of its handlers.</summary>
public static class LiveEvents
{
    /// <summary>The bell's, the Zprávy badge's, the linked accounts' numbers
    /// or the support in the school changed (the unread-counts request).</summary>
    public const string Counts = "counts";

    /// <summary>A SuperAdmin's "Zpětná vazba" badge.</summary>
    public const string Feedback = "feedback";

    /// <summary>A ticket (its id) or its thread changed.</summary>
    public const string Ticket = "ticket";

    /// <summary>The support session changed: ended, switched whom it's in as.</summary>
    public const string Support = "support";
}
