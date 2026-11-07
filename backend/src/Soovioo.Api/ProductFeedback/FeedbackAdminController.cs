using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Email;

namespace Soovioo.Api.ProductFeedback;

/// <summary>SuperAdmin → Zpětná vazba: what schools' users and demo
/// visitors thought, and the answers to it.</summary>
[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/superadmin/feedback")]
public class FeedbackAdminController(
    AppDbContext db,
    [FromKeyedServices(SmtpEmailSender.Key)] IEmailSender smtp,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<FeedbackAdminController> logger) : ControllerBase
{
    private const int MaxPageSize = 100;
    public const int MaxReplyLength = 10000;

    /// <summary>Newest first, filtered (see <see cref="FeedbackFilter"/>).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<FeedbackDto>>> GetFeedback(
        [FromQuery] FeedbackFilter filter, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Filtered(filter);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto())
            .ToListAsync();
        return Ok(new PagedResult<FeedbackDto>(items, total, page, pageSize));
    }

    /// <summary>For the badge at "Zpětná vazba" in the menu.</summary>
    [HttpGet("unseen-count")]
    public async Task<ActionResult<int>> GetUnseenCount() =>
        Ok(await db.Feedback.CountAsync(f => f.SeenAt == null));

    [HttpGet("{id:int}/replies")]
    public async Task<ActionResult<IEnumerable<FeedbackReplyDto>>> GetReplies(int id)
    {
        if (!await db.Feedback.AnyAsync(f => f.Id == id))
        {
            return NotFound();
        }
        return Ok(await db.FeedbackReplies
            .Where(r => r.FeedbackId == id)
            .OrderBy(r => r.SentAt).ThenBy(r => r.Id)
            .Select(r => new FeedbackReplyDto(r.Id, r.Text, r.SentBy, r.SentAt))
            .ToListAsync());
    }

    /// <summary>Seen, flagged or resolved — only the fields sent change.</summary>
    [HttpPatch("{id:int}")]
    public async Task<ActionResult<FeedbackDto>> Update(int id, FeedbackUpdate update)
    {
        if (await db.Feedback.FirstOrDefaultAsync(f => f.Id == id) is not { } feedback)
        {
            return NotFound();
        }
        var now = time.GetUtcNow();
        if (update.Seen is { } seen)
        {
            feedback.SeenAt = seen ? feedback.SeenAt ?? now : null;
        }
        if (update.Flagged is { } flagged)
        {
            feedback.Flagged = flagged;
        }
        if (update.Resolved is { } resolved)
        {
            feedback.ResolvedAt = resolved ? feedback.ResolvedAt ?? now : null;
        }
        await db.SaveChangesAsync();
        return Ok(await db.Feedback.Where(f => f.Id == id).Select(ToDto()).FirstAsync());
    }

    /// <summary>Spam, or someone asking for it to be erased — the replies go with it.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await db.Feedback.FirstOrDefaultAsync(f => f.Id == id) is not { } feedback)
        {
            return NotFound();
        }
        db.Feedback.Remove(feedback);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>E-mails the answer and keeps it; only to someone who agreed
    /// to be contacted, and stored only once it went out.</summary>
    [HttpPost("{id:int}/replies")]
    public async Task<ActionResult<FeedbackReplyDto>> Reply(int id, FeedbackReplyRequest request)
    {
        if (await db.Feedback.FirstOrDefaultAsync(f => f.Id == id) is not { } feedback)
        {
            return NotFound();
        }
        if (feedback.Email is null || !feedback.Consent)
        {
            return this.FieldError("text", "Odesílatel nesouhlasil s kontaktem, odpovědět nelze.");
        }
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return this.FieldError("text", "Napište text odpovědi.");
        }
        if (text.Length > MaxReplyLength)
        {
            return this.FieldError("text", "Odpověď je moc dlouhá.");
        }

        try
        {
            await smtp.SendAsync(feedback.Email, "Odpověď na vaši zpětnou vazbu k Soovioo", ReplyEmail(feedback, text));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reply to feedback {Id} wasn't sent.", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "E-mail se nepodařilo odeslat, zkuste to později.");
        }

        var now = time.GetUtcNow();
        var reply = new FeedbackReply
        {
            FeedbackId = id,
            Text = text,
            SentBy = User.Identity?.Name ?? "Superadmin",
            SentAt = now,
        };
        db.FeedbackReplies.Add(reply);
        feedback.SeenAt ??= now;
        await db.SaveChangesAsync();
        return Ok(new FeedbackReplyDto(reply.Id, reply.Text, reply.SentBy, reply.SentAt));
    }

    private IQueryable<Feedback> Filtered(FeedbackFilter filter)
    {
        var query = db.Feedback.AsQueryable();
        query = filter.State switch
        {
            FeedbackState.Unseen => query.Where(f => f.SeenAt == null),
            FeedbackState.Unanswered => query.Where(f => f.Email != null && f.Consent && !f.Replies.Any()),
            FeedbackState.Answered => query.Where(f => f.Replies.Any()),
            FeedbackState.Flagged => query.Where(f => f.Flagged),
            FeedbackState.Open => query.Where(f => f.ResolvedAt == null),
            FeedbackState.Resolved => query.Where(f => f.ResolvedAt != null),
            _ => query,
        };
        query = filter.Source switch
        {
            FeedbackSource.School => query.Where(f => f.DemoSlug == null),
            FeedbackSource.Demo => query.Where(f => f.DemoSlug != null),
            _ => query,
        };
        if (filter.Rating is 0)
        {
            query = query.Where(f => f.Rating == null);
        }
        else if (filter.Rating is { } rating)
        {
            query = query.Where(f => f.Rating == rating);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = TextSearch.Fold(filter.Search.Trim());
            query = query.Where(f =>
                (f.Text != null && TextSearch.Fold(f.Text).Contains(search))
                || (f.Email != null && TextSearch.Fold(f.Email).Contains(search))
                || (f.Role != null && TextSearch.Fold(f.Role).Contains(search))
                || (f.DemoSlug != null && TextSearch.Fold(f.DemoSlug).Contains(search))
                || (f.SchoolName != null && TextSearch.Fold(f.SchoolName).Contains(search)));
        }
        return query;
    }

    /// <summary>The school and the user are looked up past the tenant
    /// filters: either may be gone by now.</summary>
    private Expression<Func<Feedback, FeedbackDto>> ToDto()
    {
        var schools = db.Schools.IgnoreQueryFilters();
        var users = db.Users.IgnoreQueryFilters();
        return f => new FeedbackDto(
            f.Id, f.SchoolName, f.DemoSlug, schools.Any(s => s.Id == f.SchoolId),
            users.Where(u => u.Id == f.UserId)
                .Select(u => u.Person != null && (u.Person.FirstName + u.Person.LastName) != ""
                    ? (u.Person.FirstName + " " + u.Person.LastName).Trim()
                    : u.Username)
                .FirstOrDefault(),
            f.Rating, f.Text, f.Email, f.Consent, f.Role, f.CreatedAt,
            f.SeenAt, f.Flagged, f.ResolvedAt, f.Replies.Count, f.Replies.Max(r => (DateTimeOffset?)r.SentAt));
    }

    private string ReplyEmail(Feedback feedback, string text)
    {
        var contact = options.Value.FeedbackEmail;
        var original = feedback.Text is null
            ? ""
            : $"""
                <p style="color: #666">Vaše zpětná vazba ({SchoolFormat.Date(feedback.CreatedAt)}):</p>
                <blockquote style="margin: 0 0 0 8px; padding-left: 8px; border-left: 3px solid #ccc; color: #666; white-space: pre-wrap">{FeedbackService.Html(feedback.Text)}</blockquote>
                """;
        return $"""
            <p style="white-space: pre-wrap">{FeedbackService.Html(text)}</p>
            <p>Tým Soovioo — odpovědět můžete na <a href="mailto:{contact}">{contact}</a>.</p>
            {original}
            """;
    }
}

