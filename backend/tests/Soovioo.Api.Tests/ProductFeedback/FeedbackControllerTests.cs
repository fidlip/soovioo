using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.ProductFeedback;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Tests.Backoffice;

namespace Soovioo.Api.Tests.ProductFeedback;

public class FeedbackControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static AppDbContext Db(School school)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(school.Id));
        db.Schools.Add(school);
        db.Users.Add(new User
        {
            Id = 7, SchoolId = school.Id, Username = "jan", PasswordHash = "x",
            Person = new Person { SchoolId = school.Id, FirstName = "Jan", LastName = "Novák" },
        });
        db.SaveChanges();
        return db;
    }

    private static FeedbackController Controller(AppDbContext db, FakeBackoffice? backoffice = null, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(db.Schools.Single().Id),
            new FeedbackService(db, backoffice ?? new FakeBackoffice(), new FakeClock(Now)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "7"), .. extra], "test")),
                },
            },
        };

    private static JsonElement Sent(AppDbContext db)
    {
        var sent = db.OutboxEvents.Single();
        Assert.Equal(FeedbackService.FeedbackSent, sent.Type);
        return JsonDocument.Parse(sent.Payload).RootElement;
    }

    [Fact]
    public async Task SchoolUsersFeedbackGoesToTheBackofficeWithTheSchoolAndTheSender()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        var result = await Controller(db).Send(
            new FeedbackRequest(5, "  Rozvrh je super.  ", "jan@example.com", Consent: true, Role: "Učitel"));

        Assert.IsType<NoContentResult>(result);
        var sent = Sent(db);
        Assert.Equal((5, "ZŠ Lipová"), (sent.GetProperty("schoolId").GetInt32(), sent.GetProperty("schoolName").GetString()));
        Assert.Equal((7, "Jan Novák", "Učitel"),
            (sent.GetProperty("userId").GetInt32(), sent.GetProperty("userName").GetString(), sent.GetProperty("role").GetString()));
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("demoSlug").ValueKind);
        Assert.Equal(("Rozvrh je super.", 5), (sent.GetProperty("text").GetString(), sent.GetProperty("rating").GetInt32()));
        Assert.Equal("jan@example.com", sent.GetProperty("email").GetString());
        Assert.Empty(db.DemoContacts);
    }

    [Fact]
    public async Task TheEmailGoesOnlyWithConsent()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        await Controller(db).Send(new FeedbackRequest(3, null, "jan@example.com", Consent: false));

        Assert.Equal(JsonValueKind.Null, Sent(db).GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task EmptyFeedbackIsRefused()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        Assert.IsType<BadRequestObjectResult>(await Controller(db).Send(new FeedbackRequest(null, "  ", null)));
        Assert.IsType<BadRequestObjectResult>(await Controller(db).Send(new FeedbackRequest(6, "x", null)));

        Assert.Empty(db.OutboxEvents);
    }

    [Fact]
    public async Task WithoutABackofficeThereIsNoFeedback()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });
        var controller = Controller(db, FakeBackoffice.NotConnected());

        Assert.False(controller.Get().Value!.Available);
        Assert.IsType<BadRequestObjectResult>(await controller.Send(new FeedbackRequest(5, "x", null)));
        Assert.Empty(db.OutboxEvents);
        Assert.True(Controller(db).Get().Value!.Available);
    }

    [Fact]
    public async Task FromADemoItIsTheDemosFeedbackAndTheEmailGetsTheDemosReminders()
    {
        using var db = Db(new School { Id = 5, Name = "Demo", Slug = "demo-x", IsDemo = true });

        await Controller(db).Send(new FeedbackRequest(4, "Líbí", "v@example.com", Consent: true));

        Assert.Equal("demo-x", Sent(db).GetProperty("demoSlug").GetString());
        var contact = await db.DemoContacts.SingleAsync();
        Assert.Equal(("feedback", true), (contact.Source, contact.Consent));
    }

    [Fact]
    public async Task SupportModeCantSendFeedbackForTheSchool()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        var result = await Controller(db, extra: new Claim(SupportClaims.SessionId, "1"))
            .Send(new FeedbackRequest(5, "x", null));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(db.OutboxEvents);
    }
}
