using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;
using Sova.Api.Events;

namespace Sova.Api.Tests.Events;

public class EventsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static EventsController NewController(AppDbContext db, int currentUserId, string role)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Name, "tester"),
        ]);
        return new EventsController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public async Task CreateEventAssignsSchoolIdAndCreator()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Teacher")
            .CreateEvent(new CreateEventRequest(
                "Školní výlet", "Celodenní výlet do zoo", null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 16, 0, 0, TimeSpan.Zero),
                AllDay: false));

        var dto = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Školní výlet", dto.Title);
        var saved = await db.Events.FindAsync(dto.Id);
        Assert.NotNull(saved);
        Assert.Equal(SchoolId, saved!.SchoolId);
        Assert.Equal(7, saved.CreatedByUserId);
    }

    [Fact]
    public async Task CreateEventAcceptsAndReturnsValidColor()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Teacher")
            .CreateEvent(new CreateEventRequest(
                "Zelená akce", null, null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                AllDay: false, Color: "#1ABC9C"));

        var dto = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("#1ABC9C", dto.Color);
    }

    [Fact]
    public async Task CreateEventRejectsMalformedColor()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Teacher")
            .CreateEvent(new CreateEventRequest(
                "Špatná barva", null, null,
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                AllDay: false, Color: "red"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateEventRejectsEndBeforeStart()
    {
        using var db = NewDb();

        var result = await NewController(db, 7, "Admin")
            .CreateEvent(new CreateEventRequest(
                "Neplatná akce", null, null,
                new DateTimeOffset(2026, 5, 1, 16, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                AllDay: false));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetEventsFiltersByDateRange()
    {
        using var db = NewDb();
        db.Events.AddRange(
            new Event
            {
                SchoolId = SchoolId,
                Title = "Akce v květnu",
                StartAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            },
            new Event
            {
                SchoolId = SchoolId,
                Title = "Akce v červnu",
                StartAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            });
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Teacher").GetEvents(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
            null);

        var events = Assert.IsAssignableFrom<IEnumerable<EventDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(events);
        Assert.Equal("Akce v červnu", events.Single().Title);
    }

    [Fact]
    public async Task DeleteEventRemovesIt()
    {
        using var db = NewDb();
        var @event = new Event
        {
            SchoolId = SchoolId,
            Title = "Ke smazání",
            StartAt = DateTimeOffset.UtcNow,
            EndAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        db.Events.Add(@event);
        await db.SaveChangesAsync();

        var result = await NewController(db, 1, "Admin").DeleteEvent(@event.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Events.FindAsync(@event.Id));
    }
}
