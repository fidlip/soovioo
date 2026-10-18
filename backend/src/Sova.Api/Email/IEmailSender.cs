namespace Sova.Api.Email;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}
