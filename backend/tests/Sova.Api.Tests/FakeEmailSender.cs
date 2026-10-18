using Sova.Api.Email;

namespace Sova.Api.Tests;

internal class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string HtmlBody)> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
