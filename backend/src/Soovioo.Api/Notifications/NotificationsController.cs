using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Notifications;

/// <summary>The caller's own notifications (the bell), which kinds they
/// want under it, in the daily e-mail and as a push (Nastavení), and the
/// phones the push goes to.</summary>
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

    /// <summary>Push: the kinds, the whole text or just the kind of event
    /// (only while the school allows it), and the night quiet.</summary>
    [HttpPut("push-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> SetPushSettings(NotificationPushSettingsRequest request)
    {
        var user = await MeAsync();
        if (user is null)
        {
            return NotFound();
        }
        if (request.FullText && !user.School!.PushFullText)
        {
            return BadRequest("Škola celý text v upozorněních na mobil nepovoluje.");
        }
        if (request.Quiet == PushQuietMode.Own && (request.QuietFrom is null || request.QuietTo is null))
        {
            return BadRequest("Vyberte začátek a konec nočního klidu.");
        }
        user.MutedPushNotifications = request.Muted.Distinct().Order().ToList();
        user.PushFullTextAt = request.FullText ? user.PushFullTextAt ?? clock.GetUtcNow() : null;
        user.PushQuietOff = request.Quiet == PushQuietMode.Off;
        user.PushQuietFrom = request.Quiet == PushQuietMode.Own ? Minutes(request.QuietFrom!.Value) : null;
        user.PushQuietTo = request.Quiet == PushQuietMode.Own ? Minutes(request.QuietTo!.Value) : null;
        await db.SaveChangesAsync();
        return Ok(SettingsOf(user));
    }

    /// <summary>The mobile app after logging in and whenever its token
    /// changes. A token another account registered moves to the caller —
    /// the phone was logged in as someone else before.</summary>
    [HttpPost("push-devices")]
    public async Task<IActionResult> RegisterDevice(PushDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096 || !Enum.IsDefined(request.Platform))
        {
            return BadRequest();
        }
        var user = await MeAsync();
        if (user?.SchoolId is null)
        {
            return NotFound();
        }
        var now = clock.GetUtcNow();
        var device = await db.PushDevices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Token == request.Token);
        if (device is null)
        {
            device = new PushDevice { Token = request.Token, CreatedAt = now };
            db.PushDevices.Add(device);
        }
        device.UserId = user.Id;
        device.SchoolId = user.SchoolId.Value;
        device.Platform = request.Platform;
        device.LastSeenAt = now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The mobile app when logging out — nothing more for this
    /// account comes to that phone.</summary>
    [HttpPost("push-devices/remove")]
    public async Task<IActionResult> RemoveDevice(PushDeviceRemoveRequest request)
    {
        var me = CurrentUserId();
        await db.DeleteAsync(db.PushDevices.Where(d => d.UserId == me && d.Token == request.Token));
        return NoContent();
    }

    private static TimeOnly Minutes(TimeOnly time) => new(time.Hour, time.Minute);

    /// <summary>The "odhlásit" link of the daily e-mail — switches off every
    /// kind the school doesn't force, without logging in.</summary>
    [AllowAnonymous]
    [HttpPost("unsubscribe")]
    public async Task<ActionResult<UnsubscribeResultDto>> Unsubscribe(UnsubscribeRequest request)
    {
        var user = string.IsNullOrEmpty(request.Token)
            ? null
            : await db.Users.IgnoreQueryFilters().Include(u => u.School).Include(u => u.Roles).ThenInclude(r => r.Permissions)
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
    /// only about messages, events and changes of their lessons (and generated
    /// timetables when they may edit the timetable), pupils' and parents' agendas are theirs.
    /// Needs the roles' permissions loaded.</summary>
    public static IReadOnlyList<NotificationType> TypesFor(IReadOnlyCollection<Role> roles)
    {
        var timetable = roles.Any(r => r.Permissions.Any(p => p.Permission == Permission.Timetable && p.Scope != PermissionScope.None))
            ? new[] { NotificationType.Timetable }
            : [];
        if (roles.Any(r => r.Kind == RoleKind.Parent))
        {
            return [.. Enum.GetValues<NotificationType>().Where(t => t != NotificationType.Timetable), .. timetable];
        }
        if (roles.Any(r => r.Kind == RoleKind.Student))
        {
            return [.. Enum.GetValues<NotificationType>().Where(t => t is not (NotificationType.Photo or NotificationType.Timetable))];
        }
        return [NotificationType.Message, NotificationType.Event, NotificationType.TimetableChange, .. timetable];
    }

    private NotificationSettingsDto SettingsOf(User user)
    {
        var school = user.School!;
        var forced = school.ForcedEmailNotifications;
        return new(
            TypesFor(user.Roles)
                .Select(t => new NotificationTypeSettingDto(
                    t, !user.MutedNotifications.Contains(t),
                    forced.Contains(t) || !user.MutedEmailNotifications.Contains(t), forced.Contains(t),
                    !user.MutedPushNotifications.Contains(t)))
                .ToList(),
            user.Person?.Email != null, user.DigestTime, school.DigestTime,
            new PushSettingsDto(
                db.PushDevices.Any(d => d.UserId == user.Id),
                school.PushFullText, school.PushFullText && user.PushFullTextAt != null,
                user.PushQuietOff ? PushQuietMode.Off : user.PushQuietFrom is null ? PushQuietMode.School : PushQuietMode.Own,
                user.PushQuietFrom, user.PushQuietTo, school.PushQuietFrom, school.PushQuietTo));
    }

    private Task<User?> MeAsync()
    {
        var me = CurrentUserId();
        return db.Users.Include(u => u.School).Include(u => u.Person).Include(u => u.Roles).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.Id == me);
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
/// <param name="Push">To the phone.</param>
public record NotificationTypeSettingDto(NotificationType Type, bool Enabled, bool Email, bool EmailForced, bool Push);

public enum PushQuietMode
{
    /// <summary>The school's night quiet.</summary>
    School,
    Own,
    Off,
}

/// <param name="HasDevice">The mobile app is logged in as this user on some phone.</param>
/// <param name="FullTextAllowed">The school lets a push carry the whole text.</param>
/// <param name="FullText">The user agreed to it — and the school allows it.</param>
/// <param name="QuietFrom">Own night quiet, set with <see cref="PushQuietMode.Own"/>.</param>
public record PushSettingsDto(
    bool HasDevice, bool FullTextAllowed, bool FullText, PushQuietMode Quiet,
    TimeOnly? QuietFrom, TimeOnly? QuietTo, TimeOnly SchoolQuietFrom, TimeOnly SchoolQuietTo);

/// <param name="HasEmail">The account has an e-mail address for the daily e-mail to go to.</param>
/// <param name="DigestTime">The user's own time of the daily e-mail, null = <paramref name="SchoolDigestTime"/>.</param>
public record NotificationSettingsDto(
    IReadOnlyList<NotificationTypeSettingDto> Types, bool HasEmail, TimeOnly? DigestTime, TimeOnly SchoolDigestTime,
    PushSettingsDto Push);

public record NotificationSettingsRequest(IReadOnlyList<NotificationType> Muted);

public record NotificationEmailSettingsRequest(IReadOnlyList<NotificationType> Muted, TimeOnly? DigestTime);

/// <param name="FullText">Agreeing that a push carries the whole text.</param>
/// <param name="QuietFrom">With <see cref="PushQuietMode.Own"/>.</param>
public record NotificationPushSettingsRequest(
    IReadOnlyList<NotificationType> Muted, bool FullText, PushQuietMode Quiet, TimeOnly? QuietFrom, TimeOnly? QuietTo);

/// <param name="Token">The app's FCM registration token.</param>
public record PushDeviceRequest(string Token, PushPlatform Platform);

public record PushDeviceRemoveRequest(string Token);

public record UnsubscribeRequest(string Token);

/// <param name="Forced">Kinds that keep coming — the school sends them to everyone.</param>
public record UnsubscribeResultDto(IReadOnlyList<NotificationType> Forced);
