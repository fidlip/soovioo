using System.Net;
using Microsoft.Extensions.Options;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Backoffice;

public class BackofficeClientTests
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
    private readonly Handler backoffice = new();

    private static readonly BackofficeOptions Connected = new()
    {
        BaseUrl = "http://backoffice.test",
        InstanceId = FakeBackoffice.InstanceId,
        Secret = "instance-secret",
    };

    private BackofficeClient Client(BackofficeOptions? options = null) =>
        new(new HttpClient(backoffice), Options.Create(options ?? Connected), clock);

    [Fact]
    public async Task ARequestGoesWithTheInstancesTokenAndProtocolVersion()
    {
        using var response = await Client().SendAsync(HttpMethod.Get, "instance-api/v1/plans/Demo");

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        var (tokenRequest, form) = backoffice.Received[0];
        Assert.Equal("http://backoffice.test/oauth/token", tokenRequest.RequestUri!.ToString());
        Assert.Equal(
            $"grant_type=client_credentials&client_id={FakeBackoffice.InstanceId}&client_secret=instance-secret", form);
        var (request, _) = backoffice.Received[1];
        Assert.Equal("http://backoffice.test/instance-api/v1/plans/Demo", request.RequestUri!.ToString());
        Assert.Equal("Bearer token-1", request.Headers.Authorization!.ToString());
        Assert.Equal("1", request.Headers.GetValues("Soovioo-Protocol").Single());
    }

    [Fact]
    public async Task TheTokenIsKeptUntilAMinuteBeforeItExpires()
    {
        var client = Client();

        await client.SendAsync(HttpMethod.Get, "a");
        clock.Now += TimeSpan.FromMinutes(8);
        await client.SendAsync(HttpMethod.Get, "b");
        clock.Now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        await client.SendAsync(HttpMethod.Get, "c");

        Assert.Equal(2, backoffice.TokensIssued);
        Assert.Equal("Bearer token-1", backoffice.Received.Single(r => r.Request.RequestUri!.AbsolutePath == "/b").Request.Headers.Authorization!.ToString());
        Assert.Equal("Bearer token-2", backoffice.Received.Single(r => r.Request.RequestUri!.AbsolutePath == "/c").Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ATokenTheBackofficeNoLongerTakesIsRenewedOnce()
    {
        backoffice.RejectToken = "token-1";

        using var response = await Client().SendAsync(HttpMethod.Get, "a");

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(2, backoffice.TokensIssued);
    }

    [Fact]
    public async Task ARefusedTokenIsAnHttpError()
    {
        backoffice.TokenStatus = HttpStatusCode.Forbidden;

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Client().SendAsync(HttpMethod.Get, "a"));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Theory]
    [InlineData(null, "s")]
    [InlineData("http://backoffice.test", null)]
    public async Task WithoutTheWholeConnectionNothingIsSent(string? baseUrl, string? secret)
    {
        var client = Client(new BackofficeOptions { BaseUrl = baseUrl, InstanceId = FakeBackoffice.InstanceId, Secret = secret });

        Assert.Null(client.Connection);
        Assert.Null(await client.SendAsync(HttpMethod.Get, "a"));
        Assert.Empty(backoffice.Received);
    }

    private class Handler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Received { get; } = [];
        public int TokensIssued { get; private set; }
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public string? RejectToken { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                if (TokenStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(TokenStatus) { Content = new StringContent("""{"error":"access_denied"}""") };
                }
                TokensIssued++;
                return Json($$"""{"access_token":"token-{{TokensIssued}}","token_type":"Bearer","expires_in":600}""");
            }
            return request.Headers.Authorization?.Parameter == RejectToken
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
