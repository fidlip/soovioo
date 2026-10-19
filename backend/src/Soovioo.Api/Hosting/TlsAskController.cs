using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Hosting;

/// <summary>Caddy's on-demand TLS "ask" endpoint (see deploy/Caddyfile):
/// before obtaining a certificate for a school subdomain on its first
/// visit, Caddy asks here whether that hostname is ours — 200 = go ahead,
/// anything else = refuse. Without this check anyone could point a random
/// <c>*.{MainDomain}</c> name at us and burn the Let's Encrypt rate limit.
/// Only reachable on the internal Docker network; the public site answers
/// <c>/api/tls/*</c> with 404.</summary>
[ApiController]
public class TlsAskController(AppDbContext db, IOptions<AppLinkOptions> options) : ControllerBase
{
    [HttpGet("api/tls/ask")]
    public async Task<IActionResult> Ask([FromQuery] string? domain)
    {
        var slug = SchoolSlugOf(domain, options.Value.MainDomain);
        if (slug is null)
        {
            return NotFound();
        }
        var exists = await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug);
        return exists ? Ok() : NotFound();
    }

    /// <summary>The school slug a certificate for <paramref name="domain"/>
    /// would serve: exactly one label in front of <paramref name="mainDomain"/>
    /// (<c>demo.soovioo.cloud</c> → <c>demo</c>). Null for anything else,
    /// including the bare main domain, which Caddy certifies on its own.</summary>
    public static string? SchoolSlugOf(string? domain, string? mainDomain)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(mainDomain))
        {
            return null;
        }
        var suffix = $".{mainDomain}";
        if (!domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var slug = domain[..^suffix.Length].ToLowerInvariant();
        return slug.Length > 0 && !slug.Contains('.') ? slug : null;
    }
}