/// <param name="SchoolName">As it was named when the feedback was sent.</param>
/// <param name="DemoSlug">Set for a demo's feedback.</param>
/// <param name="SchoolExists">False once the school (or demo) was deleted.</param>
/// <param name="UserName">Who sent it from a school, while the account exists.</param>
public record FeedbackDto(
    int Id, string? SchoolName, string? DemoSlug, bool SchoolExists, string? UserName,
    int? Rating, string? Text, string? Email, bool Consent, string? Role,
    DateTimeOffset CreatedAt, DateTimeOffset? SeenAt, bool Flagged, DateTimeOffset? ResolvedAt,
    int ReplyCount, DateTimeOffset? LastReplyAt);

public record FeedbackReplyDto(int Id, string Text, string SentBy, DateTimeOffset SentAt);

public record FeedbackReplyRequest(string? Text);

public record FeedbackUpdate(bool? Seen = null, bool? Flagged = null, bool? Resolved = null);

public enum FeedbackState
{
    Unseen,
    /// <summary>May be contacted, no reply yet.</summary>
    Unanswered,
    Answered,
    Flagged,
    /// <summary>Not resolved.</summary>
    Open,
    Resolved,
}

public enum FeedbackSource
{
    School,
    Demo,
}

/// <param name="Rating">1–5, or 0 for "without a rating".</param>
/// <param name="Search">Text, e-mail, role, school or demo contains it (case and diacritics folded).</param>
public record FeedbackFilter(
    FeedbackState? State = null, FeedbackSource? Source = null, int? Rating = null, string? Search = null);
