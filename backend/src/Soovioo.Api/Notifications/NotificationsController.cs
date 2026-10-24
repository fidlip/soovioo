using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Notifications;

/// <summary>The caller's own notifications (the bell) and which kinds they
/// want (Nastavení).</summary>
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

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount() =>
        Ok(await Mine().CountAsync(n => n.ReadAt == null));

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
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        return user is null ? NotFound() : Ok(SettingsOf(user));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> SetSettings(NotificationSettingsRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId());
        if (user is null)
        {
            return NotFound();
        }
        user.MutedNotifications = request.Muted.Distinct().Order().ToList();
        await db.SaveChangesAsync();
        return Ok(SettingsOf(user));
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

    private static NotificationSettingsDto SettingsOf(User user) =>
        new(TypesFor(user.Roles).Select(t => new NotificationTypeSettingDto(t, !user.MutedNotifications.Contains(t))).ToList());

    private IQueryable<Notification> Mine()
    {
        var me = CurrentUserId();
        return db.Notifications.Where(n => n.UserId == me);
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <param name="Page">The app's page without the school part, e.g. <c>grades</c>.</param>
/// <param name="TargetId">What on that page — the conversation for <c>messages</c>.</param>
public record NotificationDto(
    int Id, NotificationType Type, string Title, string? Body, string Page, int? TargetId,
    DateTimeOffset CreatedAt, bool Read);

public record NotificationPageDto(IReadOnlyList<NotificationDto> Items, bool HasMore);

public record NotificationTypeSettingDto(NotificationType Type, bool Enabled);

public record NotificationSettingsDto(IReadOnlyList<NotificationTypeSettingDto> Types);

public record NotificationSettingsRequest(IReadOnlyList<NotificationType> Muted);
