using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Soovioo.Api.Backoffice;

/// <summary>Bound from the <c>Backoffice</c> config section. The cloud has its
/// connection here; without <see cref="BaseUrl"/> the instance isn't connected
/// and never calls the backoffice.</summary>
public class BackofficeOptions
{
    /// <summary>E.g. <c>http://backoffice:8080</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>This instance's id in the backoffice (<c>client_id</c>).</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>The instance's <c>client_secret</c>.</summary>
    public string? Secret { get; set; }

    /// <summary>The port of the internal interface the backoffice sends commands
    /// to (<see cref="InternalApi"/>); not published outside. Null = no internal
    /// interface.</summary>
    public int? InternalPort { get; set; }
}

/// <summary>Where this instance's backoffice is and who the instance is in it.</summary>
public record BackofficeConnection(Uri BaseUrl, Guid InstanceId, string Secret);

/// <summary>The instance's side of talking to the backoffice: its token
/// (<c>client_credentials</c>), the protocol version header.</summary>
public interface IBackofficeClient
{
    /// <summary>Null when the instance isn't connected to a backoffice.</summary>
    BackofficeConnection? Connection { get; }

    /// <summary>Sends a request to <paramref name="path"/> (relative to the
    /// backoffice) with the instance's token, getting a new one when the
    /// backoffice no longer takes it. Null when not connected.</summary>
    Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null);
}

public class BackofficeClient(HttpClient http, IOptions<BackofficeOptions> options, TimeProvider clock) : IBackofficeClient
{
    public const string ProtocolHeader = "Soovioo-Protocol";
    public const int ProtocolVersion = 1;

    /// <summary>A token is renewed this long before it expires.</summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpiresAt;

    public BackofficeConnection? Connection { get; } = ConnectionFrom(options.Value);

    public static BackofficeConnection? ConnectionFrom(BackofficeOptions settings) =>
        !string.IsNullOrWhiteSpace(settings.BaseUrl) && settings.InstanceId is { } id && id != Guid.Empty
        && !string.IsNullOrWhiteSpace(settings.Secret)
            ? new BackofficeConnection(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), id, settings.Secret)
            : null;

    public async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (Connection is null)
        {
            return null;
        }
        var response = await SendWithTokenAsync(method, path, content, headers, renew: false, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }
        // Expired early, or the backoffice's key changed.
        response.Dispose();
        return await SendWithTokenAsync(method, path, content, headers, renew: true, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(
        HttpMethod method, string path, HttpContent? content, IReadOnlyDictionary<string, string>? headers, bool renew,
        CancellationToken cancellationToken)
    {
        var accessToken = await TokenAsync(renew, cancellationToken);
        var request = new HttpRequestMessage(method, new Uri(Connection!.BaseUrl, path.TrimStart('/')))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add(ProtocolHeader, ProtocolVersion.ToString());
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }
        return await http.SendAsync(request, cancellationToken);
    }

    private async Task<string> TokenAsync(bool renew, CancellationToken cancellationToken)
    {
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!renew && token is not null && clock.GetUtcNow() < tokenExpiresAt - RenewBefore)
            {
                return token;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Connection!.BaseUrl, "oauth/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = Connection.InstanceId.ToString(),
                    ["client_secret"] = Connection.Secret,
                }),
            };
            request.Headers.Add(ProtocolHeader, ProtocolVersion.ToString());
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The backoffice refused the instance's token: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}",
                    null, response.StatusCode);
            }
            var issued = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new HttpRequestException("The backoffice sent no token.");
            token = issued.AccessToken;
            tokenExpiresAt = clock.GetUtcNow().AddSeconds(issued.ExpiresIn);
            return token;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
