using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>Whom the support enters the school as — the school's choice
/// (<see cref="School.SupportAs"/>).</summary>
public enum SupportAsScope
{
    /// <summary>As an admin of the school without being one of its accounts.</summary>
    Admin,

    /// <summary>Only as one of the users the school named for the ticket
    /// (<see cref="SupportSession.SupportAsUserIds"/>), in any of their
    /// roles — not as an admin of no one.</summary>
    Users,
}

/// <summary>Who the support may be in the school: its admin, or — when the
/// school wants it so — only one of the users named on the ticket.</summary>
public static class SupportAsPolicy
{
    public static bool Allows(School school, SupportSession session, int userId) =>
        school.SupportAs == SupportAsScope.Users && session.SupportAsUserIds.Contains(userId);

    /// <summary>As the school's admin, without a user.</summary>
    public static bool AllowsAdmin(School school) => school.SupportAs == SupportAsScope.Admin;

    public const string AdminRefused = "Škola povoluje vstup jen jako uživatel vybraný u požadavku.";

    /// <summary>The users the support may be in the session, with their
    /// roles: the ticket's author first, the rest by name.</summary>
    public static async Task<List<SupportAsUserDto>> CandidatesAsync(AppDbContext db, School school, SupportSession session)
    {
        if (school.SupportAs != SupportAsScope.Users || session.SupportAsUserIds.Count == 0)
        {
            return [];
        }
        var users = await AccountsAsync(db, school.Id, session.SupportAsUserIds);
        var author = session.TicketAuthorUserId;
        return [.. users.Where(u => u.Id == author), .. users.Where(u => u.Id != author)];
    }

    /// <summary>The school's active accounts (only <paramref name="ids"/>,
    /// when given) with their roles, by name.</summary>
    public static async Task<List<SupportAsUserDto>> AccountsAsync(
        AppDbContext db, int schoolId, IReadOnlyCollection<int>? ids = null)
    {
        var query = db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.SchoolId == schoolId && u.DeactivatedAt == null && u.ErasedAt == null && !u.IsSuperAdmin);
        if (ids is not null)
        {
            query = query.Where(u => ids.Contains(u.Id));
        }
        var users = await query.Include(u => u.Person).Include(u => u.Roles).ToListAsync();
        return users
            .Where(u => u.Roles.Count > 0)
            .Select(u => new SupportAsUserDto(u.Id, PersonNames.Display(u), RoleRefDto.ListOf(u.Roles)))
            .OrderBy(u => u.Name, StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), true))
            .ToList();
    }

    /// <summary>"učitel Jan Novák" — for the banners and the log.</summary>
    public static string Label(string roleName, string name) => $"{LowerFirst(roleName)} {name}";

    /// <summary>"Učitel" → "učitel", but "ICT koordinátor" stays.</summary>
    private static string LowerFirst(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1])
            ? char.ToLower(text[0], new System.Globalization.CultureInfo("cs-CZ")) + text[1..]
            : text;

    public const string ChangedReason = "Škola změnila, za koho smí podpora vstoupit.";

    /// <summary>The school or a ticket changed whom the support may be: its
    /// sessions in the school as someone no longer allowed go back to the
    /// admin when the school allows that (their next request gets a new
    /// token, see <see cref="SupportSessionGuardMiddleware"/>), else end.
    /// Doesn't save.</summary>
    /// <param name="by">Who changed it — the SuperAdmin inside learns it.</param>
    public static async Task ApplyToSessionsAsync(AppDbContext db, School school, DateTimeOffset now, string by)
    {
        var sessions = await db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.SchoolId == school.Id && s.EndedAt == null && s.ExpiresAt > now)
            .ToListAsync();
        foreach (var session in sessions)
        {
            if (session.AsUserId is { } asUserId ? Allows(school, session, asUserId) : AllowsAdmin(school))
            {
                continue;
            }
            if (AllowsAdmin(school))
            {
                session.AsUserId = null;
                session.AsRoleId = null;
                session.AsName = null;
            }
            else
            {
                session.EndedAt = now;
                session.RefreshSecretHash = null;
                session.EndedBy = by;
                session.EndReason = ChangedReason;
            }
        }
    }
}

/// <param name="Roles">The roles the support may be in as them.</param>
public record SupportAsUserDto(int Id, string Name, IReadOnlyList<RoleRefDto> Roles);

public record SupportAsUserRefDto(int Id, string Name);

public record SetSupportAsRequest(SupportAsScope Scope);

/// <param name="UserIds">None = only as the admin.</param>
public record SetSupportAsUsersRequest(IReadOnlyList<int>? UserIds);

/// <summary>Who the support enters or switches to; no user = the school's admin.</summary>
/// <param name="RoleId">One of the user's roles; none = the one they're in, else the first.</param>
public record SupportAsRequest(int? UserId = null, int? RoleId = null);
