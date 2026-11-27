using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Soovioo.Api.Data;

namespace Soovioo.Api.Support;

/// <summary>Whose changes a context journals (<see cref="AppDbContext.SupportJournal"/>):
/// set for a request in support mode by <see cref="SupportSessionGuardMiddleware"/>,
/// for a background job by what the session started.</summary>
public record SupportJournalScope(int SessionId, int TicketId, int SchoolId, TimeProvider Clock)
{
    public static SupportJournalScope? Of(SupportSession session, TimeProvider clock) =>
        session.TicketId is { } ticketId ? new(session.Id, ticketId, session.SchoolId, clock) : null;
}

/// <summary>Writes the journal of support mode (<see cref="SupportChange"/>)
/// from what a save changes — rows the database deletes or nulls by
/// cascading are loaded first, so they're in it too. Also the values'
/// JSON form and loading rows of any entity type, which the reset
/// (<see cref="SupportReset"/>) shares.</summary>
public static class SupportJournal
{
    public const string FileEntityType = "file";

    /// <summary>The support's own records, logs, tokens and what was sent
    /// already — not the school's data a reset could return.</summary>
    private static readonly HashSet<Type> NotJournaled =
    [
        typeof(Audit.AuditLogEntry), typeof(SupportSession), typeof(SupportChange),
        typeof(SupportTicketEntry), typeof(Auth.RefreshToken),
        typeof(Notifications.PushDevice), typeof(Notifications.Notification), typeof(Demo.DemoEmail),
        typeof(Demo.DemoContact),
        typeof(Timetable.TimetableGeneration),
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new UtcDateTimeOffsetConverter(), new PlainDecimalConverter() },
    };

    public static bool IsJournaled(IEntityType type) => !NotJournaled.Contains(type.ClrType);

    internal static async Task<int> SaveAsync(
        AppDbContext db, SupportJournalScope scope, bool acceptAllChangesOnSuccess, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        await LoadCascadedAsync(db, ct);
        var pending = new List<Pending>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !IsJournaled(entry.Metadata))
            {
                continue;
            }
            var operation = entry.State switch
            {
                EntityState.Added => SupportChangeOperation.Added,
                EntityState.Modified => SupportChangeOperation.Modified,
                _ => SupportChangeOperation.Deleted,
            };
            var before = operation == SupportChangeOperation.Added ? null : Values(entry, original: true);
            var after = operation == SupportChangeOperation.Modified ? Values(entry, original: false) : null;
            if (operation == SupportChangeOperation.Modified && JsonNode.DeepEquals(before, after))
            {
                continue;
            }
            pending.Add(new Pending(entry, operation, operation == SupportChangeOperation.Added ? null : Key(entry),
                before, after));
        }
        if (pending.Count == 0)
        {
            return await db.SaveWithoutJournalAsync(acceptAllChangesOnSuccess, ct);
        }

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var result = await db.SaveWithoutJournalAsync(acceptAllChangesOnSuccess, ct);
        var now = scope.Clock.GetUtcNow();
        foreach (var change in pending)
        {
            db.SupportChanges.Add(new SupportChange
            {
                SessionId = scope.SessionId,
                TicketId = scope.TicketId,
                SchoolId = scope.SchoolId,
                At = now,
                EntityType = change.Entry.Metadata.Name,
                // Added rows have their keys only now.
                KeyJson = change.Key ?? Key(change.Entry),
                Operation = change.Operation,
                OriginalJson = change.Before?.ToJsonString(),
                CurrentJson = (change.Operation == SupportChangeOperation.Added
                    ? Values(change.Entry, original: false)
                    : change.After)?.ToJsonString(),
            });
        }
        await db.SaveWithoutJournalAsync(true, ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
        return result;
    }

    /// <summary>A stored file deleted in support mode stays on disk till the
    /// journal goes. Saves.</summary>
    public static void DeferFileDeletion(AppDbContext db, SupportJournalScope scope, string relativePath)
    {
        db.SupportChanges.Add(new SupportChange
        {
            SessionId = scope.SessionId,
            TicketId = scope.TicketId,
            SchoolId = scope.SchoolId,
            At = scope.Clock.GetUtcNow(),
            EntityType = FileEntityType,
            KeyJson = relativePath,
            Operation = SupportChangeOperation.FileDeleted,
        });
        db.SaveChanges();
    }

    /// <summary>The rows the database would delete or set null along with
    /// the deleted ones, tracked — EF then cascades to them itself.</summary>
    private static async Task LoadCascadedAsync(AppDbContext db, CancellationToken ct)
    {
        var done = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            var deleted = db.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Deleted && done.Add(e.Entity))
                .ToList();
            if (deleted.Count == 0)
            {
                return;
            }
            foreach (var entry in deleted)
            {
                foreach (var fk in entry.Metadata.GetReferencingForeignKeys())
                {
                    if (fk.IsOwnership || fk.DeclaringEntityType.IsOwned() || !IsJournaled(fk.DeclaringEntityType)
                        || fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull))
                    {
                        continue;
                    }
                    var values = fk.PrincipalKey.Properties.Select(p => entry.Property(p.Name).OriginalValue).ToArray();
                    if (values.All(v => v is not null))
                    {
                        await LoadAsync(db, fk.DeclaringEntityType, fk.Properties, values, ct);
                    }
                }
            }
            db.ChangeTracker.CascadeChanges();
        }
    }

    public static JsonObject Values(EntityEntry entry, bool original)
    {
        var values = new JsonObject();
        foreach (var property in entry.Properties)
        {
            values[property.Metadata.Name] = ToNode(
                original ? property.OriginalValue : property.CurrentValue, property.Metadata.ClrType);
        }
        return values;
    }

    public static string Key(EntityEntry entry) =>
        KeyOf(entry.Metadata.FindPrimaryKey()!.Properties, p => entry.Property(p.Name).CurrentValue);

    public static string KeyOf(IReadOnlyList<IProperty> properties, Func<IProperty, object?> value)
    {
        var key = new JsonObject();
        foreach (var property in properties)
        {
            key[property.Name] = ToNode(value(property), property.ClrType);
        }
        return key.ToJsonString();
    }

    public static JsonNode? ToNode(object? value, Type type) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, type, Json);

    public static object? FromNode(JsonNode? node, Type type) => node?.Deserialize(type, Json);

    private static readonly MethodInfo LoadTypedMethod =
        typeof(SupportJournal).GetMethod(nameof(LoadTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Tracked rows of <paramref name="type"/> (not an owned one)
    /// whose <paramref name="properties"/> equal <paramref name="values"/> —
    /// across every school and query filter.</summary>
    public static Task<List<object>> LoadAsync(
        AppDbContext db, IEntityType type, IReadOnlyList<IProperty> properties, object?[] values,
        CancellationToken ct = default) =>
        (Task<List<object>>)LoadTypedMethod.MakeGenericMethod(type.ClrType)
            .Invoke(null, [db, type, properties, values, ct])!;

    private static async Task<List<object>> LoadTypedAsync<T>(
        AppDbContext db, IEntityType type, IReadOnlyList<IProperty> properties, object?[] values, CancellationToken ct)
        where T : class
    {
        IQueryable<T> rows = type.HasSharedClrType ? db.Set<T>(type.Name) : db.Set<T>();
        var row = Expression.Parameter(typeof(T), "row");
        Expression? match = null;
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var equal = Expression.Equal(
                Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], row, Expression.Constant(property.Name)),
                Expression.Constant(Convert(values[i], property.ClrType), property.ClrType));
            match = match is null ? equal : Expression.AndAlso(match, equal);
        }
        var list = await rows.IgnoreQueryFilters().Where(Expression.Lambda<Func<T, bool>>(match!, row)).ToListAsync(ct);
        return list.Cast<object>().ToList();
    }

    private static object? Convert(object? value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return value is null || target.IsInstanceOfType(value) || target.IsEnum
            ? value
            : System.Convert.ChangeType(value, target);
    }

    private record Pending(
        EntityEntry Entry, SupportChangeOperation Operation, string? Key, JsonObject? Before, JsonObject? After);

    /// <summary>The database gives instants back in UTC.</summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime());
    }

    /// <summary>1.50 from a numeric(…, 2) column is the 1.5 that was saved.</summary>
    private sealed class PlainDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value / 1.000000000000000000000000000000000m);
    }
}
