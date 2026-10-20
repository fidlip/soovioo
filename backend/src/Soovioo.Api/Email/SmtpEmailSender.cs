using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Soovioo.Api.Email;

public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            throw new InvalidOperationException(
                "Smtp:Host is not configured — e-mail sending isn't set up in this environment.");
        }

        using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
        if (!string.IsNullOrWhiteSpace(smtp.User))
        {
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(to);
        // SmtpClient doesn't add a Message-ID; Gmail patches one in, but a
        // missing one counts against the message in some spam filters.
        message.Headers.Add("Message-ID", $"<{Guid.NewGuid():N}@{message.From.Host}>");

        await client.SendMailAsync(message);
    }
}
