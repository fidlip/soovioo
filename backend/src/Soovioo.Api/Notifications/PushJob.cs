using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Notifications;

/// <summary>Hangfire job every minute (registered in Program.cs) — sends the
/// waiting notifications (<see cref="Notification.Push"/>) to each user's
/// phones, several at once as one push. During a user's night quiet they
/// wait; after it they go out together.</summary>
public class PushJob(AppDbContext db, IPushSender sender, TimeProvider clock)
{
    public const string GenericBody = "Podrobnosti najdete v aplikaci Soovioo.";

    /// <summary>The push's title when it can't carry the text.</summary>
    public static string GenericTitle(NotificationType type) => type switch
    {
        NotificationType.Grade => "Nová známka",
        NotificationType.Message => "Nová zpráva",
        NotificationType.Absence => "Zapsaná absence",
        NotificationType.Excuse => "Vyřízená omluvenka",
        NotificationType.Homework => "Nový domácí úkol",
        NotificationType.Event => "Akce v kalendáři",
        NotificationType.Photo => "Fotka ke schválení",
        NotificationType.Timetable => "Rozvrh je vygenerovaný",
        _ => "Nové upozornění",
    };

    public async Task RunAsync()
    {
        var now = clock.GetUtcNow();
        var pending = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.Push && n.PushedAt == null)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToListAsync();
        if (pending.Count == 0)
        {
            return;
        }
        var userIds = pending.Select(n => n.UserId).Distinct().ToList();
        var users = await db.Users.IgnoreQueryFilters()
            .Include(u => u.School)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
        var devices = (await db.PushDevices.IgnoreQueryFilters()
                .Where(d => userIds.Contains(d.UserId))
                .ToListAsync())
            .ToLookup(d => d.UserId);

        foreach (var group in pending.GroupBy(n => n.UserId))
        {
            var user = users[group.Key];
            if (IsQuiet(user, now))
            {
                continue;
            }
            var unread = group.Where(n => n.ReadAt == null).ToList();
            if (unread.Count > 0 && user.DeactivatedAt == null && user.School?.IsDemo == false)
            {
                var message = MessageFor(unread, user.School.PushFullText && user.PushFullTextAt != null);
                foreach (var device in devices[user.Id])
                {
                    if (await sender.SendAsync(device.Token, message) == PushResult.InvalidToken)
                    {
                        db.PushDevices.Remove(device);
                    }
                }
            }
            foreach (var notification in group)
            {
                notification.PushedAt = now;
            }
            await db.SaveChangesAsync();
        }
    }

    /// <summary>In the user's night quiet (Prague time) — their own, the
    /// school's, or none.</summary>
    public static bool IsQuiet(User user, DateTimeOffset now)
    {
        if (user.PushQuietOff)
        {
            return false;
        }
        var (from, to) = user.PushQuietFrom is { } ownFrom && user.PushQuietTo is { } ownTo
            ? (ownFrom, ownTo)
            : (user.School!.PushQuietFrom, user.School.PushQuietTo);
        var time = TimeOnly.FromDateTime(SchoolClock.Local(now));
        return from < to
            ? time >= from && time < to
            : from > to && (time >= from || time < to);
    }

    /// <summary>One notification as it is; more of them as their count and
    /// the newest titles. Without the full text only the kind of event.</summary>
    public static PushMessage MessageFor(IReadOnlyList<Notification> items, bool fullText)
    {
        var newest = items[^1];
        var data = new Dictionary<string, string> { ["page"] = items.Count == 1 ? newest.Page : "notifications" };
        if (items.Count == 1 && newest.TargetId is { } targetId)
        {
            data["targetId"] = targetId.ToString();
        }
        if (items.Count == 1)
        {
            return fullText
                ? new PushMessage(newest.Title, newest.Body ?? "", data)
                : new PushMessage(GenericTitle(newest.Type), GenericBody, data);
        }
        var title = NotificationDigestJob.Count(items.Count);
        var body = fullText
            ? string.Join(" · ", items.AsEnumerable().Reverse().Take(3).Select(n => n.Title))
            : string.Join(", ", items.Select(n => GenericTitle(n.Type)).Distinct());
        return new PushMessage(title, body, data);
    }
}
