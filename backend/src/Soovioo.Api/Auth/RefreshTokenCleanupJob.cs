namespace Soovioo.Api.Auth;

/// <summary>Daily Hangfire job (registered in Program.cs) pruning expired
/// <see cref="RefreshToken"/> rows.</summary>
public class RefreshTokenCleanupJob(RefreshTokenService refreshTokens)
{
    public Task RunAsync() => refreshTokens.DeleteExpiredAsync();
}
