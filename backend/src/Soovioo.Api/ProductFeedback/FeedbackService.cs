using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.ProductFeedback;

/// <param name="Role">Which role the user was looking through.</param>
public record FeedbackRequest(int? Rating, string? Text, string? Email, bool Consent = false, string? Role = null);

/// <summary>Stores feedback from a school or a demo and forwards it to
/// <see cref="DemoOptions.FeedbackEmail"/>.</summary>
public class FeedbackService(
    AppDbContext db,
    [FromKeyedServices(SmtpEmailSender.Key)] IEmailSender smtp,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<FeedbackService> logger)
{
    public const int MaxTextLength = 4000;

    /// <returns>The field and message when the request is refused.</returns>
    public async Task<(string Field, string Message)?> SaveAsync(School school, int? userId, FeedbackRequest request)
    {
        if (request.Rating is < 1 or > 5)
        {
            return ("rating", "Hodnocení je 1 až 5.");
        }
        var text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();
        if (text?.Length > MaxTextLength)
        {
            return ("text", "Text je moc dlouhý.");
        }
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is not null && (!email.Contains('@') || email.Length > 254))
        {
            return ("email", "Neplatný e-mail.");
        }
        if (request.Rating is null && text is null)
        {
            return ("text", "Napište nám aspoň pár slov, nebo vyberte hodnocení.");
        }

        var feedback = new Feedback
        {
            SchoolId = school.Id,
            SchoolName = school.Name,
            DemoSlug = school.IsDemo ? school.Slug : null,
            UserId = userId,
            Rating = request.Rating,
            Text = text,
            Email = email,
            Consent = request.Consent && email is not null,
            Role = request.Role,
            CreatedAt = time.GetUtcNow(),
        };
        db.Feedback.Add(feedback);
        // A demo visitor's contact is kept like the wizard's.
        if (school.IsDemo && email is not null
            && !await db.DemoContacts.AnyAsync(c => c.SchoolId == school.Id && c.Email == email))
        {
            db.DemoContacts.Add(new DemoContact
            {
                SchoolId = school.Id,
                DemoSlug = school.Slug,
                Email = email,
                Consent = feedback.Consent,
                Source = "feedback",
                CreatedAt = feedback.CreatedAt,
            });
        }
        await db.SaveChangesAsync();

        var stars = Stars(feedback.Rating);
        var from = school.IsDemo
            ? $"<strong>Demo:</strong> {Html(school.Name)} ({school.Slug}, {school.Type})"
            : $"<strong>Škola:</strong> {Html(school.Name)} ({school.Slug})";
        try
        {
            await smtp.SendAsync(
                options.Value.FeedbackEmail,
                $"Zpětná vazba {(school.IsDemo ? "z dema" : "ze školy")} ({stars})",
                $"""
                <p>{from}<br>
                <strong>Role:</strong> {Html(feedback.Role ?? "?")}<br>
                <strong>Hodnocení:</strong> {stars}<br>
                <strong>E-mail:</strong> {Html(feedback.Email ?? "—")} (souhlas s kontaktem: {(feedback.Consent ? "ano" : "ne")})</p>
                <p style="white-space: pre-wrap">{Html(feedback.Text ?? "")}</p>
                """);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Feedback {Id} wasn't forwarded to the team.", feedback.Id);
        }
        return null;
    }

    public static string Stars(int? rating) =>
        rating is { } r ? new string('★', r) + new string('☆', 5 - r) : "bez hodnocení";

    /// <summary>Escapes markup only — Czech letters stay readable.</summary>
    public static string Html(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
