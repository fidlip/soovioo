using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Notifications;

public class PushTests
{
    private const int SchoolId = 1;

    /// <summary>12:00 in Prague on 2026-10-26 (winter time, UTC+1).</summary>
    private static readonly DateTimeOffset Noon = new(2026, 10, 26, 11, 0, 0, TimeSpan.Zero);

    private sealed class FakePushSender : IPushSender
    {
        public List<(string Token, PushMessage Message)> Sent { get; } = [];
        public HashSet<string> Invalid { get; } = [];

        public Task<PushResult> SendAsync(string token, PushMessage message)
        {
            if (Invalid.Contains(token))
            {
                return Task.FromResult(PushResult.InvalidToken);
            }
            Sent.Add((token, message));
            return Task.FromResult(PushResult.Sent);
        }
    }

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static async Task<(School School, User Parent, User Other)> SeedAsync(AppDbContext db, bool demo = false)
    {
        var school = new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola", IsDemo = demo };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        var other = new User { Username = "rodic2", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Schools.Add(school);
        db.Users.AddRange(parent, other);
        await db.SaveChangesAsync();
        return (school, parent, other);
    }

    private static void AddDevice(AppDbContext db, User user, string token) =>
        db.PushDevices.Add(new PushDevice { SchoolId = SchoolId, UserId = user.Id, Token = token, CreatedAt = Noon, LastSeenAt = Noon });

