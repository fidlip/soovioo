using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Backoffice;

/// <summary>An event for the backoffice, written in the same transaction as
/// the change it's about, so an outage of the backoffice doesn't lose it.
/// <see cref="OutboxJob"/> sends it and then deletes it.</summary>
public class OutboxEvent
{
    /// <summary>The backoffice keeps an event once per id.</summary>
    public Guid Id { get; set; }

    public required string Type { get; set; }

    /// <summary>JSON object.</summary>
    public required string Payload { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}

public static class Outbox
{
    /// <summary>Adds the event to <paramref name="db"/>'s pending changes; it's
    /// saved with them. A not connected instance records nothing.</summary>
    public static void Add(AppDbContext db, IBackofficeClient backoffice, string type, object payload, TimeProvider clock)
    {
        if (backoffice.Connection is null)
        {
            return;
        }
        var now = clock.GetUtcNow();
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            OccurredAt = now,
            NextAttemptAt = now,
        });
    }
}

/// <summary>Sends due events to <c>POST instance-api/v1/events</c> in the
/// order they happened. One that fails waits longer each time (from a minute
/// up to an hour) and holds back the ones after it, so the order stays.</summary>
public class OutboxJob(AppDbContext db, IBackofficeClient backoffice, TimeProvider clock, ILogger<OutboxJob> logger)
{
    public const string RecurringJobId = "backoffice-outbox";

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public async Task RunAsync()
    {
        if (backoffice.Connection is null)
        {
            return;
        }
        var events = await db.OutboxEvents.OrderBy(e => e.OccurredAt).Take(100).ToListAsync();
        foreach (var outboxEvent in events)
        {
            var now = clock.GetUtcNow();
            if (outboxEvent.NextAttemptAt > now)
            {
                return;
            }
            var sent = await SendAsync(outboxEvent);
            if (sent == Delivery.Failed)
            {
                outboxEvent.Attempts++;
                var delay = FirstRetryDelay * Math.Pow(2, Math.Min(outboxEvent.Attempts - 1, 10));
                outboxEvent.NextAttemptAt = now + (delay < MaxRetryDelay ? delay : MaxRetryDelay);
                await db.SaveChangesAsync();
                return;
            }
            db.OutboxEvents.Remove(outboxEvent);
            await db.SaveChangesAsync();
        }
    }

    private enum Delivery
    {
        Accepted,
        Rejected,
        Failed,
    }

    /// <summary>An event the backoffice refuses as such (400, 422) is dropped —
    /// sending it again wouldn't help, and it would hold back all after it.</summary>
    private async Task<Delivery> SendAsync(OutboxEvent outboxEvent)
    {
        string error;
        try
        {
            using var payload = JsonDocument.Parse(outboxEvent.Payload);
            using var response = await backoffice.SendAsync(HttpMethod.Post, "instance-api/v1/events", JsonContent.Create(new
            {
                id = outboxEvent.Id,
                type = outboxEvent.Type,
                occurredAt = outboxEvent.OccurredAt,
                payload = payload.RootElement,
            }));
            if (response!.IsSuccessStatusCode)
            {
                return Delivery.Accepted;
            }
            error = $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity)
            {
                logger.LogError("The backoffice refused event {EventId} ({Type}), dropped: {Error}",
                    outboxEvent.Id, outboxEvent.Type, error);
                return Delivery.Rejected;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            error = ex.Message;
        }
        outboxEvent.LastError = error.Length > 2000 ? error[..2000] : error;
        logger.LogWarning("Event {EventId} ({Type}) not delivered to the backoffice: {Error}",
            outboxEvent.Id, outboxEvent.Type, error);
        return Delivery.Failed;
    }
}
