using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

/// <summary>A school's support plan — how many working days a support
/// ticket may take.</summary>
public record SupportPlanDto(string Code, string Name, int ResponseWorkingDays);

/// <summary>Bound from the <c>SupportPlans</c> config section.</summary>
public class SupportPlanOptions
{
    /// <summary>The backoffice, e.g. <c>http://backoffice:8080</c>; empty =
    /// no service yet, every school has <see cref="Default"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sent as <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>For a school the service doesn't know, or while it's
    /// unreachable or not configured.</summary>
    public SupportPlanDto Default { get; set; } = new("Free", "Free", 5);

    /// <summary>The plan every demo school has, asked for by its code.</summary>
    public string DemoPlanCode { get; set; } = "Demo";
}

/// <summary>Asks the backoffice for a school's plan: a demo
/// school's is the demo plan (<c>GET {BaseUrl}/plans/{code}</c>), any other's
/// the one assigned to it (<c>GET {BaseUrl}/schools/{id}/plan</c>). Never fails:
/// without the service or for an unknown school or plan it's the configured
/// default; when the call goes wrong it's the plan the service last gave
/// (kept in memory), else the default.</summary>
public class SupportPlanClient(
    HttpClient http, IOptions<SupportPlanOptions> options, IMemoryCache cache, ILogger<SupportPlanClient> logger)
{
    public async Task<SupportPlanDto> GetPlanAsync(School school)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.Default;
        }
        var path = school.IsDemo
            ? $"plans/{Uri.EscapeDataString(settings.DemoPlanCode)}"
            : $"schools/{school.Id}/plan";
        var key = $"support-plan:{path}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/{path}");
            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                request.Headers.Add("X-Api-Key", settings.ApiKey);
            }
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                cache.Remove(key);
                return settings.Default;
            }
            response.EnsureSuccessStatusCode();
            var plan = await response.Content.ReadFromJsonAsync<SupportPlanDto>();
            if (plan is { ResponseWorkingDays: >= 0 } && !string.IsNullOrEmpty(plan.Code))
            {
                return cache.Set(key, plan);
            }
            logger.LogWarning("Support plan of school {SchoolId} is not valid, using the last known one or the default.", school.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Support plan of school {SchoolId} unavailable, using the last known one or the default.", school.Id);
        }
        return cache.TryGetValue(key, out SupportPlanDto? known) && known is not null ? known : settings.Default;
    }
}
