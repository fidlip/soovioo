using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Support;

public enum SupportResetAction
{
    /// <summary>Added by the support — goes.</summary>
    Delete,

    /// <summary>Deleted by the support — comes back.</summary>
    Restore,

    /// <summary>Changed by the support — gets its values back.</summary>
    Revert,
}

/// <param name="Type">What the row is, in words ("Známka").</param>
/// <param name="Record">Which one ("5.A", "Jana Nováková", "#12").</param>
/// <param name="Skipped">Why it stays as it is; null = it's returned.</param>
public record SupportResetItemDto(
    string Type, string Record, SupportResetAction Action, string? Skipped, IReadOnlyList<int> SessionIds);

public record SupportResetPreviewDto(IReadOnlyList<SupportResetItemDto> Items);

/// <summary>Returns what the support changed in a ticket's sessions
/// (<see cref="SupportChange"/>): each row as it was before them, unless
/// someone changed it since, other data hangs on it now, or what it hangs
/// on is gone — those stay and the preview says why. All in one save.</summary>
public class SupportReset(AppDbContext db, TimeProvider clock)
{
    public const string ChangedLater = "Záznam se po zásahu podpory změnil.";
    public const string ChangedLaterBySupport = "Záznam později změnila podpora při jiné návštěvě.";
    public const string DeletedLater = "Záznam mezitím někdo smazal.";
    public const string CreatedLater = "Záznam mezitím znovu vznikl.";
    public const string HasDependents = "Na záznam se mezitím navázala další data";
    public const string PrincipalGone = "Záznam, ke kterému patří, už neexistuje.";
    public const string Duplicate = "Koliduje s jiným záznamem.";
    public const string MessageRead = "Zprávu si už někdo přečetl.";

    /// <summary>What a reset of the ticket (or of one of its sessions)
    /// would do. Leaves nothing tracked.</summary>
    public async Task<SupportResetPreviewDto> PreviewAsync(int ticketId, int? sessionId)
    {
        var plan = await PlanAsync(ticketId, sessionId);
        db.ChangeTracker.Clear();
        return new SupportResetPreviewDto(plan.Items.Select(i => i.ToDto()).ToList());
    }

    /// <summary>Applies the reset to the tracked rows — the caller saves.</summary>
    public async Task<SupportResetPreviewDto> ApplyAsync(int ticketId, int? sessionId)
    {
        var plan = await PlanAsync(ticketId, sessionId);
        var now = clock.GetUtcNow();
        var returned = plan.Items.Where(i => i.Skipped is null).ToList();
        foreach (var item in returned.Where(i => i.Action == SupportResetAction.Revert))
        {
            SetValues(item.Current!, item.Target!, keys: false);
        }
        foreach (var item in returned.Where(i => i.Action == SupportResetAction.Restore).OrderBy(i => i.Type.IsOwned()))
        {
            Insert(item);
        }
        foreach (var item in returned.Where(i => i.Action == SupportResetAction.Delete))
        {
            item.Current!.State = EntityState.Deleted;
        }
        foreach (var change in returned.Concat(plan.Unchanged).SelectMany(i => i.Changes))
        {
            change.ResetAt = now;
        }
        DeferOrphanedFiles(returned, now);
        return new SupportResetPreviewDto(plan.Items.Select(i => i.ToDto()).ToList());
    }

