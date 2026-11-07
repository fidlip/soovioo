using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Evidence;
using Soovioo.Api.ProductFeedback;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

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
        db.SaveChanges();
        return db;
    }

    private static FeedbackController Controller(AppDbContext db, FakeEmailSender mail, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(db.Schools.Single().Id),
            new FeedbackService(db, mail, Options.Create(new DemoOptions()), new FakeClock(Now), NullLogger<FeedbackService>.Instance))
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

    [Fact]
    public async Task SchoolUsersFeedbackIsKeptWithTheSchoolAndTheSenderAndForwarded()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });
        var mail = new FakeEmailSender();

        var result = await Controller(db, mail).Send(
            new FeedbackRequest(5, "  Rozvrh je super.  ", "jan@example.com", Consent: true, Role: "Učitel"));

        Assert.IsType<NoContentResult>(result);
        var feedback = await db.Feedback.SingleAsync();
        Assert.Equal((5, "ZŠ Lipová", 7), (feedback.SchoolId!.Value, feedback.SchoolName, feedback.UserId!.Value));
        Assert.Null(feedback.DemoSlug);
        Assert.Equal("Rozvrh je super.", feedback.Text);
        Assert.True(feedback.Consent);
        Assert.Equal(Now, feedback.CreatedAt);
        Assert.Empty(db.DemoContacts);
        var (to, subject, body) = Assert.Single(mail.Sent);
        Assert.Equal("info@soovioo.cz", to);
        Assert.Equal("Zpětná vazba ze školy (★★★★★)", subject);
        Assert.Contains("ZŠ Lipová", body);
    }

    [Fact]
    public async Task ConsentNeedsAnEmail()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        await Controller(db, new FakeEmailSender()).Send(new FeedbackRequest(3, null, " ", Consent: true));

        var feedback = await db.Feedback.SingleAsync();
        Assert.Null(feedback.Email);
        Assert.False(feedback.Consent);
    }

    [Fact]
    public async Task EmptyFeedbackIsRefused()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });
        var mail = new FakeEmailSender();

        Assert.IsType<BadRequestObjectResult>(await Controller(db, mail).Send(new FeedbackRequest(null, "  ", null)));
        Assert.IsType<BadRequestObjectResult>(await Controller(db, mail).Send(new FeedbackRequest(6, "x", null)));

        Assert.Empty(db.Feedback);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task FromADemoItIsTheDemosFeedback()
    {
        using var db = Db(new School { Id = 5, Name = "Demo", Slug = "demo-x", IsDemo = true });
        var mail = new FakeEmailSender();

        await Controller(db, mail).Send(new FeedbackRequest(4, "Líbí", "v@example.com", Consent: true));

        Assert.Equal("demo-x", (await db.Feedback.SingleAsync()).DemoSlug);
        Assert.Equal("feedback", (await db.DemoContacts.SingleAsync()).Source);
        Assert.StartsWith("Zpětná vazba z dema", Assert.Single(mail.Sent).Subject);
    }

    [Fact]
    public async Task SupportModeCantSendFeedbackForTheSchool()
    {
        using var db = Db(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });

        var result = await Controller(db, new FakeEmailSender(), new Claim(SupportClaims.SessionId, "1"))
            .Send(new FeedbackRequest(5, "x", null));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(db.Feedback);
    }
}
