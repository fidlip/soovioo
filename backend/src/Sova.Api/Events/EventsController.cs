using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Events;

[Authorize]
[ApiController]
[Route("api/events")]
public partial class EventsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <summary>Every authenticated user of the school sees every event —
    /// like the timetable, this is a shared bulletin, not per-student data,
    /// so no CanAccessStudentAsync-style check is needed.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetEvents(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int? schoolClassId)
    {
        var query = db.Events.AsQueryable();
        if (from is not null)
        {
            query = query.Where(e => e.EndAt >= from);
        }
        if (to is not null)
        {
            query = query.Where(e => e.StartAt <= to);
        }
        if (schoolClassId is not null)
        {
            query = query.Where(e => e.SchoolClassId == schoolClassId);
        }

        var events = await query
            .OrderBy(e => e.StartAt)
            .Select(e => ToDto(e))
            .ToListAsync();

        return Ok(events);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorPattern();

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<EventDto>> CreateEvent(CreateEventRequest request)
    {
        if (request.EndAt < request.StartAt)
        {
            return BadRequest("EndAt must not be before StartAt.");
        }

        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Color must be a \"#rrggbb\" hex string.");
        }

        if (request.SchoolClassId is not null && await db.SchoolClasses.FindAsync(request.SchoolClassId) is null)
        {
            return BadRequest("Unknown schoolClassId.");
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var @event = new Event
        {
            SchoolId = currentSchool.SchoolId!.Value,
            SchoolClassId = request.SchoolClassId,
            Title = request.Title,
            Description = request.Description,
            Location = request.Location,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            AllDay = request.AllDay,
            Color = request.Color,
            CreatedByUserId = userId,
        };

        db.Events.Add(@event);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEvents), new { }, ToDto(@event));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<EventDto>> UpdateEvent(int id, CreateEventRequest request)
    {
        if (request.EndAt < request.StartAt)
        {
            return BadRequest("EndAt must not be before StartAt.");
        }

        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return BadRequest("Color must be a \"#rrggbb\" hex string.");
        }

        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (@event is null)
        {
            return NotFound();
        }

        if (request.SchoolClassId is not null && await db.SchoolClasses.FindAsync(request.SchoolClassId) is null)
        {
            return BadRequest("Unknown schoolClassId.");
        }

        @event.SchoolClassId = request.SchoolClassId;
        @event.Title = request.Title;
        @event.Description = request.Description;
        @event.Location = request.Location;
        @event.StartAt = request.StartAt;
        @event.EndAt = request.EndAt;
        @event.AllDay = request.AllDay;
        @event.Color = request.Color;
        await db.SaveChangesAsync();

        return Ok(ToDto(@event));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (@event is null)
        {
            return NotFound();
        }

        db.Events.Remove(@event);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static EventDto ToDto(Event e) => new(
        e.Id, e.SchoolClassId, e.Title, e.Description, e.Location, e.StartAt, e.EndAt, e.AllDay, e.Color);
}

public record CreateEventRequest(
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool AllDay,
    int? SchoolClassId = null,
    string? Color = null);

public record EventDto(
    int Id,
    int? SchoolClassId,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool AllDay,
    string? Color);
