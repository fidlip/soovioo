using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Events;

/// <summary>Deliberately has no [Authorize] — Google/Apple Calendar
/// subscribe to this URL directly and can't send a Bearer token. The
/// CalendarToken in the URL (see CalendarTokenController) is the only
/// gate, so it must be treated like a password: never logged, never
/// guessable.</summary>
[ApiController]
[Route("api/calendar-feed")]
public class CalendarFeedController(AppDbContext db) : ControllerBase
{
    [HttpGet("{token}.ics")]
    public async Task<IActionResult> GetFeed(string token)
    {
        // No ICurrentSchoolContext/JWT on this request, so the normal
        // SchoolId query filter has nothing to match against — look the
        // user up (and then their school's events) with it explicitly
        // bypassed, same as the unauthenticated login lookup in AuthController.
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.CalendarToken == token);
        if (user?.SchoolId is null)
        {
            return NotFound();
        }

        // Same visibility rules as the in-app calendar (GetEvents) — a
        // subscribed feed must not leak events this user couldn't see in
        // the app itself.
        var schoolId = user.SchoolId.Value;
        var query = user.Role switch
        {
            UserRole.Admin => db.VisibleToAdmin(schoolId),
            UserRole.Teacher => await db.VisibleToTeacherAsync(schoolId, user.Id),
            UserRole.Parent => await db.VisibleToParentAsync(schoolId, user.Id),
            _ => db.Events.Where(e => false),
        };
        var events = await query.ToListAsync();

        var calendar = new Calendar();
        foreach (var e in events)
        {
            calendar.Events.Add(new CalendarEvent
            {
                Uid = $"sova-event-{e.Id}@sova",
                Summary = e.Title,
                Description = e.Description,
                Location = e.Location,
                Start = new CalDateTime(e.StartAt.UtcDateTime, "UTC", hasTime: !e.AllDay),
                End = new CalDateTime(e.EndAt.UtcDateTime, "UTC", hasTime: !e.AllDay),
            });
        }

        var ics = new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
        return Content(ics, "text/calendar; charset=utf-8");
    }
}
