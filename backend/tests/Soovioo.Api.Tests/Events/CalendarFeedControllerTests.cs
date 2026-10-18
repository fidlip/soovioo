using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Tests.Events;

public class CalendarFeedControllerTests
{
    private static AppDbContext NewDb(DbContextOptions<AppDbContext> options, int? schoolId, bool isSuperAdmin = false) =>
        new(options, new FakeCurrentSchoolContext(schoolId, isSuperAdmin));

    [Fact]
    public async Task ValidTokenReturnsIcsWithTheUsersSchoolEvents()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Seeded as SuperAdmin so rows for both schools actually get written
        // (see MultiTenancyQueryFilterTests for why a non-SuperAdmin context
        // can't be used for seeding).
        using (var seedDb = NewDb(options, null, isSuperAdmin: true))
        {
            var schoolA = new School { Name = "Škola A", Slug = "a" };
            var schoolB = new School { Name = "Škola B", Slug = "b" };
            seedDb.Schools.AddRange(schoolA, schoolB);
            seedDb.SaveChanges();

            seedDb.Users.Add(new User
            {
                Username = "rodic.a", PasswordHash = "x", Role = UserRole.Parent,
                SchoolId = schoolA.Id, CalendarToken = "tok-a",
            });
            seedDb.Events.AddRange(
                new Event { Title = "Akce A", SchoolId = schoolA.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1) },
                new Event { Title = "Akce B", SchoolId = schoolB.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1) });
            seedDb.SaveChanges();
        }

        // No ICurrentSchoolContext available for an unauthenticated feed
        // request — SchoolId/IsSuperAdmin are irrelevant here since the
        // controller looks the token up with IgnoreQueryFilters().
        using var db = NewDb(options, null);
        var result = await new CalendarFeedController(db).GetFeed("tok-a");

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/calendar; charset=utf-8", content.ContentType);
        Assert.Contains("Akce A", content.Content);
        Assert.DoesNotContain("Akce B", content.Content);
        Assert.Contains("BEGIN:VEVENT", content.Content);
    }

    [Fact]
    public async Task UnknownTokenReturnsNotFound()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = NewDb(options, null);

        var result = await new CalendarFeedController(db).GetFeed("does-not-exist");

        Assert.IsType<NotFoundResult>(result);
    }
}
