using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Tests.Auth;

/// <summary>Settable clock for refresh-token expiry/grace tests.</summary>
internal class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class AuthTestServices
{
    public static readonly JwtTokenFactory TokenFactory = new(Options.Create(
        new JwtOptions { SigningKey = "test-signing-key-at-least-32-chars-long", Issuer = "soovioo-tests", Audience = "soovioo-tests" }));

    public static RefreshTokenService RefreshTokens(AppDbContext db, TimeProvider? clock = null) =>
        new(db, clock ?? TimeProvider.System);

    public static AuthSessionService Sessions(AppDbContext db, TimeProvider? clock = null) =>
        new(db, TokenFactory, RefreshTokens(db, clock), Options.Create(new AuthCookieOptions()));
}
