using System.Reflection;
using System.Runtime.CompilerServices;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Backoffice;

public record InstanceState(string AppVersion, int ProtocolVersion, List<InstanceSchoolState> Schools);

/// <param name="StorageBytes">Not counted yet.</param>
public record InstanceSchoolState(
    int Id, string Slug, string Name, string? Izo, string? RedIzo, bool IsDemo, DateTimeOffset CreatedAt,
    long? StorageBytes);

/// <summary>Tells the backoffice which schools this instance has
/// (<c>PUT instance-api/v1/state</c>): after start, after a school is created,
/// changed or deleted (<see cref="SchoolChangesReport"/>) and every hour. Only
/// what the schools are — nothing from inside them.</summary>
public class InstanceStateReporter(AppDbContext db, IBackofficeClient backoffice, IConfiguration configuration)
{
    public const string RecurringJobId = "backoffice-state";

    public static string AppVersion(IConfiguration configuration) =>
        configuration["App:Version"] is { Length: > 0 } version
            ? version
            : typeof(InstanceStateReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";

    public async Task<InstanceState> BuildAsync() => new(
        AppVersion(configuration),
        BackofficeClient.ProtocolVersion,
        await db.Schools.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new InstanceSchoolState(s.Id, s.Slug, s.Name, s.Izo, s.RedIzo, s.IsDemo, s.CreatedAt, null))
            .ToListAsync());

    /// <summary>Throws when the backoffice doesn't take it, so Hangfire tries again.</summary>
    [AutomaticRetry(Attempts = 5)]
    public async Task ReportAsync()
    {
        if (backoffice.Connection is null)
        {
            return;
        }
        using var response = await backoffice.SendAsync(
            HttpMethod.Put, "instance-api/v1/state", JsonContent.Create(await BuildAsync()));
        response!.EnsureSuccessStatusCode();
    }
}

/// <summary>A school created, renamed, re-identified or deleted, wherever it
/// happens (the backoffice's commands, demo schools, the wizard), queues
/// <see cref="InstanceStateReporter.ReportAsync"/> once it's saved.</summary>
public class SchoolChangesReport(IBackofficeClient backoffice, IServiceProvider services) : SaveChangesInterceptor
{
    private static readonly string[] ReportedProperties =
        [nameof(School.Name), nameof(School.Slug), nameof(School.Izo), nameof(School.RedIzo), nameof(School.IsDemo)];

    private readonly ConditionalWeakTable<DbContext, object> pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Report(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Report(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        if (context is null || backoffice.Connection is null)
        {
            return;
        }
        var changed = context.ChangeTracker.Entries<School>().Any(e =>
            e.State is EntityState.Added or EntityState.Deleted
            || (e.State == EntityState.Modified
                && ReportedProperties.Any(p => e.Property(p).IsModified)));
        if (changed)
        {
            pending.AddOrUpdate(context, true);
        }
    }

    private void Report(DbContext? context)
    {
        if (context is null || !pending.Remove(context))
        {
            return;
        }
        services.GetRequiredService<IBackgroundJobClient>()
            .Enqueue<InstanceStateReporter>(reporter => reporter.ReportAsync());
    }
}
