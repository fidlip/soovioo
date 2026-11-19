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
    /// <summary>For a school the backoffice doesn't know, or while it's
    /// unreachable or the instance isn't connected to it.</summary>
    public SupportPlanDto Default { get; set; } = new("Free", "Free", 5);

    /// <summary>The plan every demo school has, asked for by its code.</summary>
    public string DemoPlanCode { get; set; } = "Demo";
}

/// <summary>Asks the backoffice for a school's plan: a demo school's is the
/// demo plan (<c>GET instance-api/v1/plans/{code}</c>), any other's the one
/// assigned to it (<c>GET instance-api/v1/schools/{id}/plan</c>). Never fails:
/// without a backoffice or for an unknown school or plan it's the configured
/// default; when the call goes wrong it's the plan the backoffice last gave
/// (kept in memory until <see cref="Forget"/>), else the default.</summary>
public class SupportPlanClient(
    Backoffice.IBackofficeClient backoffice, IOptions<SupportPlanOptions> options, IMemoryCache cache,
    ILogger<SupportPlanClient> logger)
{
    public async Task<SupportPlanDto> GetPlanAsync(School school)
    {
        var settings = options.Value;
        if (backoffice.Connection is null)
        {
            return settings.Default;
        }
        var path = school.IsDemo
            ? $"instance-api/v1/plans/{Uri.EscapeDataString(settings.DemoPlanCode)}"
            : $"instance-api/v1/schools/{school.Id}/plan";
        var key = CacheKey(path);
        try
        {
            using var response = await backoffice.SendAsync(HttpMethod.Get, path);
            if (response!.StatusCode == HttpStatusCode.NotFound)
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

    /// <summary>The backoffice changed the school's plan (<c>SchoolPlanChanged</c>):
    /// the one kept from before no longer stands in when it's unreachable.</summary>
    public void Forget(int schoolId) => cache.Remove(CacheKey($"instance-api/v1/schools/{schoolId}/plan"));

    private static string CacheKey(string path) => $"support-plan:{path}";
}
