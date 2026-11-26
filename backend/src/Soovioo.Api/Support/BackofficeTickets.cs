using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soovioo.Api.Backoffice;

namespace Soovioo.Api.Support;

/// <summary>The school's user acting on a ticket; the backoffice trusts the instance with it.</summary>
public record TicketActor(int UserId, string Name);

public record BackofficeTicketSummary(
    int Id, int SchoolId, string Title, SupportTicketStatus Status, DateTimeOffset CreatedAt, DateOnly DueDate,
    bool Overdue, string CreatedByName, bool CreatedBySupport, int? AssigneeId, string? AssigneeName,
    DateTimeOffset LastActivityAt);

/// <param name="Kind">The backoffice's; the school shows the ones it knows.</param>
public record BackofficeTicketEntry(
    int Id, DateTimeOffset At, string Kind, int? AuthorUserId, string AuthorName, bool BySupport, bool Internal,
    string? Text, SupportTicketStatus? Status, DateTimeOffset? Until, DateTimeOffset? EditedAt, bool Deleted);

/// <param name="Time">The ticket's support time, estimates and the school's month, passed on as it is.</param>
public record BackofficeTicket(
    int Id, int SchoolId, string Title, string Description, SupportTicketStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StatusChangedAt, DateOnly DueDate, bool Overdue, string PlanName, int? CreatedByUserId,
    string CreatedByName, bool CreatedBySupport, int? AssigneeId, string? AssigneeName, DateTimeOffset? AutoClosesAt,
    DateTimeOffset? ReopenableUntil, DateTimeOffset? AccessRequestedAt, List<BackofficeTicketEntry> Entries,
    JsonElement Time);

/// <summary>The backoffice refused it, saying why in Czech for the school's user.</summary>
public class TicketRefusedException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>The instance isn't connected to a backoffice, or it can't be reached now.</summary>
public class SupportUnavailableException(bool connected, Exception? inner = null)
    : Exception(connected ? "The backoffice is unreachable." : "The instance has no backoffice.", inner)
{
    public bool Connected { get; } = connected;
}

/// <summary>Support tickets kept in the backoffice (<c>instance-api/v1/schools/{schoolId}/tickets</c>).</summary>
public interface IBackofficeTickets
{
    bool Connected { get; }

    Task<List<BackofficeTicketSummary>> ListAsync(int schoolId);

    Task<BackofficeTicket> GetAsync(int schoolId, int id);

    Task<BackofficeTicket> CreateAsync(int schoolId, TicketActor by, string title, string? description, bool estimateRequested);

    /// <summary><paramref name="action"/> relative to the ticket, e.g. <c>messages</c> or <c>estimates/3/approve</c>.</summary>
    Task<BackofficeTicket> ActAsync(int schoolId, int id, TicketActor by, HttpMethod method, string action, object? body = null);

    /// <summary>The school's support time in a month (<c>yyyy-MM</c>, none = this one).</summary>
    Task<JsonElement> UsageAsync(int schoolId, string? month);
}

public class BackofficeTickets(IBackofficeClient backoffice, ILogger<BackofficeTickets> logger) : IBackofficeTickets
{
    public const string ActorHeader = "Soovioo-Actor";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public bool Connected => backoffice.Connection is not null;

    public Task<List<BackofficeTicketSummary>> ListAsync(int schoolId) =>
        SendAsync<List<BackofficeTicketSummary>>(HttpMethod.Get, Tickets(schoolId), null, null);

    public Task<BackofficeTicket> GetAsync(int schoolId, int id) =>
        SendAsync<BackofficeTicket>(HttpMethod.Get, $"{Tickets(schoolId)}/{id}", null, null);

    public Task<BackofficeTicket> CreateAsync(
        int schoolId, TicketActor by, string title, string? description, bool estimateRequested) =>
        SendAsync<BackofficeTicket>(HttpMethod.Post, Tickets(schoolId), by,
            new { title, description, estimateRequested });

    public Task<BackofficeTicket> ActAsync(
        int schoolId, int id, TicketActor by, HttpMethod method, string action, object? body = null) =>
        SendAsync<BackofficeTicket>(method, $"{Tickets(schoolId)}/{id}/{action}", by, body);

    public Task<JsonElement> UsageAsync(int schoolId, string? month) =>
        SendAsync<JsonElement>(HttpMethod.Get,
            $"instance-api/v1/schools/{schoolId}/support-usage{(month is null ? "" : $"?month={Uri.EscapeDataString(month)}")}",
            null, null);

    private static string Tickets(int schoolId) => $"instance-api/v1/schools/{schoolId}/tickets";

    public static string ActorValue(TicketActor by) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { userId = by.UserId, name = by.Name }, Json));

    private async Task<T> SendAsync<T>(HttpMethod method, string path, TicketActor? by, object? body)
    {
        if (!Connected)
        {
            throw new SupportUnavailableException(connected: false);
        }
        HttpResponseMessage? response;
        try
        {
            using var content = method == HttpMethod.Get || method == HttpMethod.Delete
                ? null
                : JsonContent.Create(body ?? new { }, options: Json);
            response = await backoffice.SendAsync(method, path, content, headers: by is null
                ? null
                : new Dictionary<string, string> { [ActorHeader] = ActorValue(by) });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "Support tickets: the backoffice is unreachable ({Method} {Path}).", method, path);
            throw new SupportUnavailableException(connected: true, e);
        }
        using (response)
        {
            if (response is null)
            {
                throw new SupportUnavailableException(connected: false);
            }
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(Json)
                       ?? throw new SupportUnavailableException(connected: true);
            }
            var status = (int)response.StatusCode;
            if (status is >= 400 and < 500 && response.StatusCode != HttpStatusCode.Unauthorized)
            {
                throw new TicketRefusedException(status, await DetailAsync(response) ?? "Požadavek se nepodařilo provést.");
            }
            logger.LogWarning("Support tickets: the backoffice answered {Status} ({Method} {Path}).", status, method, path);
            throw new SupportUnavailableException(connected: true);
        }
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
