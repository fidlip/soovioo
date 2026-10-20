namespace Soovioo.Api.Demo;

/// <summary>An e-mail a demo school "sent" — nothing leaves the server from
/// a demo (it must never become a way to mail strangers), the web app shows
/// it as a preview instead, links included, so the invite flow can still be
/// tried end to end. See <see cref="DemoAwareEmailSender"/>.</summary>
public class DemoEmail
{
    public int Id { get; set; }
    public int SchoolId { get; set; }
    public required string To { get; set; }
    public required string Subject { get; set; }
    public required string HtmlBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
