using System.Text.Json;
using Soovioo.Api.Support;

namespace Soovioo.Api.Tests.Support;

/// <summary>The backoffice's tickets in memory, with just enough of its rules
/// for the school's side; <see cref="Calls"/> says what it was asked to do and
/// by whom. <see cref="Down"/> makes it unreachable, <see cref="NotConnected"/>
/// an instance without one.</summary>
internal class FakeBackofficeTickets(bool connected = true) : IBackofficeTickets
{
    public static FakeBackofficeTickets NotConnected() => new(connected: false);

    private static readonly JsonElement NoTime = JsonDocument.Parse("""{"estimates":[]}""").RootElement;

    private int nextId = 100;
    private int nextEntryId = 1000;

    public Dictionary<int, BackofficeTicket> Tickets { get; } = [];

    public List<(int SchoolId, int TicketId, string Action, TicketActor By, string Body)> Calls { get; } = [];

    public bool Down { get; set; }

    public bool Connected { get; } = connected;

    /// <summary>A ticket the school's user asked for.</summary>
    public int Seed(int schoolId, TicketActor author, SupportTicketStatus status = SupportTicketStatus.New)
    {
        var id = nextId++;
        Tickets[id] = new BackofficeTicket(
            id, schoolId, "Třídy", "", status, DateTimeOffset.UtcNow, null, new DateOnly(2026, 11, 16), false, "Free",
            author.UserId, author.Name, false, 1, "Pavel z podpory", null, null, null,
            [Entry("Created", author.Name, false, userId: author.UserId)], NoTime);
        return id;
    }

    /// <summary>Changes a ticket as the support would.</summary>
    public void Support(int id, Func<BackofficeTicket, BackofficeTicket> change) => Tickets[id] = change(Tickets[id]);

    public BackofficeTicketEntry Entry(
        string kind, string author, bool bySupport, string? text = null, int? userId = null, SupportTicketStatus? status = null) =>
        new(nextEntryId++, DateTimeOffset.UtcNow, kind, userId, author, bySupport, false, text, status, null, null, false);

    public Task<List<BackofficeTicketSummary>> ListAsync(int schoolId) => Answer(() => Tickets.Values
        .Where(t => t.SchoolId == schoolId)
        .Select(t => new BackofficeTicketSummary(
            t.Id, t.SchoolId, t.Title, t.Status, t.CreatedAt, t.DueDate, t.Overdue, t.CreatedByName, t.CreatedBySupport,
            t.AssigneeId, t.AssigneeName, t.Entries.Max(e => (DateTimeOffset?)e.At) ?? t.CreatedAt))
        .ToList());

    public Task<BackofficeTicket> GetAsync(int schoolId, int id) => Answer(() => Find(schoolId, id));

    public Task<BackofficeTicket> CreateAsync(
        int schoolId, TicketActor by, string title, string? description, bool estimateRequested) => Answer(() =>
    {
        var id = nextId++;
        List<BackofficeTicketEntry> entries = [Entry("Created", by.Name, false, userId: by.UserId)];
        if (estimateRequested)
        {
            entries.Add(Entry("EstimateRequested", by.Name, false, userId: by.UserId));
        }
        Tickets[id] = new BackofficeTicket(
            id, schoolId, title, description ?? "", SupportTicketStatus.New, DateTimeOffset.UtcNow, null,
            new DateOnly(2026, 11, 16), false, "Placený", by.UserId, by.Name, false, null, null, null, null, null,
            entries, NoTime);
        Calls.Add((schoolId, id, "create", by, title));
        return Tickets[id];
    });

    public Task<BackofficeTicket> ActAsync(
        int schoolId, int id, TicketActor by, HttpMethod method, string action, object? body = null) => Answer(() =>
    {
        var ticket = Find(schoolId, id);
        var json = body is null ? "" : JsonSerializer.Serialize(body, BackofficeTickets.Json);
        Calls.Add((schoolId, id, $"{method} {action}", by, json));
        var text = body is null ? null : JsonDocument.Parse(json).RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
        var entries = ticket.Entries.ToList();
        var status = ticket.Status;
        switch (action.Split('/'))
        {
            case ["messages"]:
                entries.Add(Entry("Message", by.Name, false, text, by.UserId));
                break;
            case ["entries", var entryId] when method == HttpMethod.Put:
                entries = entries.Select(e => e.Id == int.Parse(entryId) ? e with { Text = text, EditedAt = DateTimeOffset.UtcNow } : e).ToList();
                break;
            case ["entries", var entryId] when method == HttpMethod.Delete:
                entries = entries.Select(e => e.Id == int.Parse(entryId) ? e with { Text = null, Deleted = true } : e).ToList();
                break;
            case ["cancel"]:
                status = SupportTicketStatus.Cancelled;
                break;
            case ["close"]:
                status = status == SupportTicketStatus.Resolved
                    ? SupportTicketStatus.Closed
                    : throw new TicketRefusedException(409, "Uzavřít jde jen vyřešený požadavek.");
                break;
            case ["return"]:
                status = SupportTicketStatus.Assigned;
                entries.Add(Entry("Message", by.Name, false, text, by.UserId));
                break;
            case ["reopen"]:
                status = SupportTicketStatus.Resolved;
                break;
        }
        return Tickets[id] = ticket with { Status = status, Entries = entries };
    });

    public Task<JsonElement> UsageAsync(int schoolId, string? month) =>
        Answer(() => JsonDocument.Parse($$"""{"month":"{{month ?? "2026-11"}}"}""").RootElement);

    private BackofficeTicket Find(int schoolId, int id) =>
        Tickets.TryGetValue(id, out var ticket) && ticket.SchoolId == schoolId
            ? ticket
            : throw new TicketRefusedException(404, "Požadavek neexistuje.");

    private Task<T> Answer<T>(Func<T> answer)
    {
        if (!Connected || Down)
        {
            throw new SupportUnavailableException(Connected);
        }
        return Task.FromResult(answer());
    }
}
