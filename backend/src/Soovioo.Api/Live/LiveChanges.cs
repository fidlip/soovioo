using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.ProductFeedback;
using Soovioo.Api.Support;

namespace Soovioo.Api.Live;

/// <summary>Whatever saves what a live page shows tells its tabs
/// (<see cref="LivePublisher"/>) — once it's committed, so the page reading
/// it again finds it: notifications and conversations read or new (the
/// badges of whom they are, and of the accounts linked to them), accounts
/// linked or unlinked, feedback
/// (the SuperAdmins), a support ticket, its thread and its visits (the
/// ticket's detail), a support session ended or switched (its tab, and the
/// banners of whom it is in as and of the ticket's author).</summary>
public class LiveChanges(LivePublisher publisher, ILogger<LiveChanges> logger)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    /// <summary>What changes the support banners (<see cref="SupportPresence"/>)
    /// or the session's tab — not its last request or the connection.</summary>
    private static readonly string[] SessionStateProperties =
    [
        nameof(SupportSession.EndedAt), nameof(SupportSession.ExpiresAt), nameof(SupportSession.AsUserId),
        nameof(SupportSession.AsRoleId),
    ];

    private readonly ConditionalWeakTable<DbContext, State> states = new();

    private sealed class State
    {
        /// <summary>The save under way.</summary>
        public Batch? Saving;

        /// <summary>Saved, waiting for its transaction.</summary>
        public Batch Saved = new();
    }

    private sealed class Batch
    {
        public readonly HashSet<int> Users = [];
        public bool Feedback;
        public readonly List<object> Tickets = [];
        public readonly List<Message> Messages = [];
        public readonly HashSet<Guid> LinkGroups = [];
        public readonly HashSet<int> AuditSessions = [];
        public readonly List<(SupportSession Session, int? AsUserBefore)> Sessions = [];
        public readonly List<SupportSession> Connections = [];

        public bool IsEmpty => Users.Count == 0 && !Feedback && Tickets.Count == 0 && Messages.Count == 0
                               && LinkGroups.Count == 0 && AuditSessions.Count == 0 && Sessions.Count == 0 && Connections.Count == 0;

        public void Add(Batch other)
        {
            Users.UnionWith(other.Users);
            Feedback |= other.Feedback;
            Tickets.AddRange(other.Tickets);
            Messages.AddRange(other.Messages);
            LinkGroups.UnionWith(other.LinkGroups);
            AuditSessions.UnionWith(other.AuditSessions);
            Sessions.AddRange(other.Sessions);
            Connections.AddRange(other.Connections);
        }
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context);
        return result;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Drop(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        PublishAsync(eventData.Context).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        PublishAsync(eventData.Context);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Discard(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        context.ChangeTracker.DetectChanges();
        var batch = new Batch();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                Collect(entry, batch);
            }
        }
        states.GetOrCreateValue(context).Saving = batch.IsEmpty ? null : batch;
    }

    private static void Collect(EntityEntry entry, Batch batch)
    {
        switch (entry.Entity)
        {
            // Not when it's only been pushed or e-mailed.
            case Notification n when entry.State != EntityState.Modified
                                     || IsModified(entry, nameof(n.ReadAt)) || IsModified(entry, nameof(n.InApp))
                                     || IsModified(entry, nameof(n.CreatedAt)):
                batch.Users.Add(n.UserId);
                break;
            // Linked or unlinked: the accounts menu, from the same request.
            case AccountLink l when entry.State != EntityState.Modified:
                batch.Users.Add(l.UserId);
                batch.LinkGroups.Add(l.GroupId);
                break;
            case ConversationParticipant p when entry.State != EntityState.Modified || IsModified(entry, nameof(p.LastReadAt)):
                batch.Users.Add(p.UserId);
                break;
            case Message m:
                batch.Messages.Add(m);
                break;
            case Feedback when entry.State != EntityState.Modified || IsModified(entry, nameof(Feedback.SeenAt)):
                batch.Feedback = true;
                break;
            case SupportTicketEntry or SupportChange:
                batch.Tickets.Add(entry.Entity);
                break;
            case AuditLogEntry { SupportSessionId: { } sessionId }:
                batch.AuditSessions.Add(sessionId);
                break;
            case SupportSession s when entry.State == EntityState.Added || SessionStateProperties.Any(p => IsModified(entry, p)):
                batch.Sessions.Add((s, entry.State == EntityState.Added
                    ? null
                    : entry.Property(nameof(s.AsUserId)).OriginalValue as int?));
                break;
            case SupportSession s when IsModified(entry, nameof(s.DisconnectedAt)):
                batch.Connections.Add(s);
                break;
        }
    }

    private static bool IsModified(EntityEntry entry, string property) => entry.Property(property).IsModified;

    private async Task SavedAsync(DbContext? context)
    {
        if (context is null || !states.TryGetValue(context, out var state) || state.Saving is not { } batch)
        {
            return;
        }
        state.Saving = null;
        state.Saved.Add(batch);
        if (context.Database.CurrentTransaction is null)
        {
            await PublishAsync(context);
        }
    }

    private void Drop(DbContext? context)
    {
        if (context is not null && states.TryGetValue(context, out var state))
        {
            state.Saving = null;
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
        {
            states.Remove(context);
        }
    }

    private async Task PublishAsync(DbContext? context)
    {
        if (context is not AppDbContext db || !states.TryGetValue(db, out var state) || state.Saved.IsEmpty)
        {
            return;
        }
        var batch = state.Saved;
        state.Saved = new Batch();
        try
        {
            await PublishAsync(db, batch);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live events not published");
        }
    }

    private async Task PublishAsync(AppDbContext db, Batch batch)
    {
        var users = new HashSet<int>(batch.Users);
        var conversations = batch.Messages.Select(m => m.ConversationId).Distinct().ToList();
        if (conversations.Count > 0)
        {
            users.UnionWith(await db.ConversationParticipants.IgnoreQueryFilters().AsNoTracking()
                .Where(p => conversations.Contains(p.ConversationId))
                .Select(p => p.UserId)
                .ToListAsync());
        }

        if (batch.LinkGroups.Count > 0)
        {
            users.UnionWith(await db.AccountLinks.AsNoTracking()
                .Where(l => batch.LinkGroups.Contains(l.GroupId))
                .Select(l => l.UserId)
                .ToListAsync());
        }

        var tickets = batch.Tickets
            .Select(t => t switch
            {
                SupportTicketEntry entry => (entry.SchoolId, entry.TicketId),
                SupportChange change => (change.SchoolId, change.TicketId),
                _ => (0, 0),
            })
            .Concat(batch.Connections.Select(s => (s.SchoolId, s.TicketId ?? 0)))
            .ToHashSet();
        if (batch.AuditSessions.Count > 0)
        {
            tickets.UnionWith((await db.SupportSessions.IgnoreQueryFilters().AsNoTracking()
                    .Where(s => batch.AuditSessions.Contains(s.Id) && s.TicketId != null)
                    .Select(s => new { s.SchoolId, TicketId = s.TicketId!.Value })
                    .ToListAsync())
                .Select(s => (s.SchoolId, s.TicketId)));
        }
        tickets.RemoveWhere(t => t.Item2 == 0);

        if (users.Count > 0)
        {
            users.UnionWith((await db.LinkedWithAsync(users)).Values.SelectMany(ids => ids));
        }
        await publisher.CountsAsync(users);
        if (batch.Feedback)
        {
            await publisher.FeedbackAsync();
        }
        await publisher.TicketsAsync(tickets);
        await publisher.SessionsAsync(db, batch.Sessions);
    }
}
