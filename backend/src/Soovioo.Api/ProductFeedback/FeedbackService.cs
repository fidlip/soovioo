using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.ProductFeedback;

/// <param name="Role">Which role the user was looking through.</param>
public record FeedbackRequest(int? Rating, string? Text, string? Email, bool Consent = false, string? Role = null);

/// <summary>Sends feedback from a school or a demo to the backoffice (Kontakty) through the
/// outbox; nothing of it stays here. An instance not connected to a backoffice has nowhere to
/// send it, the form isn't shown there.</summary>
public class FeedbackService(AppDbContext db, IBackofficeClient backoffice, TimeProvider time)
{
    public const int MaxTextLength = 4000;
    public const string FeedbackSent = "FeedbackSent";

    public bool Available => backoffice.Connection is not null;

    /// <returns>The field and message when the request is refused.</returns>
    public async Task<(string Field, string Message)?> SendAsync(School school, int? userId, FeedbackRequest request)
    {
        if (!Available)
        {
            return ("text", "Zpětnou vazbu teď nejde poslat.");
        }
        if (request.Rating is < 1 or > 5)
        {
            return ("rating", "Hodnocení je 1 až 5.");
        }
        var text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();
        if (text?.Length > MaxTextLength)
        {
            return ("text", "Text je moc dlouhý.");
        }
        if (!ContactFormats.TryEmail(request.Email, out var email))
        {
            return ("email", ContactFormats.InvalidEmail);
        }
        if (request.Rating is null && text is null)
        {
            return ("text", "Napište nám aspoň pár slov, nebo vyberte hodnocení.");
        }

        var consent = request.Consent && email is not null;
        var now = time.GetUtcNow();
        var user = userId is { } id
            ? await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == id)
            : null;
        // The e-mail only with consent to be answered: the backoffice keeps nothing else of it.
        Outbox.Add(db, backoffice, FeedbackSent, new
        {
            schoolId = school.Id,
            schoolName = school.Name,
            demoSlug = school.IsDemo ? school.Slug : null,
            userId = user?.Id,
            userName = user is null ? null : People.PersonNames.Display(user),
            role = request.Role,
            rating = request.Rating,
            text,
            email = consent ? email : null,
            at = now,
        }, time);
        // A demo visitor's e-mail gets the demo's reminders like the wizard's.
        if (school.IsDemo && email is not null
            && !await db.DemoContacts.AnyAsync(c => c.SchoolId == school.Id && c.Email == email))
        {
            db.DemoContacts.Add(new DemoContact
            {
                SchoolId = school.Id,
                DemoSlug = school.Slug,
                Email = email,
                Consent = consent,
                Source = "feedback",
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return null;
    }
}
