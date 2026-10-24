using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.People;

namespace Soovioo.Api.Notifications;

public record NotificationDraft(
    int UserId, NotificationType Type, string Title, string? Body, string Page,
    int? TargetId = null, int? StudentId = null);

/// <summary>Where the agendas send what's new. Adds the notifications;
/// the caller saves. Leaves out deactivated accounts and the kinds a user
/// switched off; an unread notification about the same thing is updated
/// instead of adding another one.</summary>
public class Notifier(AppDbContext db, TimeProvider clock)
{
    public const int PreviewLength = 140;

    public Task NotifyAsync(
        int schoolId, IEnumerable<int> userIds, NotificationType type, string title, string? body, string page,
        int? targetId = null, int? studentId = null, int? exceptUserId = null) =>
        AddAsync(schoolId, userIds
            .Where(id => id != exceptUserId)
            .Select(id => new NotificationDraft(id, type, title, body, page, targetId, studentId))
            .ToList());

    /// <summary>The pupil's own account and their parents', per pupil.</summary>
    public async Task<Dictionary<int, List<int>>> FamiliesAsync(IEnumerable<int> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var parents = await db.ParentStudents
            .Where(ps => ids.Contains(ps.StudentId))
            .ParentAccountLinks()
            .Select(l => new { l.StudentId, l.UserId })
            .ToListAsync();
        var own = await db.Students
            .Where(s => ids.Contains(s.Id) && s.UserId != null)
            .Select(s => new { StudentId = s.Id, UserId = s.UserId!.Value })
            .ToListAsync();
        return parents.Concat(own)
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());
    }

    public async Task<List<int>> FamilyAsync(int studentId) =>
        (await FamiliesAsync([studentId])).GetValueOrDefault(studentId) ?? [];

    /// <summary>The newest message of each conversation, to everyone in it
    /// but its sender. Call after saving — it needs the ids.</summary>
    public Task MessagesAsync(IEnumerable<Conversation> conversations)
    {
        var drafts = new List<NotificationDraft>();
        var schoolId = 0;
        foreach (var conversation in conversations)
        {
            var message = conversation.Messages.MaxBy(m => m.SentAt);
            if (message is null)
            {
                continue;
            }
            schoolId = conversation.SchoolId;
            drafts.AddRange(conversation.Participants
                .Where(p => p.UserId != message.SenderUserId)
                .Select(p => new NotificationDraft(
                    p.UserId, NotificationType.Message, $"Nová zpráva: {conversation.Subject}",
                    Preview($"{message.SenderName}: {message.Body}"), "messages", conversation.Id,
                    conversation.StudentId)));
        }
        return AddAsync(schoolId, drafts);
    }

    public async Task AddAsync(int schoolId, IReadOnlyCollection<NotificationDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            return;
        }
        var userIds = drafts.Select(d => d.UserId).Distinct().ToList();
        var muted = (await db.Users.IgnoreQueryFilters()
                .Where(u => userIds.Contains(u.Id) && u.SchoolId == schoolId && u.DeactivatedAt == null)
                .Select(u => new { u.Id, u.MutedNotifications })
                .ToListAsync())
            .ToDictionary(u => u.Id, u => u.MutedNotifications);
        var wanted = drafts
            .Where(d => muted.TryGetValue(d.UserId, out var off) && !off.Contains(d.Type))
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var targets = wanted.Where(d => d.TargetId != null).Select(d => d.TargetId!.Value).Distinct().ToList();
        var types = wanted.Select(d => d.Type).Distinct().ToList();
        List<Notification> unread = targets.Count == 0
            ? []
            : await db.Notifications.IgnoreQueryFilters()
                .Where(n => n.SchoolId == schoolId && n.ReadAt == null && n.TargetId != null
                    && targets.Contains(n.TargetId.Value) && types.Contains(n.Type) && userIds.Contains(n.UserId))
                .ToListAsync();

        var now = clock.GetUtcNow();
        foreach (var draft in wanted)
        {
            var notification = draft.TargetId is null
                ? null
                : unread.FirstOrDefault(n => n.UserId == draft.UserId && n.Type == draft.Type && n.TargetId == draft.TargetId);
            if (notification is null)
            {
                notification = new Notification
                {
                    SchoolId = schoolId,
                    UserId = draft.UserId,
                    Type = draft.Type,
                    Title = draft.Title,
                    Page = draft.Page,
                    TargetId = draft.TargetId,
                };
                db.Notifications.Add(notification);
                if (draft.TargetId is not null)
                {
                    unread.Add(notification);
                }
            }
            notification.Title = draft.Title;
            notification.Body = draft.Body;
            notification.StudentId = draft.StudentId;
            notification.CreatedAt = now;
        }
    }

    public static string Preview(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= PreviewLength ? flat : flat[..(PreviewLength - 1)].TrimEnd() + "…";
    }

    public static string Format(DateOnly date) => $"{date.Day}. {date.Month}. {date.Year}";
}
