namespace Soovioo.Api.Auth;

/// <summary>Bound from the <c>App</c> config section — lets the backend build
/// a link into the frontend (currently only the invite e-mail, see
/// <see cref="UsersController"/>) without duplicating the frontend's own
/// tenant-routing decision. Mirrors <c>SchoolContextService</c>/
/// <c>environment.mainDomain</c> on the frontend; the two aren't wired
/// together (same known gap as <c>API_BASE_URL</c> in ROADMAP.md), so keep
/// them in sync by hand if either changes.</summary>
public class AppLinkOptions
{
    /// <summary>True in dev/LAN, where the frontend has no real subdomain
    /// and carries the school slug as the first path segment instead
    /// (<c>http://localhost:4200/demo/...</c>). False in a real deployment,
    /// where the slug is a subdomain of <see cref="MainDomain"/> instead.</summary>
    public bool PathBasedTenant { get; set; }

    /// <summary>Required when <see cref="PathBasedTenant"/> is true — e.g.
    /// <c>http://localhost:4200</c>. A link is built as
    /// <c>{FrontendBaseUrl}/{slug}/accept-invite?token=...</c>.</summary>
    public string? FrontendBaseUrl { get; set; }

    /// <summary>Required when <see cref="PathBasedTenant"/> is false — e.g.
    /// <c>soovioo.cz</c>. A link is built as
    /// <c>https://{slug}.{MainDomain}/accept-invite?token=...</c>.</summary>
    public string? MainDomain { get; set; }
}
