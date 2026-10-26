using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Soovioo.Api.Auth;

namespace Soovioo.Api.Matrika;

[Authorize]
[RequirePermission(Permission.MatrikaExport)]
[ApiController]
[Route("api/matrika")]
public class MatrikaController(MatrikaExportService exportService) : ControllerBase
{
    /// <summary>Generates the "základní soubor" XML for the current school.
    /// See <see cref="MatrikaExportService"/> for exactly what is (and isn't)
    /// implemented yet — this is a pre-pilot skeleton, not a submission-ready
    /// export. Returns 400 with a list of per-student validation errors
    /// (missing birth date, gender, class ročník, ...) instead of a partial
    /// file when any student can't be fully represented.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly? rdat,
        [FromQuery] string cast,
        [FromQuery] string contactName,
        [FromQuery] string contactPhone,
        [FromQuery] string contactEmail)
    {
        var outcome = await exportService.BuildBaseFileAsync(
            rdat ?? DateOnly.FromDateTime(DateTime.Today), cast, contactName, contactPhone, contactEmail);

        if (!outcome.Success)
        {
            return BadRequest(new { errors = outcome.Errors });
        }

        return File(outcome.Content!, "application/xml", outcome.FileName);
    }
}
