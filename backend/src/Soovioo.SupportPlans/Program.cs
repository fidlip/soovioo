using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Soovioo.SupportPlans.Data;

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
{
    throw new InvalidOperationException(
        "ApiKey (32+ characters) is not configured — set it via user-secrets or the ApiKey environment variable.");
}
var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);

builder.Services.AddDbContext<PlansDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlansDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
}

app.Use(async (context, next) =>
{
    var sent = Encoding.UTF8.GetBytes(context.Request.Headers["X-Api-Key"].ToString());
    if (!CryptographicOperations.FixedTimeEquals(sent, apiKeyBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapGet("/schools/{schoolId:int}/plan", async (int schoolId, PlansDbContext db, TimeProvider clock) =>
{
    var today = PragueToday(clock);
    var plan = await db.SchoolPlans
        .Where(a => a.SchoolId == schoolId && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today))
        .OrderByDescending(a => a.ValidFrom)
        .Select(a => new PlanResponse(a.Plan.Code, a.Plan.Name, a.Plan.ResponseWorkingDays))
        .FirstOrDefaultAsync();
    return plan is null ? Results.NotFound() : Results.Ok(plan);
});

app.Run();

static DateOnly PragueToday(TimeProvider clock)
{
    var prague = TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var zone) ? zone : TimeZoneInfo.Utc;
    return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), prague).DateTime);
}

/// <summary>The contract the Soovioo app reads (SupportPlanClient).</summary>
public record PlanResponse(string Code, string Name, int ResponseWorkingDays);

public partial class Program;
