using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

/// <summary>An account linked with the owner's accounts at other schools:
/// those with the same <see cref="GroupId"/> switch between each other
/// without a password and share the owner's phones. Not tenant-scoped and
/// never shown to a school — only the owner sees it.</summary>
public class AccountLink
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public Guid GroupId { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>SHA-256 of the one-time code that logs into this account,
    /// issued to a linked account switching here.</summary>
    public string? SwitchCodeHash { get; set; }
    public DateTimeOffset? SwitchCodeExpiresAt { get; set; }

    /// <summary>The session switching here is a "Zůstat přihlášen" one.</summary>
    public bool SwitchPersistent { get; set; }
}

/// <summary>Linking on the web, under way: the account that started it goes
/// to the other school's login page and comes back. Only the same account can
/// finish it, so a code sent to somebody else links nothing.</summary>
public class AccountLinkRequest
{
    public int Id { get; set; }

    /// <summary>SHA-256 of the code the browser carries to the other school.</summary>
    public required string CodeHash { get; set; }

    public int InitiatorUserId { get; set; }
    public User? InitiatorUser { get; set; }

    public int TargetSchoolId { get; set; }
    public School? TargetSchool { get; set; }

    /// <summary>The account that said yes at that school; null until then.</summary>
    public int? ApprovedUserId { get; set; }

    /// <summary>SHA-256 of the code the browser that said yes carries back;
    /// nobody else has it, not even whoever started the request.</summary>
    public string? CompletionCodeHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <param name="Unread">Unread notifications under that account's bell.</param>
public record LinkedAccountDto(int UserId, string SchoolName, string Username, int Unread);

public static class AccountLinks
{
    /// <summary>Each of the accounts with those linked to it, itself first.</summary>
    public static async Task<Dictionary<int, List<int>>> LinkedWithAsync(this AppDbContext db, IReadOnlyCollection<int> userIds)
    {
        var groups = await db.AccountLinks
            .Where(l => userIds.Contains(l.UserId))
            .Select(l => new { l.UserId, l.GroupId })
            .ToListAsync();
        var groupIds = groups.Select(g => g.GroupId).Distinct().ToList();
        var members = (await db.AccountLinks
                .Where(l => groupIds.Contains(l.GroupId))
                .Select(l => new { l.UserId, l.GroupId })
                .ToListAsync())
            .ToLookup(l => l.GroupId, l => l.UserId);
        var groupOf = groups.ToDictionary(g => g.UserId, g => g.GroupId);
        return userIds.Distinct().ToDictionary(
            id => id,
            id => groupOf.TryGetValue(id, out var group)
                ? [id, .. members[group].Where(other => other != id)]
                : new List<int> { id });
    }

    /// <summary>The accounts linked to this one; null when there are none.</summary>
    public static async Task<List<LinkedAccountDto>?> LinkedAccountsOfAsync(this AppDbContext db, int userId)
    {
        var group = await db.AccountLinks
            .Where(l => l.UserId == userId)
            .Select(l => (Guid?)l.GroupId)
            .FirstOrDefaultAsync();
        if (group is null)
        {
            return null;
        }
        var others = await db.AccountLinks.IgnoreQueryFilters()
            .Where(l => l.GroupId == group && l.UserId != userId)
            .OrderBy(l => l.User!.School!.Name)
            .Select(l => new LinkedAccountDto(
                l.UserId, l.User!.School!.Name, l.User.Username,
                db.Notifications.IgnoreQueryFilters().Count(n => n.UserId == l.UserId && n.InApp && n.ReadAt == null)))
            .ToListAsync();
        return others.Count == 0 ? null : others;
    }

    /// <summary>Takes the account out of its links; a lone account left
    /// behind isn't linked to anything either.</summary>
    public static async Task UnlinkAccountAsync(this AppDbContext db, int userId)
    {
        var link = await db.AccountLinks.FirstOrDefaultAsync(l => l.UserId == userId);
        if (link is null)
        {
            return;
        }
        db.AccountLinks.Remove(link);
        var rest = await db.AccountLinks.Where(l => l.GroupId == link.GroupId && l.UserId != userId).ToListAsync();
        if (rest.Count == 1)
        {
            db.AccountLinks.Remove(rest[0]);
        }
        await db.SaveChangesAsync();
    }
}
