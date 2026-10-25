using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Notifications;

/// <summary>The caller's own notifications (the bell) and which kinds they
/// want under it and in the daily e-mail (Nastavení).</summary>
[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    public const int PageSize = 30;

    /// <summary>Newest first; <paramref name="beforeId"/> continues below
    /// the last one shown.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationPageDto>> GetNotifications([FromQuery] int? beforeId)
    {
        var query = Mine();
        if (beforeId is { } before)
        {
            query = query.Where(n => n.Id < before);
        }
        var items = await query
            .OrderByDescending(n => n.Id)
            .Take(PageSize + 1)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Page, n.TargetId, n.CreatedAt, n.ReadAt != null))
            .ToListAsync();
        return Ok(new NotificationPageDto(items.Take(PageSize).ToList(), items.Count > PageSize));
    }

    /// <summary>The bell's and the Zprávy badge's numbers in one request —
    /// the app asks on every navigation and once a minute.</summary>
    [HttpGet("unread-counts")]
    public async Task<ActionResult<UnreadCountsDto>> GetUnreadCounts() =>
        Ok(new UnreadCountsDto(
            await Mine().CountAsync(n => n.ReadAt == null),
            await db.CountUnreadConversationsAsync(CurrentUserId())));

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var notification = await Mine().FirstOrDefaultAsync(n => n.Id == id);
        if (notification is null)
        {
            return NotFound();
        }
        notification.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var now = clock.GetUtcNow();
        foreach (var notification in await Mine().Where(n => n.ReadAt == null).ToListAsync())
        {
            notification.ReadAt = now;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetSettings()
    {
        var user = await MeAsync();
        return user is null ? NotFound() : Ok(SettingsOf(user));
    }

    /// <summary>The kinds under the bell.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> SetSettings(NotificationSettingsRequest request)
    {
        var user = await MeAsync();
        if (user is null)
        {
            return NotFound();
        }
        user.MutedNotifications = request.Muted.Distinct().Order().ToList();
        await db.SaveChangesAsync();
        return Ok(SettingsOf(user));
    }

    /// <summary>The kinds in the daily e-mail and when it comes (null = the
    /// school's time).</summary>
    [HttpPut("email-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> SetEmailSettings(NotificationEmailSettingsRequest request)
    {
        var user = await MeAsync();
        if (user is null)
        {
            return NotFound();
        }
        user.MutedEmailNotifications = request.Muted.Distinct().Order().ToList();
        user.DigestTime = request.DigestTime is { } time ? new TimeOnly(time.Hour, time.Minute) : null;
        await db.SaveChangesAsync();
        return Ok(SettingsOf(user));
    }

    /// <summary>The "odhlásit" link of the daily e-mail — switches off every
    /// kind the school doesn't force, without logging in.</summary>
    [AllowAnonymous]
    [HttpPost("unsubscribe")]
    public async Task<ActionResult<UnsubscribeResultDto>> Unsubscribe(UnsubscribeRequest request)
    {
        var user = string.IsNullOrEmpty(request.Token)
            ? null
            : await db.Users.IgnoreQueryFilters().Include(u => u.School)
                .FirstOrDefaultAsync(u => u.EmailUnsubscribeToken == request.Token && u.ErasedAt == null);
        if (user?.School is null)
        {
            return NotFound();
        }
        user.MutedEmailNotifications = Enum.GetValues<NotificationType>().ToList();
        await db.SaveChangesAsync();
        return Ok(new UnsubscribeResultDto(
            TypesFor(user.Roles).Where(user.School.ForcedEmailNotifications.Contains).ToList()));
    }

    /// <summary>The kinds that can reach this account — staff alone hear
    /// only about messages and events, pupils' and parents' agendas are
    /// theirs.</summary>
    public static IReadOnlyList<NotificationType> TypesFor(IReadOnlyCollection<UserRole> roles)
    {
        if (roles.Contains(UserRole.Parent))
        {
            return Enum.GetValues<NotificationType>();
        }
        if (roles.Contains(UserRole.Student))
        {
            return Enum.GetValues<NotificationType>().Where(t => t != NotificationType.Photo).ToList();
        }
        return [NotificationType.Message, NotificationType.Event];
    }

    private static NotificationSettingsDto SettingsOf(User user)
    {
        var forced = user.School!.ForcedEmailNotifications;
        return new(
            TypesFor(user.Roles)
                .Select(t => new NotificationTypeSettingDto(
                    t, !user.MutedNotifications.Contains(t),
                    forced.Contains(t) || !user.MutedEmailNotifications.Contains(t), forced.Contains(t)))
                .ToList(),
            user.Person?.Email != null, user.DigestTime, user.School.DigestTime);
    }

    private Task<User?> MeAsync()
    {
        var me = CurrentUserId();
        return db.Users.Include(u => u.School).Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == me);
    }

    private IQueryable<Notification> Mine()
    {
        var me = CurrentUserId();
        return db.Notifications.Where(n => n.UserId == me && n.InApp);
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <param name="Page">The app's page without the school part, e.g. <c>grades</c>.</param>
/// <param name="TargetId">What on that page — the conversation for <c>messages</c>.</param>
public record NotificationDto(
    int Id, NotificationType Type, string Title, string? Body, string Page, int? TargetId,
    DateTimeOffset CreatedAt, bool Read);

/// <param name="Messages">Conversations with something unread.</param>
public record UnreadCountsDto(int Notifications, int Messages);

public record NotificationPageDto(IReadOnlyList<NotificationDto> Items, bool HasMore);

/// <param name="Enabled">Under the bell.</param>
/// <param name="EmailForced">The school sends this one by e-mail to everyone.</param>
public record NotificationTypeSettingDto(NotificationType Type, bool Enabled, bool Email, bool EmailForced);

/// <param name="HasEmail">The account has an e-mail address for the daily e-mail to go to.</param>
/// <param name="DigestTime">The user's own time of the daily e-mail, null = <paramref name="SchoolDigestTime"/>.</param>
public record NotificationSettingsDto(
    IReadOnlyList<NotificationTypeSettingDto> Types, bool HasEmail, TimeOnly? DigestTime, TimeOnly SchoolDigestTime);

public record NotificationSettingsRequest(IReadOnlyList<NotificationType> Muted);

public record NotificationEmailSettingsRequest(IReadOnlyList<NotificationType> Muted, TimeOnly? DigestTime);

public record UnsubscribeRequest(string Token);

/// <param name="Forced">Kinds that keep coming — the school sends them to everyone.</param>
public record UnsubscribeResultDto(IReadOnlyList<NotificationType> Forced);
