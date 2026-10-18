using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Reference;

/// <summary>Read-only — Country/District/Municipality are seeded reference
/// data (see Program.cs SeedReferenceData), never edited through the API.
/// Only 252 rows, so the full list is returned (unlike municipalities).</summary>
[Authorize]
[ApiController]
[Route("api/countries")]
public class CountriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Country>>> GetCountries()
    {
        return Ok(await db.Countries.OrderBy(c => c.Name).ToListAsync());
    }
}
