using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Email;

namespace Soovioo.Api.Notifications;

/// <summary>Hangfire job every 15 minutes (registered in Program.cs) — the
/// daily e-mail of what's new and still unread, at each person's time (or
/// the school's). Goes only when there's something to send; a demo school's
/// lands in its e-mail preview instead.</summary>
public class NotificationDigestJob(
    AppDbContext db,
    [FromKeyedServices(SmtpEmailSender.Key)] IEmailSender smtp,
    IOptions<AppLinkOptions> links,
    TimeProvider clock,
    ILogger<NotificationDigestJob> logger)
{
    public const int MaxItems = 20;

    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>How far back the first e-mail of an account looks. Each one
    /// has what came in [last e-mail's turn, now).</summary>
    public static readonly TimeSpan FirstLookBack = TimeSpan.FromDays(1);

    public async Task RunAsync()
    {
        var now = clock.GetUtcNow();
        var firstSince = now - FirstLookBack;
        var today = SchoolClock.Today(clock);
        var users = await db.Users.IgnoreQueryFilters()
            .Include(u => u.School)
            .Include(u => u.Person)
            .Where(u => u.SchoolId != null && u.DeactivatedAt == null && u.ErasedAt == null && u.Person!.Email != null)
            .Where(u => db.Notifications.IgnoreQueryFilters()
                .Any(n => n.UserId == u.Id && n.ReadAt == null && n.CreatedAt >= (u.DigestSentAt ?? firstSince) && n.CreatedAt < now))
            .ToListAsync();

        foreach (var user in users)
        {
            var slot = SchoolClock.At(today, user.DigestTime ?? user.School!.DigestTime);
            if (now < slot || user.DigestSentAt >= slot)
            {
                continue;
            }
            var since = user.DigestSentAt ?? firstSince;
            var forced = user.School!.ForcedEmailNotifications;
            var items = await db.Notifications.IgnoreQueryFilters()
                .Where(n => n.UserId == user.Id && n.ReadAt == null && n.CreatedAt >= since && n.CreatedAt < now
                    && (forced.Contains(n.Type) || !user.MutedEmailNotifications.Contains(n.Type)))
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
            if (items.Count > 0)
            {
                user.EmailUnsubscribeToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                try
                {
                    await SendAsync(user, items);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Daily notification e-mail for user {UserId} wasn't sent.", user.Id);
                    continue;
                }
            }
            user.DigestSentAt = now;
            await db.SaveChangesAsync();
        }
    }

    private async Task SendAsync(User user, IReadOnlyList<Notification> items)
    {
        var school = user.School!;
        var subject = $"{Count(items.Count)} — {school.Name}";
        var body = Body(links.Value.SchoolOrigin(school.Slug), user.EmailUnsubscribeToken!, items);
        if (school.IsDemo)
        {
            db.DemoEmails.Add(new DemoEmail { SchoolId = school.Id, To = user.Person!.Email!, Subject = subject, HtmlBody = body });
            return;
        }
        await smtp.SendAsync(user.Person!.Email!, subject, body);
    }

    public static string Count(int count) => count switch
    {
        1 => "1 nové upozornění",
        >= 2 and <= 4 => $"{count} nová upozornění",
        _ => $"{count} nových upozornění",
    };

    public static string Body(string? origin, string unsubscribeToken, IReadOnlyList<Notification> items)
    {
        var html = new StringBuilder();
        html.Append("<p>Dobrý den,</p><p>od posledního souhrnu pro vás v Soovioo přibylo:</p><ul>");
        foreach (var item in items.Take(MaxItems))
        {
            var title = Html.Encode(item.Title);
            if (origin is not null)
            {
                title = $"""<a href="{origin}/{NotificationLinks.For(item)}">{title}</a>""";
            }
            html.Append($"<li><strong>{title}</strong>");
            if (!string.IsNullOrEmpty(item.Body))
            {
                html.Append($"<br>{Html.Encode(item.Body)}");
            }
            html.Append("</li>");
        }
        html.Append("</ul>");
        if (items.Count > MaxItems)
        {
            html.Append($"<p>Další upozornění ({items.Count - MaxItems}) najdete v aplikaci.</p>");
        }
        if (origin is not null)
        {
            html.Append($"""<p><a href="{origin}">Otevřít Soovioo</a></p>""");
        }
        html.Append("""<p style="color:#6b7280;font-size:12px">Souhrn chodí jednou denně, jen když je co poslat.""");
        if (origin is not null)
        {
            html.Append($""" Co v něm bude a kdy přijde, změníte v <a href="{origin}/settings">Nastavení</a>."""
                + $""" <a href="{origin}/unsubscribe?token={unsubscribeToken}">Odhlásit tyto e-maily</a>""");
        }
        html.Append("</p>");
        return html.ToString();
    }
}