    /// <summary>Unreset changes of the ticket, per session.</summary>
    public async Task<Dictionary<int, int>> CountsAsync(int ticketId) =>
        await db.SupportChanges.IgnoreQueryFilters()
            .Where(c => c.TicketId == ticketId && c.ResetAt == null && c.Operation != SupportChangeOperation.FileDeleted)
            .GroupBy(c => c.SessionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

    private sealed class Item
    {
        public required IEntityType Type { get; init; }
        public required string Key { get; init; }
        public required List<SupportChange> Changes { get; init; }

        /// <summary>As before the sessions; null = didn't exist.</summary>
        public JsonObject? Target { get; init; }

        /// <summary>As the sessions left it; null = deleted.</summary>
        public JsonObject? Expected { get; init; }

        public EntityEntry? Current { get; set; }
        public string? Skipped { get; set; }

        /// <summary>Of the delete check: rows referencing it.</summary>
        public List<(IForeignKey Fk, EntityEntry Entry)>? Dependents { get; set; }

        public SupportResetAction Action => Target is null
            ? SupportResetAction.Delete
            : Expected is null ? SupportResetAction.Restore : SupportResetAction.Revert;

        public SupportResetItemDto ToDto() => new(
            SupportChangeLabels.Type(Type), SupportChangeLabels.Record(Target ?? Expected!, Key), Action, Skipped,
            Changes.Select(c => c.SessionId).Distinct().ToList());
    }

    private sealed record Plan(List<Item> Items, List<Item> Unchanged);

    private async Task<Plan> PlanAsync(int ticketId, int? sessionId)
    {
        var changes = await db.SupportChanges.IgnoreQueryFilters()
            .Where(c => c.TicketId == ticketId && c.ResetAt == null && c.Operation != SupportChangeOperation.FileDeleted
                        && (sessionId == null || c.SessionId == sessionId))
            .OrderBy(c => c.Id)
            .ToListAsync();
        var items = new List<Item>();
        var unchanged = new List<Item>();
        foreach (var group in changes.GroupBy(c => (c.EntityType, c.KeyJson)))
        {
            if (db.Model.FindEntityType(group.Key.EntityType) is not { } type)
            {
                continue;
            }
            var list = group.ToList();
            var item = new Item
            {
                Type = type,
                Key = group.Key.KeyJson,
                Changes = list,
                Target = list[0].Operation == SupportChangeOperation.Added ? null : Parse(list[0].OriginalJson),
                Expected = list[^1].Operation == SupportChangeOperation.Deleted ? null : Parse(list[^1].CurrentJson),
            };
            if (item.Target is null && item.Expected is null
                || item.Target is not null && item.Expected is not null && JsonNode.DeepEquals(item.Target, item.Expected))
            {
                unchanged.Add(item);
            }
            else
            {
                items.Add(item);
            }
        }
        var byKey = items.ToDictionary(i => (i.Type.Name, i.Key));

        foreach (var item in items)
        {
            item.Current = await FindAsync(item.Type, item.Key);
            item.Skipped = item.Expected is null
                ? item.Current is null ? null : CreatedLater
                : item.Current is null
                    ? DeletedLater
                    : Matches(item.Current, item.Expected) ? null : await ChangedLaterAsync(item);
            if (item.Skipped is null && item.Action == SupportResetAction.Delete
                && item.Current!.Entity is Message message && await ReadAsync(message))
            {
                item.Skipped = MessageRead;
            }
        }

        // What one row can't do may stop another: repeat till nothing changes.
        var principals = new Dictionary<(string, string), bool>();
        bool changed;
        do
        {
            changed = false;
            foreach (var item in items.Where(i => i.Skipped is null))
            {
                item.Skipped = item.Action == SupportResetAction.Delete
                    ? await DependentsAsync(item, byKey)
                    : await PrincipalsAsync(item, byKey, principals) ?? await DuplicateAsync(item, byKey);
                changed |= item.Skipped is not null;
            }
        }
        while (changed);
        return new Plan(items, unchanged);
    }

    private static JsonObject? Parse(string? json) => json is null ? null : JsonNode.Parse(json)!.AsObject();

    /// <summary>Its values as the sessions left them — properties added to
    /// the model since don't count.</summary>
    private static bool Matches(EntityEntry current, JsonObject expected)
    {
        var values = SupportJournal.Values(current, original: false);
        return expected.All(p => !values.ContainsKey(p.Key) || JsonNode.DeepEquals(values[p.Key], p.Value));
    }

    private async Task<string> ChangedLaterAsync(Item item)
    {
        var last = item.Changes[^1].Id;
        var name = item.Type.Name;
        return await db.SupportChanges.IgnoreQueryFilters()
            .AnyAsync(c => c.EntityType == name && c.KeyJson == item.Key && c.Id > last)
            ? ChangedLaterBySupport
            : ChangedLater;
    }

    /// <summary>Someone but its sender has read it.</summary>
    private Task<bool> ReadAsync(Message message) =>
        db.ConversationParticipants.IgnoreQueryFilters()
            .AnyAsync(p => p.ConversationId == message.ConversationId && p.UserId != message.SenderUserId
                           && p.LastReadAt >= message.SentAt);

    /// <summary>A row going may not take the school's data with it: every
    /// row referencing it must go or turn elsewhere in the reset too.</summary>
    private async Task<string?> DependentsAsync(Item item, Dictionary<(string, string), Item> byKey)
    {
        if (item.Dependents is null)
        {
            item.Dependents = [];
            foreach (var fk in item.Type.GetReferencingForeignKeys())
            {
                if (fk.IsOwnership || fk.DeclaringEntityType.IsOwned())
                {
                    continue;
                }
                var values = fk.PrincipalKey.Properties.Select(p => item.Current!.Property(p.Name).CurrentValue).ToArray();
                foreach (var row in await SupportJournal.LoadAsync(db, fk.DeclaringEntityType, fk.Properties, values))
                {
                    item.Dependents.Add((fk, Tracked(row)));
                }
            }
        }
        foreach (var (fk, dependent) in item.Dependents)
        {
            if (!SupportJournal.IsJournaled(fk.DeclaringEntityType)
                && fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull)
            {
                continue;
            }
            var planned = byKey.GetValueOrDefault((fk.DeclaringEntityType.Name, SupportJournal.Key(dependent)));
            var leaves = planned is { Skipped: null }
                         && (planned.Action == SupportResetAction.Delete
                             || fk.Properties.Any(p => !JsonNode.DeepEquals(
                                 planned.Target![p.Name], SupportJournal.ToNode(dependent.Property(p.Name).CurrentValue, p.ClrType))));
            if (!leaves)
            {
                return $"{HasDependents} ({SupportChangeLabels.Type(fk.DeclaringEntityType)}).";
            }
        }
        return null;
    }

    /// <summary>What a returned row refers to must be there, or come back
    /// in the reset too.</summary>
    private async Task<string?> PrincipalsAsync(
        Item item, Dictionary<(string, string), Item> byKey, Dictionary<(string, string), bool> exists)
    {
        foreach (var fk in item.Type.GetForeignKeys())
        {
            var nodes = fk.Properties.Select(p => item.Target![p.Name]).ToList();
            if (nodes.Any(n => n is null)
                || item.Action == SupportResetAction.Revert
                && fk.Properties.All(p => JsonNode.DeepEquals(item.Target![p.Name], item.Expected![p.Name])))
            {
                continue;
            }
            var principal = fk.PrincipalEntityType;
            var key = PrincipalKey(fk, item.Target!);
            if (fk.PrincipalKey.IsPrimaryKey() && byKey.GetValueOrDefault((principal.Name, key)) is { } planned)
            {
                if (planned.Skipped is null ? planned.Action == SupportResetAction.Delete : planned.Action == SupportResetAction.Restore)
                {
                    return PrincipalGone;
                }
                if (planned.Skipped is null)
                {
                    continue;
                }
            }
            if (principal.IsOwned())
            {
                continue;
            }
            if (!exists.TryGetValue((principal.Name, key), out var found))
            {
                var values = fk.Properties.Select((p, i) => SupportJournal.FromNode(nodes[i], p.ClrType)).ToArray();
                found = (await SupportJournal.LoadAsync(db, principal, fk.PrincipalKey.Properties, values)).Count > 0;
                exists[(principal.Name, key)] = found;
            }
            if (!found)
            {
                return PrincipalGone;
            }
        }
        return null;
    }

    /// <summary>A unique index another row holds the values of now.</summary>
    private async Task<string?> DuplicateAsync(Item item, Dictionary<(string, string), Item> byKey)
    {
        if (item.Type.IsOwned())
        {
            return null;
        }
        foreach (var index in item.Type.GetIndexes().Where(i => i.IsUnique))
        {
            var nodes = index.Properties.Select(p => item.Target![p.Name]).ToList();
            if (nodes.Any(n => n is null)
                || item.Action == SupportResetAction.Revert
                && index.Properties.All(p => JsonNode.DeepEquals(item.Target![p.Name], item.Expected![p.Name])))
            {
                continue;
            }
            var values = index.Properties.Select((p, i) => SupportJournal.FromNode(nodes[i], p.ClrType)).ToArray();
            foreach (var row in await SupportJournal.LoadAsync(db, item.Type, index.Properties, values))
            {
                var other = Tracked(row);
                var key = SupportJournal.Key(other);
                if (key == item.Key)
                {
                    continue;
                }
                var planned = byKey.GetValueOrDefault((item.Type.Name, key));
                var moves = planned is { Skipped: null }
                            && (planned.Action == SupportResetAction.Delete
                                || index.Properties.Any(p => !JsonNode.DeepEquals(planned.Target![p.Name], item.Target![p.Name])));
                if (!moves)
                {
                    return Duplicate;
                }
            }
        }
        return null;
    }

    /// <summary>The row as it is now, tracked; null when it's gone.</summary>
    private async Task<EntityEntry?> FindAsync(IEntityType type, string keyJson)
    {
        var key = JsonNode.Parse(keyJson)!.AsObject();
        if (type.FindOwnership() is { } ownership)
        {
            var ownerKey = ownership.Properties.Select(p => SupportJournal.FromNode(key[p.Name], p.ClrType)).ToArray();
            await SupportJournal.LoadAsync(db, ownership.PrincipalEntityType, ownership.PrincipalKey.Properties, ownerKey);
            return db.ChangeTracker.Entries()
                .FirstOrDefault(e => e.Metadata == type && SupportJournal.Key(e) == keyJson);
        }
        var properties = type.FindPrimaryKey()!.Properties;
        var values = properties.Select(p => SupportJournal.FromNode(key[p.Name], p.ClrType)).ToArray();
        var rows = await SupportJournal.LoadAsync(db, type, properties, values);
        return rows.Count == 0 ? null : Tracked(rows[0]);
    }

    /// <summary>The key of the row <paramref name="fk"/> points to from <paramref name="values"/>.</summary>
    private static string PrincipalKey(IForeignKey fk, JsonObject values)
    {
        var key = new JsonObject();
        for (var i = 0; i < fk.Properties.Count; i++)
        {
            key[fk.PrincipalKey.Properties[i].Name] = values[fk.Properties[i].Name]?.DeepClone();
        }
        return key.ToJsonString();
    }

    private EntityEntry Tracked(object entity) =>
        db.ChangeTracker.Entries().First(e => ReferenceEquals(e.Entity, entity));

    private static void SetValues(EntityEntry entry, JsonObject values, bool keys)
    {
        foreach (var property in entry.Properties)
        {
            if ((keys || !property.Metadata.IsPrimaryKey()) && values.TryGetPropertyValue(property.Metadata.Name, out var node))
            {
                var value = SupportJournal.FromNode(node, property.Metadata.ClrType);
                if (!Equals(property.CurrentValue, value))
                {
                    property.CurrentValue = value;
                }
            }
        }
    }

    private void Insert(Item item)
    {
        var type = item.Type;
        if (type.ClrType == typeof(Dictionary<string, object>))
        {
            var bag = new Dictionary<string, object>();
            foreach (var property in type.GetProperties())
            {
                bag[property.Name] = SupportJournal.FromNode(item.Target![property.Name], property.ClrType)!;
            }
            db.Set<Dictionary<string, object>>(type.Name).Add(bag);
            return;
        }
        var entity = Activator.CreateInstance(type.ClrType)!;
        if (type.FindOwnership() is { } ownership)
        {
            var ownerKey = PrincipalKey(ownership, item.Target!);
            var owner = db.ChangeTracker.Entries()
                .First(e => e.Metadata == ownership.PrincipalEntityType && SupportJournal.Key(e) == ownerKey);
            var navigation = ownership.PrincipalToDependent!;
            if (navigation.IsCollection)
            {
                navigation.GetCollectionAccessor()!.Add(owner.Entity, entity, forMaterialization: false);
            }
            else
            {
                owner.Reference(navigation.Name).CurrentValue = entity;
            }
            db.ChangeTracker.DetectChanges();
            SetValues(Tracked(entity), item.Target!, keys: true);
            return;
        }
        var entry = db.Entry(entity);
        SetValues(entry, item.Target!, keys: true);
        entry.State = EntityState.Added;
    }

    /// <summary>Files only the returned rows held — they go with the journal
    /// (<see cref="SupportChangeCleanupJob"/>) unless something holds them then.</summary>
    private void DeferOrphanedFiles(List<Item> returned, DateTimeOffset now)
    {
        foreach (var item in returned.Where(i => i.Expected is not null))
        {
            var kept = item.Target is null ? [] : SupportChangeLabels.Files(item.Type, item.Target).ToHashSet();
            var session = item.Changes[^1];
            foreach (var path in SupportChangeLabels.Files(item.Type, item.Expected!).Where(f => !kept.Contains(f)))
            {
                db.SupportChanges.Add(new SupportChange
                {
                    SessionId = session.SessionId,
                    TicketId = session.TicketId,
                    SchoolId = session.SchoolId,
                    At = now,
                    EntityType = SupportJournal.FileEntityType,
                    KeyJson = path,
                    Operation = SupportChangeOperation.FileDeleted,
                });
            }
        }
    }
}
