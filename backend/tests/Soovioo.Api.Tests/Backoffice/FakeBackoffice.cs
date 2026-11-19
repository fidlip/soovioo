using System.Net;
using Soovioo.Api.Backoffice;

namespace Soovioo.Api.Tests.Backoffice;

/// <summary>A backoffice that answers with <see cref="Respond"/> and remembers
/// what it was sent; <see cref="NotConnected"/> is an instance without one.</summary>
internal class FakeBackoffice : IBackofficeClient
{
    public static readonly Guid InstanceId = new("aee3cdd4-0f16-4596-9451-fb6f06e11bfa");

    public static FakeBackoffice NotConnected() => new(connected: false);

    public FakeBackoffice(bool connected = true) =>
        Connection = connected ? new BackofficeConnection(new Uri("http://backoffice.test/"), InstanceId, "secret") : null;

    public BackofficeConnection? Connection { get; }

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.NoContent);

    public List<(HttpRequestMessage Request, string Body)> Sent { get; } = [];

    public static FakeBackoffice Answering(HttpStatusCode status, string body = "") => new()
    {
        Respond = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        },
    };

    public async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, CancellationToken cancellationToken = default)
    {
        if (Connection is null)
        {
            return null;
        }
        var request = new HttpRequestMessage(method, new Uri(Connection.BaseUrl, path)) { Content = content };
        Sent.Add((request, content is null ? "" : await content.ReadAsStringAsync(cancellationToken)));
        return Respond(request);
    }
}
