namespace Soovioo.Api.Auth;

/// <summary>Bound from the <c>AuthCookie</c> config section.</summary>
public class AuthCookieOptions
{
    /// <summary>Marks the refresh cookie <c>Secure</c>. On by default;
    /// Development turns it off because the dev server is also reached over
    /// plain http from a phone on the LAN by IP, where browsers drop Secure
    /// cookies (only <c>localhost</c> is exempt).</summary>
    public bool Secure { get; set; } = true;
}