    private static NotificationsController Bell(AppDbContext db, User user, TimeProvider? clock = null) =>
        new(db, clock ?? new FakeClock(Noon))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                    ], "Test")),
                },
            },
        };

    private static Task NotifyAsync(AppDbContext db, TimeProvider clock, User user, NotificationType type, string title, int? targetId = null) =>
        new Notifier(db, clock).NotifyAsync(SchoolId, [user.Id], type, title, "Jan Novák: 1", "grades", targetId);

    [Fact]
    public async Task OnlyAccountsWithAPhoneAndTheKindOnGetAPush()
    {
        using var db = NewDb();
        var (_, parent, other) = await SeedAsync(db);
        AddDevice(db, parent, "t1");
        await db.SaveChangesAsync();
        var clock = new FakeClock(Noon);

        await NotifyAsync(db, clock, parent, NotificationType.Grade, "Nová známka — Matematika");
        await NotifyAsync(db, clock, other, NotificationType.Grade, "Nová známka — Matematika");
        parent.MutedPushNotifications = [NotificationType.Event];
        await db.SaveChangesAsync();
        await NotifyAsync(db, clock, parent, NotificationType.Event, "Výlet");
        await db.SaveChangesAsync();

        Assert.True((await db.Notifications.SingleAsync(n => n.UserId == parent.Id && n.Type == NotificationType.Grade)).Push);
        Assert.False((await db.Notifications.SingleAsync(n => n.UserId == parent.Id && n.Type == NotificationType.Event)).Push);
        Assert.False((await db.Notifications.SingleAsync(n => n.UserId == other.Id)).Push);
    }

    [Fact]
    public async Task DemoSchoolSendsNoPush()
    {
        using var db = NewDb();
        var (_, parent, _) = await SeedAsync(db, demo: true);
        AddDevice(db, parent, "t1");
        await db.SaveChangesAsync();

        await NotifyAsync(db, new FakeClock(Noon), parent, NotificationType.Grade, "Nová známka");
        await db.SaveChangesAsync();

        Assert.False((await db.Notifications.SingleAsync()).Push);
    }

    [Fact]
    public async Task PushCarriesOnlyTheKindUnlessTheSchoolAllowsAndThePersonAgreed()
    {
        using var db = NewDb();
        var (school, parent, _) = await SeedAsync(db);
        AddDevice(db, parent, "t1");
        AddDevice(db, parent, "t2");
        await db.SaveChangesAsync();
        var clock = new FakeClock(Noon);
        var sender = new FakePushSender();

        await NotifyAsync(db, clock, parent, NotificationType.Grade, "Nová známka — Matematika");
        await db.SaveChangesAsync();
        await new PushJob(db, sender, clock).RunAsync();

        Assert.Equal(["t1", "t2"], sender.Sent.Select(s => s.Token).Order());
        var generic = sender.Sent[0].Message;
        Assert.Equal("Nová známka", generic.Title);
        Assert.Equal(PushJob.GenericBody, generic.Body);
        Assert.Equal("grades", generic.Data["page"]);
        Assert.NotNull((await db.Notifications.SingleAsync()).PushedAt);

        // Agreed, but the school doesn't allow it — still only the kind.
        parent.PushFullTextAt = Noon;
        await NotifyAsync(db, clock, parent, NotificationType.Message, "Nová zpráva: Výlet", targetId: 7);
        await db.SaveChangesAsync();
        await new PushJob(db, sender, clock).RunAsync();
        Assert.Equal("Nová zpráva", sender.Sent[^1].Message.Title);

        school.PushFullText = true;
        await NotifyAsync(db, clock, parent, NotificationType.Message, "Nová zpráva: Výlet", targetId: 8);
        await db.SaveChangesAsync();
        await new PushJob(db, sender, clock).RunAsync();
        var full = sender.Sent[^1].Message;
        Assert.Equal("Nová zpráva: Výlet", full.Title);
        Assert.Equal("Jan Novák: 1", full.Body);
        Assert.Equal("8", full.Data["targetId"]);
        Assert.Equal(6, sender.Sent.Count);
    }

    [Fact]
    public async Task SeveralAtOnceGoAsOnePushAndReadOnesAreSkipped()
    {
        using var db = NewDb();
        var (_, parent, _) = await SeedAsync(db);
        AddDevice(db, parent, "t1");
        await db.SaveChangesAsync();
        var clock = new FakeClock(Noon);
        await NotifyAsync(db, clock, parent, NotificationType.Grade, "Nová známka — Matematika");
        await NotifyAsync(db, clock, parent, NotificationType.Homework, "Nový úkol");
        await NotifyAsync(db, clock, parent, NotificationType.Event, "Přečtená akce");
        await db.SaveChangesAsync();
        (await db.Notifications.SingleAsync(n => n.Title == "Přečtená akce")).ReadAt = Noon;
        await db.SaveChangesAsync();
        var sender = new FakePushSender();

        await new PushJob(db, sender, clock).RunAsync();

        var (_, message) = Assert.Single(sender.Sent);
        Assert.Equal("2 nová upozornění", message.Title);
        Assert.Equal("Nová známka, Nový domácí úkol", message.Body);
        Assert.Equal("notifications", message.Data["page"]);
        Assert.All(await db.Notifications.ToListAsync(), n => Assert.NotNull(n.PushedAt));

        await new PushJob(db, sender, clock).RunAsync();
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task NightQuietHoldsThePushUntilItEnds()
    {
        using var db = NewDb();
        var (_, parent, other) = await SeedAsync(db);
        AddDevice(db, parent, "t1");
        AddDevice(db, other, "t2");
        other.PushQuietOff = true;
        await db.SaveChangesAsync();
        // 23:00 in Prague — in the school's 21:00–7:00.
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero));
        await NotifyAsync(db, clock, parent, NotificationType.Grade, "Nová známka");
        await NotifyAsync(db, clock, other, NotificationType.Grade, "Nová známka");
        await db.SaveChangesAsync();
        var sender = new FakePushSender();

        await new PushJob(db, sender, clock).RunAsync();
        Assert.Equal(["t2"], sender.Sent.Select(s => s.Token));

        // 7:00 in Prague.
        clock.Now = new DateTimeOffset(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
        await new PushJob(db, sender, clock).RunAsync();
        Assert.Equal(["t2", "t1"], sender.Sent.Select(s => s.Token));
    }

    [Theory]
    [InlineData(21, 0, 7, 0, 22, true)]
    [InlineData(21, 0, 7, 0, 6, true)]
    [InlineData(21, 0, 7, 0, 7, false)]
    [InlineData(21, 0, 7, 0, 12, false)]
    [InlineData(13, 0, 15, 0, 14, true)]
    [InlineData(13, 0, 15, 0, 16, false)]
    [InlineData(8, 0, 8, 0, 8, false)]
    public void QuietWindowMayCrossMidnight(int fromH, int fromM, int toH, int toM, int pragueHour, bool quiet)
    {
        var user = new User
        {
            Username = "u", PasswordHash = "x",
            PushQuietFrom = new TimeOnly(fromH, fromM), PushQuietTo = new TimeOnly(toH, toM),
            School = new School { Name = "Š", Slug = "s" },
        };
        // Winter time: Prague = UTC+1.
        var now = new DateTimeOffset(2026, 11, 2, pragueHour, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(quiet, PushJob.IsQuiet(user, now));
    }

    [Fact]
    public async Task InvalidTokenForgetsTheDevice()
    {
        using var db = NewDb();
        var (_, parent, _) = await SeedAsync(db);
        AddDevice(db, parent, "gone");
        AddDevice(db, parent, "ok");
        await db.SaveChangesAsync();
        var clock = new FakeClock(Noon);
        await NotifyAsync(db, clock, parent, NotificationType.Grade, "Nová známka");
        await db.SaveChangesAsync();
        var sender = new FakePushSender { Invalid = { "gone" } };

        await new PushJob(db, sender, clock).RunAsync();

        Assert.Equal(["ok"], await db.PushDevices.Select(d => d.Token).ToListAsync());
    }

    [Fact]
    public async Task DeviceRegistersMovesToTheNewAccountAndIsRemovedOnLogout()
    {
        using var db = NewDb();
        var (_, parent, other) = await SeedAsync(db);

        Assert.IsType<NoContentResult>(await Bell(db, parent).RegisterDevice(new PushDeviceRequest("tok", PushPlatform.Android)));
        Assert.IsType<NoContentResult>(await Bell(db, parent).RegisterDevice(new PushDeviceRequest("tok", PushPlatform.Android)));
        Assert.Equal(parent.Id, (await db.PushDevices.SingleAsync()).UserId);

        // The phone logged in as someone else.
        await Bell(db, other).RegisterDevice(new PushDeviceRequest("tok", PushPlatform.Android));
        Assert.Equal(other.Id, (await db.PushDevices.SingleAsync()).UserId);

        // Someone else's token stays.
        await Bell(db, parent).RemoveDevice(new PushDeviceRemoveRequest("tok"));
        Assert.Single(await db.PushDevices.ToListAsync());
        await Bell(db, other).RemoveDevice(new PushDeviceRemoveRequest("tok"));
        Assert.Empty(await db.PushDevices.ToListAsync());

        Assert.IsType<BadRequestResult>(await Bell(db, parent).RegisterDevice(new PushDeviceRequest(" ", PushPlatform.Android)));
    }

    [Fact]
    public async Task PushSettingsStoreKindsConsentAndQuiet()
    {
        using var db = NewDb();
        var (school, parent, _) = await SeedAsync(db);
        var clock = new FakeClock(Noon);

        var refused = await Bell(db, parent, clock).SetPushSettings(
            new NotificationPushSettingsRequest([], FullText: true, PushQuietMode.School, null, null));
        Assert.IsType<BadRequestObjectResult>(refused.Result);

        school.PushFullText = true;
        await db.SaveChangesAsync();
        var result = await Bell(db, parent, clock).SetPushSettings(new NotificationPushSettingsRequest(
            [NotificationType.Event], FullText: true, PushQuietMode.Own, new TimeOnly(22, 30, 15), new TimeOnly(6, 0)));
        var settings = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(settings.Types.Single(t => t.Type == NotificationType.Event).Push);
        Assert.True(settings.Types.Single(t => t.Type == NotificationType.Grade).Push);
        Assert.True(settings.Push.FullText);
        Assert.Equal(PushQuietMode.Own, settings.Push.Quiet);
        Assert.Equal(new TimeOnly(22, 30), settings.Push.QuietFrom);
        Assert.Equal(Noon, parent.PushFullTextAt);

        // Agreeing again keeps the original time; the school taking it back hides it.
        clock.Now = Noon.AddDays(1);
        await Bell(db, parent, clock).SetPushSettings(new NotificationPushSettingsRequest([], true, PushQuietMode.Off, null, null));
        Assert.Equal(Noon, parent.PushFullTextAt);
        Assert.True(parent.PushQuietOff);
        Assert.Null(parent.PushQuietFrom);
        school.PushFullText = false;
        await db.SaveChangesAsync();
        var after = Assert.IsType<NotificationSettingsDto>(Assert.IsType<OkObjectResult>((await Bell(db, parent).GetSettings()).Result).Value);
        Assert.False(after.Push.FullText);
        Assert.False(after.Push.FullTextAllowed);

        await Bell(db, parent, clock).SetPushSettings(new NotificationPushSettingsRequest([], false, PushQuietMode.School, null, null));
        Assert.Null(parent.PushFullTextAt);
        Assert.False(parent.PushQuietOff);
    }
}
