using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soovioo.SupportPlans.Data;

namespace Soovioo.SupportPlans.Tests;

public class SchoolPlanEndpointTests : IDisposable
{
    private const string ApiKey = "test-only-support-plans-key-32-characters";
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> _factory;

    public SchoolPlanEndpointTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Testing");
            host.UseSetting("ApiKey", ApiKey);
            host.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<PlansDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PlansDbContext>>();
                services.AddDbContext<PlansDbContext>(o => o.UseInMemoryDatabase(databaseName));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Without_the_right_key_it_is_unauthorized(string? key)
    {
        Seed(new SchoolPlan { SchoolId = 1, Plan = Paid(), ValidFrom = new DateOnly(2026, 1, 1) });

        var response = await Get(1, key);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_the_plan_valid_today_in_the_contract_shape()
    {
        Seed(new SchoolPlan
        {
            SchoolId = 1, Plan = Paid(), ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31),
        });

        var response = await Get(1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            """{"code":"Paid","name":"Placený","responseWorkingDays":2}""",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_school_without_a_plan_valid_today_is_not_found()
    {
        var paid = Paid();
        Seed(
            new SchoolPlan { SchoolId = 1, Plan = paid, ValidFrom = new DateOnly(2025, 1, 1), ValidTo = new DateOnly(2026, 11, 13) },
            new SchoolPlan { SchoolId = 1, Plan = paid, ValidFrom = new DateOnly(2026, 11, 15) },
            new SchoolPlan { SchoolId = 2, Plan = paid, ValidFrom = new DateOnly(2026, 1, 1) });

        Assert.Equal(HttpStatusCode.NotFound, (await Get(1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(3)).StatusCode);
    }

    [Fact]
    public async Task The_first_and_last_day_of_validity_count()
    {
        Seed(new SchoolPlan
        {
            SchoolId = 1, Plan = Paid(), ValidFrom = new DateOnly(2026, 11, 14), ValidTo = new DateOnly(2026, 11, 14),
        });

        Assert.Equal(HttpStatusCode.OK, (await Get(1)).StatusCode);
    }

    [Fact]
    public async Task Today_is_the_day_in_Prague()
    {
        // 23:30 UTC is already 00:30 the next day in Prague.
        _clock.Now = new DateTimeOffset(2026, 11, 13, 23, 30, 0, TimeSpan.Zero);
        Seed(new SchoolPlan { SchoolId = 1, Plan = Paid(), ValidFrom = new DateOnly(2026, 11, 14) });

        Assert.Equal(HttpStatusCode.OK, (await Get(1)).StatusCode);
    }

    [Fact]
    public async Task Of_overlapping_plans_the_one_starting_later_applies()
    {
        Seed(
            new SchoolPlan { SchoolId = 1, Plan = Paid(), ValidFrom = new DateOnly(2026, 1, 1) },
            new SchoolPlan
            {
                SchoolId = 1,
                Plan = new SupportPlan { Code = "Priority", Name = "Prioritní", ResponseWorkingDays = 1 },
                ValidFrom = new DateOnly(2026, 11, 1),
            });

        var plan = await (await Get(1)).Content.ReadFromJsonAsync<PlanResponse>();

        Assert.Equal("Priority", plan?.Code);
    }

    private static SupportPlan Paid() => new() { Code = "Paid", Name = "Placený", ResponseWorkingDays = 2 };

    private void Seed(params SchoolPlan[] assignments)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlansDbContext>();
        db.SchoolPlans.AddRange(assignments);
        db.SaveChanges();
    }

    private async Task<HttpResponseMessage> Get(int schoolId, string? key = ApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/schools/{schoolId}/plan");
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }
        return await _factory.CreateClient().SendAsync(request);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
