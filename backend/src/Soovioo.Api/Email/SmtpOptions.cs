namespace Soovioo.Api.Email;

/// <summary>Bound from the <c>Smtp</c> config section. Dev points this at a
/// local catch-all (Mailpit, see docker-compose.yml) with no auth needed —
/// <see cref="User"/>/<see cref="Password"/> stay null there. A real
/// deployment supplies a real relay's credentials via environment variables
/// (<c>Smtp__User</c>/<c>Smtp__Password</c>), the same convention used for
/// <see cref="Auth.JwtOptions.SigningKey"/> — never checked into
/// appsettings.json.</summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@example.com";
    public string? FromName { get; set; }
}
