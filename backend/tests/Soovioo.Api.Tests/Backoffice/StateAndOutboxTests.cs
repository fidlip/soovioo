using System.Net;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Backoffice;

public class StateAndOutboxTests
{
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeBackoffice backoffice = new();
    private readonly Jobs jobs = new();

    private AppDbContext Db(SchoolChangesReport? report = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName);
        if (report is not null)
        {
            options.AddInterceptors(report);
        }
        return new AppDbContext(options.Options, new FakeCurrentSchoolContext(null));
    }

    private static IConfiguration Configuration(string? version = "abc123") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:Version"] = version }).Build();

    [Fact]
    public async Task TheStateIsTheVersionAndWhatTheSchoolsAre()
    {
        var created = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);
        await using (var db = Db())
        {
            db.Schools.AddRange(
                new School { Name = "ZŠ Lesní", Slug = "zs-lesni", Izo = "102345678", RedIzo = "600012345", CreatedAt = created },
                new School { Name = "Demo", Slug = "demo-1", IsDemo = true, CreatedAt = created });
            await db.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            await new InstanceStateReporter(db, backoffice, Configuration()).ReportAsync();
        }

        var (request, body) = Assert.Single(backoffice.Sent);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://backoffice.test/instance-api/v1/state", request.RequestUri!.ToString());
        var state = JsonDocument.Parse(body).RootElement;
        Assert.Equal("abc123", state.GetProperty("appVersion").GetString());
        Assert.Equal(1, state.GetProperty("protocolVersion").GetInt32());
        var schools = state.GetProperty("schools").EnumerateArray().ToList();
        Assert.Equal(2, schools.Count);
        Assert.Equal("zs-lesni", schools[0].GetProperty("slug").GetString());
        Assert.Equal("600012345", schools[0].GetProperty("redIzo").GetString());
        Assert.False(schools[0].GetProperty("isDemo").GetBoolean());
        Assert.True(schools[1].GetProperty("isDemo").GetBoolean());
        Assert.Equal(created, schools[0].GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(
            ["appVersion", "protocolVersion", "schools"],
            state.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["id", "slug", "name", "izo", "redIzo", "isDemo", "createdAt", "storageBytes"],
            schools[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AStateTheBackofficeDoesntTakeIsAnErrorForHangfireToRetry()
    {
        backoffice.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await using var db = Db();

        await Assert.ThrowsAsync<HttpRequestException>(() => new InstanceStateReporter(db, backoffice, Configuration()).ReportAsync());
    }

    [Fact]
    public async Task ANotConnectedInstanceReportsNothing()
    {
        var notConnected = FakeBackoffice.NotConnected();
        await using var db = Db();

        await new InstanceStateReporter(db, notConnected, Configuration()).ReportAsync();

        Assert.Empty(notConnected.Sent);
    }

    [Fact]
    public async Task ASchoolCreatedRenamedOrDeletedQueuesAReportButOtherChangesDont()
    {
        var report = new SchoolChangesReport(backoffice, jobs.Services);
        await using var db = Db(report);
        var school = new School { Name = "ZŠ Lesní", Slug = "zs-lesni" };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        Assert.Equal(1, jobs.Queued);

        school.DigestTime = new TimeOnly(18, 0);
        await db.SaveChangesAsync();
        Assert.Equal(1, jobs.Queued);

        school.Name = "ZŠ Polní";
        await db.SaveChangesAsync();
        Assert.Equal(2, jobs.Queued);

        db.Schools.Remove(school);
        await db.SaveChangesAsync();
        Assert.Equal(3, jobs.Queued);
        Assert.All(jobs.Created, job => Assert.Equal(nameof(InstanceStateReporter.ReportAsync), job.Method.Name));
    }

    [Fact]
    public async Task ANotConnectedInstanceQueuesNoReport()
    {
        await using var db = Db(new SchoolChangesReport(FakeBackoffice.NotConnected(), jobs.Services));
        db.Schools.Add(new School { Name = "ZŠ Lesní", Slug = "zs-lesni" });
        await db.SaveChangesAsync();

        Assert.Equal(0, jobs.Queued);
    }

    private async Task AddEvent(string type, object payload, IBackofficeClient? client = null)
    {
        await using var db = Db();
        Outbox.Add(db, client ?? backoffice, type, payload, clock);
        await db.SaveChangesAsync();
        clock.Now += TimeSpan.FromSeconds(1);
    }

    private async Task RunOutbox()
    {
        await using var db = Db();
        await new OutboxJob(db, backoffice, clock, NullLogger<OutboxJob>.Instance).RunAsync();
    }

    private async Task<List<OutboxEvent>> Pending()
    {
        await using var db = Db();
        return await db.OutboxEvents.OrderBy(e => e.OccurredAt).ToListAsync();
    }

    [Fact]
    public async Task EventsGoInTheOrderTheyHappenedAndAreThenDeleted()
    {
        await AddEvent("First", new { n = 1 });
        await AddEvent("Second", new { n = 2 });
        var ids = (await Pending()).Select(e => e.Id).ToList();

        await RunOutbox();

        Assert.Empty(await Pending());
        Assert.Equal(2, backoffice.Sent.Count);
        Assert.All(backoffice.Sent, s => Assert.Equal("http://backoffice.test/instance-api/v1/events", s.Request.RequestUri!.ToString()));
        var sent = backoffice.Sent.Select(s => JsonDocument.Parse(s.Body).RootElement).ToList();
        Assert.Equal(ids, sent.Select(e => e.GetProperty("id").GetGuid()));
        Assert.Equal(["First", "Second"], sent.Select(e => e.GetProperty("type").GetString()));
        Assert.Equal(2, sent[1].GetProperty("payload").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task AnUndeliveredEventWaitsLongerEachTimeAndHoldsBackTheOnesAfterIt()
    {
        await AddEvent("First", new { n = 1 });
        await AddEvent("Second", new { n = 2 });
        backoffice.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        await RunOutbox();
        var first = (await Pending())[0];
        Assert.Equal(1, first.Attempts);
        Assert.Equal(clock.Now + TimeSpan.FromMinutes(1), first.NextAttemptAt);
        Assert.StartsWith("503", first.LastError);
        Assert.Single(backoffice.Sent);

        await RunOutbox();
        Assert.Single(backoffice.Sent);

        clock.Now = first.NextAttemptAt;
        await RunOutbox();
        Assert.Equal(clock.Now + TimeSpan.FromMinutes(2), (await Pending())[0].NextAttemptAt);

        backoffice.Respond = _ => new HttpResponseMessage(HttpStatusCode.Accepted);
        clock.Now = (await Pending())[0].NextAttemptAt;
        await RunOutbox();
        Assert.Empty(await Pending());
    }

    [Fact]
    public async Task AnEventTheBackofficeRefusesIsDroppedSoTheOthersGo()
    {
        await AddEvent("Bad", new { n = 1 });
        await AddEvent("Good", new { n = 2 });
        backoffice.Respond = request => new HttpResponseMessage(
            request.Content!.ReadAsStringAsync().Result.Contains("Bad") ? HttpStatusCode.BadRequest : HttpStatusCode.Accepted);

        await RunOutbox();

        Assert.Empty(await Pending());
        Assert.Equal(2, backoffice.Sent.Count);
    }

    [Fact]
    public async Task ANotConnectedInstanceRecordsNoEvents()
    {
        await AddEvent("First", new { n = 1 }, FakeBackoffice.NotConnected());

        Assert.Empty(await Pending());
    }

    private class Jobs : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        public int Queued => Created.Count;
        public IServiceProvider Services { get; }

        public Jobs() => Services = new ServiceCollection().AddSingleton<IBackgroundJobClient>(this).BuildServiceProvider();

        public string Create(Job job, IState state)
        {
            Created.Add(job);
            return Created.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
