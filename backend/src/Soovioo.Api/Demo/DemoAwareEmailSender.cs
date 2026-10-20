using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Email;

namespace Soovioo.Api.Demo;

/// <summary>The <see cref="IEmailSender"/> every school feature gets: inside
/// a demo school (the caller's <c>schoolId</c> claim) the e-mail is only
/// stored as a <see cref="DemoEmail"/> for the preview, anywhere else it
/// goes out through <see cref="SmtpEmailSender"/>. The demo's own e-mails to
/// its visitor (link, expiry reminders) use <see cref="SmtpEmailSender"/>
/// directly.</summary>
public class DemoAwareEmailSender(
    AppDbContext db, ICurrentSchoolContext currentSchool, [FromKeyedServices(SmtpEmailSender.Key)] IEmailSender smtp)
    : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (currentSchool.SchoolId is { } schoolId
            && await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Id == schoolId && s.IsDemo))
        {
            db.DemoEmails.Add(new DemoEmail { SchoolId = schoolId, To = to, Subject = subject, HtmlBody = htmlBody });
            await db.SaveChangesAsync();
            return;
        }
        await smtp.SendAsync(to, subject, htmlBody);
    }
}
