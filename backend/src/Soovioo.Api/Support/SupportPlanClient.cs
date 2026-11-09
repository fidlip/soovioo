using System.Net;
using Microsoft.Extensions.Options;

namespace Soovioo.Api.Support;

/// <summary>A school's support plan — how many working days a support
/// ticket may take.</summary>
public record SupportPlanDto(string Code, string Name, int ResponseWorkingDays);

/// <summary>Bound from the <c>SupportPlans</c> config section.</summary>
public class SupportPlanOptions
{
    /// <summary>The plans service, e.g. <c>https://soovioo.cz/api</c>; empty =
    /// no service yet, every school has <see cref="Default"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sent as <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>For a school the service doesn't know, or while it's
    /// unreachable or not configured.</summary>
    public SupportPlanDto Default { get; set; } = new("Free", "Free", 5);
}

/// <summary>Asks the separate plans service (<c>GET {BaseUrl}/schools/{id}/plan</c>)
/// for a school's plan. Never fails: without the service, for an unknown
/// school or when the call goes wrong it's the configured default.</summary>
public class SupportPlanClient(HttpClient http, IOptions<SupportPlanOptions> options, ILogger<SupportPlanClient> logger)
{
    public async Task<SupportPlanDto> GetPlanAsync(int schoolId)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.Default;
        }
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/schools/{schoolId}/plan");
            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                request.Headers.Add("X-Api-Key", settings.ApiKey);
            }
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return settings.Default;
            }
            response.EnsureSuccessStatusCode();
            var plan = await response.Content.ReadFromJsonAsync<SupportPlanDto>();
            return plan is { ResponseWorkingDays: >= 0 } && !string.IsNullOrEmpty(plan.Code) ? plan : settings.Default;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Support plan of school {SchoolId} unavailable, using the default.", schoolId);
            return settings.Default;
        }
    }
}
