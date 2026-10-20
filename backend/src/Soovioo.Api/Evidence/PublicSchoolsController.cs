using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

/// <summary>What a school's pages need before anyone logs in: does the slug
/// (subdomain) belong to a school, and what is it called. The wildcard
/// certificate makes every subdomain reachable, so the web app asks here and
/// shows "škola neexistuje" for an unknown one. Name and type are public
/// anyway — they're on the login page; so is whether it's a demo.</summary>
[AllowAnonymous]
[ApiController]
[Route("api/public/schools")]
public class PublicSchoolsController(AppDbContext db) : ControllerBase
{
    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicSchoolDto>> GetSchool(string slug)
    {
        var school = await db.Schools
            .Where(s => s.Slug == slug)
            .Select(s => new PublicSchoolDto(s.Name, s.Type.ToString(), s.IsDemo, s.ExpiresAt))
            .FirstOrDefaultAsync();
        return school is null ? NotFound() : Ok(school);
    }
}

/// <param name="IsDemo">The login page then lists the users to pick from
/// instead of asking for a password, and the app shows the demo bar.</param>
public record PublicSchoolDto(string Name, string Type, bool IsDemo = false, DateTimeOffset? ExpiresAt = null);
