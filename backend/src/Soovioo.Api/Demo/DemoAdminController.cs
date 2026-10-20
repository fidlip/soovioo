using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Demo;

/// <summary>SuperAdmin's view of the "Vyzkoušet" demos: who left a contact,
/// and what visitors thought.</summary>
[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/superadmin/demo")]
public class DemoAdminController(AppDbContext db) : ControllerBase
{
    [HttpGet("contacts")]
    public async Task<ActionResult<IEnumerable<DemoContactDto>>> GetContacts() =>
        Ok(await Contacts().ToListAsync());

    /// <summary>Semicolon-separated with a BOM — opens straight in a Czech Excel.</summary>
    [HttpGet("contacts.csv")]
    public async Task<IActionResult> GetContactsCsv()
    {
        var csv = new StringBuilder("E-mail;Souhlas s kontaktem;Zdroj;Demo;Demo stále existuje;Vytvořeno\r\n");
        foreach (var c in await Contacts().ToListAsync())
        {
            csv.Append($"{Cell(c.Email)};{(c.Consent ? "ano" : "ne")};{c.Source};{c.DemoSlug};{(c.DemoExists ? "ano" : "ne")};{c.CreatedAt:yyyy-MM-dd HH:mm}\r\n");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "soovioo-demo-kontakty.csv");
    }

    [HttpGet("feedback")]
    public async Task<ActionResult<IEnumerable<DemoFeedbackDto>>> GetFeedback() =>
        Ok(await db.DemoFeedback
            .OrderByDescending(f => f.CreatedAt)
            .Take(500)
            .Select(f => new DemoFeedbackDto(f.Id, f.DemoSlug, f.Rating, f.Text, f.Email, f.Consent, f.Role, f.CreatedAt))
            .ToListAsync());

    private IQueryable<DemoContactDto> Contacts() =>
        db.DemoContacts
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new DemoContactDto(c.Id, c.Email, c.Consent, c.Source, c.DemoSlug, c.SchoolId != null, c.CreatedAt));

    /// <summary>No formula injection when the file is opened in Excel.</summary>
    private static string Cell(string value)
    {
        var safe = value.Replace(";", ",").Replace("\"", "'");
        return safe.Length > 0 && "=+-@".Contains(safe[0]) ? "'" + safe : safe;
    }
}

public record DemoContactDto(int Id, string Email, bool Consent, string Source, string DemoSlug, bool DemoExists, DateTimeOffset CreatedAt);

public record DemoFeedbackDto(
    int Id, string DemoSlug, int? Rating, string? Text, string? Email, bool Consent, string? Role, DateTimeOffset CreatedAt);
