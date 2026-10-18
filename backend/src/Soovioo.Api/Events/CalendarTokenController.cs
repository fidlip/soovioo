using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Events;

/// <summary>Manages the caller's own <see cref="User.CalendarToken"/> — the
/// secret that gates the unauthenticated <see cref="CalendarFeedController"/>
/// ICS endpoint a calendar app subscribes to.</summary>
[Authorize]
[ApiController]
[Route("api/users/me/calendar-feed-url")]
public class CalendarTokenController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CalendarFeedUrlDto>> GetFeedUrl()
    {
        var user = await CurrentUserAsync();
        user.CalendarToken ??= GenerateToken();
        await db.SaveChangesAsync();

        return Ok(BuildDto(user.CalendarToken));
    }

    /// <summary>Invalidates the previous link (e.g. after it leaked) —
    /// every calendar app that had the old URL stops getting updates until
    /// re-subscribed with the new one.</summary>
    [HttpPost("regenerate")]
    public async Task<ActionResult<CalendarFeedUrlDto>> RegenerateFeedUrl()
    {
        var user = await CurrentUserAsync();
        user.CalendarToken = GenerateToken();
        await db.SaveChangesAsync();

        return Ok(BuildDto(user.CalendarToken));
    }

    private async Task<User> CurrentUserAsync()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Users.FirstAsync(u => u.Id == userId);
    }

    private CalendarFeedUrlDto BuildDto(string token)
    {
        var feedUrl = $"{Request.Scheme}://{Request.Host}/api/calendar-feed/{token}.ics";
        return new CalendarFeedUrlDto(feedUrl);
    }

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

/// <summary>Plain https URL — the frontend derives the webcal:// variant
/// from it (just swapping the scheme) rather than the backend building both.</summary>
public record CalendarFeedUrlDto(string FeedUrl);
