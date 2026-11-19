using Microsoft.IdentityModel.Tokens;

namespace Soovioo.Api.Backoffice;

/// <summary>The backoffice's public keys for commands
/// (<c>GET .well-known/jwks.json</c>), kept for a day. A key not among them
/// (the backoffice replaced its key) fetches them again, at most once a minute.</summary>
public class BackofficeKeys(HttpClient http, IBackofficeClient backoffice, TimeProvider clock, ILogger<BackofficeKeys> logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyList<SecurityKey> keys = [];
    private DateTimeOffset fetchedAt = DateTimeOffset.MinValue;

    /// <summary>The keys, among them the one with <paramref name="keyId"/> if the
    /// backoffice has it.</summary>
    public async Task<IReadOnlyList<SecurityKey>> GetAsync(string? keyId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var stale = now - fetchedAt > MaxAge;
        var unknown = keyId is not null && keys.All(k => k.KeyId != keyId);
        if (!stale && !unknown)
        {
            return keys;
        }
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (now - fetchedAt >= MinRefreshInterval && backoffice.Connection is { } connection)
            {
                try
                {
                    var json = await http.GetStringAsync(new Uri(connection.BaseUrl, ".well-known/jwks.json"), cancellationToken);
                    keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
                {
                    logger.LogWarning(ex, "The backoffice's keys are unavailable, using the ones from before.");
                }
                fetchedAt = now;
            }
            return keys;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}
