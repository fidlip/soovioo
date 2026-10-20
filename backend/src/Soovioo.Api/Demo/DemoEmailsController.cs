using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Demo;

/// <summary>The preview of what a demo school "sent" (see
/// <see cref="DemoAwareEmailSender"/>) — the caller's own school only, by
/// the tenant filter; a real school has none.</summary>
[Authorize]
[ApiController]
[Route("api/demo/emails")]
public class DemoEmailsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DemoEmailDto>>> GetEmails() =>
        Ok(await db.DemoEmails
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Take(20)
            .Select(e => new DemoEmailDto(e.Id, e.To, e.Subject, e.HtmlBody, e.CreatedAt))
            .ToListAsync());
}

public record DemoEmailDto(int Id, string To, string Subject, string HtmlBody, DateTimeOffset CreatedAt);
