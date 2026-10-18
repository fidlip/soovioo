using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

public record IssuedRefreshToken(string Token, bool Persistent, DateTimeOffset ExpiresAt);

public record RotatedRefreshToken(User User, IssuedRefreshToken Successor);

/// <summary>Issues, rotates and revokes <see cref="RefreshToken"/>s. Every
/// query uses IgnoreQueryFilters(): refresh/logout run unauthenticated, with
/// no school context for the per-school User filter to key on.</summary>
public class RefreshTokenService(AppDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>Two tabs refreshing at once both present the same cookie;
    /// the loser of that race shouldn't be treated as token theft.</summary>
    public static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>Starts a new family (= a new login).</summary>
    public async Task<IssuedRefreshToken> IssueAsync(User user, bool persistent)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now + (persistent ? PersistentLifetime : SessionLifetime);
        var (token, _) = Add(user.Id, Guid.NewGuid(), persistent, now, expiresAt);
        await db.SaveChangesAsync();
        return token;
    }

    /// <summary>Null when the token is unknown, expired, revoked or its user
    /// is gone — the caller answers 401 and drops the cookie.</summary>
    public async Task<RotatedRefreshToken?> RotateAsync(string rawToken)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(rawToken);
        var current = await db.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (current?.User is null || current.ExpiresAt <= now)
        {
            return null;
        }

        if (current.RevokedAt is { } revokedAt)
        {
            var isConcurrentRotation = current.ReplacedByTokenId is not null
                && now - revokedAt <= ReuseGracePeriod
                && !await IsFamilyRevokedAsync(current.FamilyId);
            if (!isConcurrentRotation)
            {
                await RevokeFamilyAsync(current.FamilyId, now);
                return null;
            }
        }

        // Persistent sessions slide; browser-session ones keep the absolute
        // 8-hour cap set at login.
        var expiresAt = current.Persistent ? now + PersistentLifetime : current.ExpiresAt;
        var (successor, entity) = Add(current.UserId, current.FamilyId, current.Persistent, now, expiresAt);
        await db.SaveChangesAsync();

        if (current.RevokedAt is null)
        {
            current.RevokedAt = now;
            current.ReplacedByTokenId = entity.Id;
            await db.SaveChangesAsync();
        }

        return new RotatedRefreshToken(current.User, successor);
    }

    /// <summary>Logout — ends this login only (its whole family).</summary>
    public async Task RevokeAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        var token = await db.Set<RefreshToken>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow());
        }
    }

    /// <summary>Password change/reset or invite acceptance — signs the user
    /// out everywhere.</summary>
    public async Task RevokeAllForUserAsync(int userId)
    {
        var now = clock.GetUtcNow();
        var active = await db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Persistence flag of the login that <paramref name="rawToken"/>
    /// belongs to, or null if it isn't a live token.</summary>
    public async Task<bool?> FindPersistentAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        var now = clock.GetUtcNow();
        var token = await db.Set<RefreshToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > now);
        return token?.Persistent;
    }

    /// <summary>Daily Hangfire cleanup. Revoked rows are kept for a while
    /// past expiry so reuse detection still recognises a stolen copy.</summary>
    public async Task DeleteExpiredAsync()
    {
        var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(1);
        await db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();
    }

    private (IssuedRefreshToken, RefreshToken) Add(
        int userId, Guid familyId, bool persistent, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            Persistent = persistent,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        db.Set<RefreshToken>().Add(entity);
        return (new IssuedRefreshToken(raw, persistent, expiresAt), entity);
    }

    private async Task<bool> IsFamilyRevokedAsync(Guid familyId) =>
        !await db.Set<RefreshToken>().IgnoreQueryFilters()
            .AnyAsync(t => t.FamilyId == familyId && t.RevokedAt == null);

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now)
    {
        var active = await db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
        await db.SaveChangesAsync();
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
