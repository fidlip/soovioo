using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Reference;

/// <summary>Read-only search — 6400+ rows, so unlike CountriesController this
/// never returns the full table, only matches for the autocomplete's typed
/// query (p-autoComplete's completeMethod). No separate districts endpoint:
/// OKRESB is derived from the chosen municipality's DistrictCode, never
/// picked directly in the UI — see Municipality.cs.</summary>
[Authorize]
[ApiController]
[Route("api/municipalities")]
public class MunicipalitiesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Municipality>>> SearchMunicipalities([FromQuery] string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Ok(Array.Empty<Municipality>());
        }

        var matches = await db.Municipalities
            .Where(m => TextSearch.Fold(m.Name).Contains(TextSearch.Fold(query)))
            .OrderBy(m => m.Name)
            .Take(20)
            .ToListAsync();

        return Ok(matches);
    }
}
