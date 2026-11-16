using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>A SuperAdmin in the school as one of its users, for the banners:
/// the user they are in as, and the author of the ticket they came for, see
/// it while the session runs.</summary>
public static class SupportPresence
{
    /// <summary>Null outside a school, in support mode and without any.</summary>
    public static async Task<IReadOnlyList<SupportPresenceDto>?> OfAsync(AppDbContext db, ClaimsPrincipal user, DateTimeOffset now)
    {
        if (user.InSupportMode() || !int.TryParse(user.FindFirstValue("schoolId"), out var schoolId)
            || user.ActorId() is not { } me)
        {
            return null;
        }
        var sessions = await db.SupportSessions.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.SuperAdminUser).ThenInclude(u => u!.Person)
            .Include(s => s.Ticket)
            .Where(s => s.SchoolId == schoolId && s.EndedAt == null && s.ExpiresAt > now && s.AsUserId != null
                        && s.HandoffCodeHash == null
                        && (s.AsUserId == me
                            || (s.Ticket != null && !s.Ticket.CreatedBySupport && s.Ticket.CreatedByUserId == me)))
            .OrderBy(s => s.StartedAt)
            .ToListAsync();
        return sessions.Count == 0
            ? null
            : sessions.Select(s =>
            {
                var asMe = s.AsUserId == me;
                return new SupportPresenceDto(
                    s.Id, asMe, s.SuperAdminUser is { } u ? PersonNames.Display(u) : s.SuperAdminUsername,
                    s.AsName ?? "", s.StartedAt, asMe ? null : s.TicketId, asMe ? null : s.Ticket?.Title);
            }).ToList();
    }
}

/// <param name="AsMe">In as the caller; else in as someone else for the
/// caller's ticket (<paramref name="TicketId"/>, <paramref name="TicketTitle"/>).</param>
/// <param name="As">"učitel Jan Novák".</param>
public record SupportPresenceDto(
    int SessionId, bool AsMe, string SuperAdminName, string As, DateTimeOffset StartedAt, int? TicketId,
    string? TicketTitle);
