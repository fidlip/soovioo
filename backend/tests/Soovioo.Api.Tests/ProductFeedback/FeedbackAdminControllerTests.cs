using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.ProductFeedback;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.ProductFeedback;

public class FeedbackAdminControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private static FeedbackAdminController Controller(AppDbContext db, IEmailSender? smtp = null) =>
        new(db, smtp ?? new FakeEmailSender(), Options.Create(new DemoOptions()), new FakeClock(Now),
            NullLogger<FeedbackAdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Jana Nováková")], "test")),
                },
            },
        };

    private static async Task<PagedResult<FeedbackDto>> List(
        AppDbContext db, FeedbackFilter? filter = null, int page = 1, int pageSize = 20) =>
        Assert.IsType<PagedResult<FeedbackDto>>(
            Assert.IsType<OkObjectResult>((await Controller(db).GetFeedback(filter ?? new(), page, pageSize)).Result).Value);

    /// <summary>An e-mail comes with the consent to be contacted unless said otherwise.</summary>
    private static Feedback Item(
        string slug, int minutesAgo = 0, string? text = "Text", string? email = null, bool? consent = null) => new()
    {
        DemoSlug = slug, Text = text, Email = email, Consent = consent ?? email is not null,
        CreatedAt = Now.AddMinutes(-minutesAgo),
    };

    [Fact]
    public async Task FeedbackComesNewestFirstPageByPageWithTheTotal()
    {
        using var db = Db();
        db.Feedback.AddRange(Item("a", 30), Item("b", 10), Item("c", 20));
        await db.SaveChangesAsync();

        var first = await List(db, pageSize: 2);
        var second = await List(db, page: 2, pageSize: 2);

        Assert.Equal(["b", "c"], first.Items.Select(f => f.DemoSlug));
        Assert.Equal(["a"], second.Items.Select(f => f.DemoSlug));
        Assert.Equal(3, first.TotalCount);
    }

    [Fact]
    public async Task FeedbackTellsWhetherItsSchoolStillExists()
    {
        using var db = Db();
        db.Schools.Add(new School { Id = 5, Name = "Live", Slug = "live", IsDemo = true });
        var live = Item("live");
        live.SchoolId = 5;
        var gone = Item("gone", 1);
        gone.SchoolId = 6;
        db.Feedback.AddRange(live, gone, Item("purged", 2));
        await db.SaveChangesAsync();

        var items = (await List(db)).Items;

        Assert.True(items.Single(f => f.DemoSlug == "live").SchoolExists);
        Assert.False(items.Single(f => f.DemoSlug == "gone").SchoolExists);
        Assert.False(items.Single(f => f.DemoSlug == "purged").SchoolExists);
    }

    [Fact]
    public async Task SchoolFeedbackNamesItsSenderWhileTheAccountExists()
    {
        using var db = Db();
        db.Schools.Add(new School { Id = 5, Name = "ZŠ Lipová", Slug = "lipova" });
        var user = new User { Id = 7, Username = "jnovak", PasswordHash = "x", SchoolId = 5 };
        db.Users.AddRange(user, new User { Id = 8, Username = "bez-jmena", PasswordHash = "x", SchoolId = 5 });
        db.Persons.Add(new Person { FirstName = "Jan", LastName = "Novák", SchoolId = 5, UserId = 7 });
        db.Feedback.AddRange(
            new Feedback { SchoolId = 5, SchoolName = "ZŠ Lipová", UserId = 7, Text = "a", CreatedAt = Now },
            new Feedback { SchoolId = 5, SchoolName = "ZŠ Lipová", UserId = 8, Text = "b", CreatedAt = Now.AddMinutes(-1) },
            new Feedback { SchoolId = 5, SchoolName = "ZŠ Lipová", UserId = 9, Text = "c", CreatedAt = Now.AddMinutes(-2) });
        await db.SaveChangesAsync();

        var items = (await List(db)).Items;

        Assert.Equal(["Jan Novák", "bez-jmena", null], items.Select(f => f.UserName));
        Assert.All(items, f => Assert.Equal("ZŠ Lipová", f.SchoolName));
        Assert.All(items, f => Assert.Null(f.DemoSlug));
    }

    [Fact]
    public async Task FeedbackFiltersBySource()
    {
        using var db = Db();
        db.Feedback.AddRange(
            Item("demo-a"),
            new Feedback { SchoolName = "ZŠ Lipová", Text = "škola", CreatedAt = Now.AddMinutes(-1) });
        await db.SaveChangesAsync();

        Assert.Equal(["ZŠ Lipová"],
            (await List(db, new FeedbackFilter(Source: FeedbackSource.School))).Items.Select(f => f.SchoolName));
        Assert.Equal(["demo-a"],
            (await List(db, new FeedbackFilter(Source: FeedbackSource.Demo))).Items.Select(f => f.DemoSlug));
    }

    [Theory]
    [InlineData(FeedbackState.Unseen, "new")]
    [InlineData(FeedbackState.Unanswered, "new")]
    [InlineData(FeedbackState.Answered, "answered")]
    [InlineData(FeedbackState.Flagged, "flagged")]
    [InlineData(FeedbackState.Resolved, "resolved")]
    public async Task FeedbackFiltersByState(FeedbackState state, string expected)
    {
        using var db = Db();
        db.Feedback.AddRange(
            Item("new", email: "a@example.com"),
            new Feedback { DemoSlug = "no-consent", Email = "c@example.com", SeenAt = Now, CreatedAt = Now },
            new Feedback
            {
                DemoSlug = "answered", Email = "b@example.com", Consent = true, SeenAt = Now, CreatedAt = Now,
                Replies = [new FeedbackReply { Text = "Díky", SentBy = "X", SentAt = Now }],
            },
            new Feedback { DemoSlug = "flagged", SeenAt = Now, Flagged = true, CreatedAt = Now },
            new Feedback { DemoSlug = "resolved", SeenAt = Now, ResolvedAt = Now, CreatedAt = Now });
        await db.SaveChangesAsync();

        var items = (await List(db, new FeedbackFilter(State: state))).Items;

        Assert.Equal([expected], items.Select(f => f.DemoSlug));
    }

    [Fact]
    public async Task OpenFeedbackIsWhatIsNotResolved()
    {
        using var db = Db();
        db.Feedback.AddRange(
            Item("open"),
            new Feedback { DemoSlug = "resolved", ResolvedAt = Now, CreatedAt = Now });
        await db.SaveChangesAsync();

        var items = (await List(db, new FeedbackFilter(State: FeedbackState.Open))).Items;

        Assert.Equal(["open"], items.Select(f => f.DemoSlug));
    }

    [Fact]
    public async Task FeedbackFiltersByRatingOrItsAbsence()
    {
        using var db = Db();
        var five = Item("five");
        five.Rating = 5;
        var two = Item("two", 1);
        two.Rating = 2;
        db.Feedback.AddRange(five, two, Item("none", 2));
        await db.SaveChangesAsync();

        Assert.Equal(["two"], (await List(db, new FeedbackFilter(Rating: 2))).Items.Select(f => f.DemoSlug));
        Assert.Equal(["none"], (await List(db, new FeedbackFilter(Rating: 0))).Items.Select(f => f.DemoSlug));
    }

    [Fact]
    public async Task FeedbackSearchLooksInTextEmailRoleSchoolAndDemo()
    {
        using var db = Db();
        var role = Item("r", 3);
        role.Role = "Učitel";
        db.Feedback.AddRange(
            Item("t", 1, text: "Chybí mi ROZVRH"),
            Item("e", 2, email: "rozvrhar@example.com"),
            role,
            Item("demo-rozvrh", 4),
            new Feedback { SchoolName = "ZŠ Rozvrhová", Text = "x", CreatedAt = Now.AddMinutes(-5) },
            Item("other", 6));
        await db.SaveChangesAsync();

        Assert.Equal(["t", "e", "demo-rozvrh", null],
            (await List(db, new FeedbackFilter(Search: " rozvrh "))).Items.Select(f => f.DemoSlug));
        Assert.Equal(["r"], (await List(db, new FeedbackFilter(Search: "UCITEL"))).Items.Select(f => f.DemoSlug));
    }

    [Fact]
    public async Task UnseenCountCountsWhatNoOneOpened()
    {
        using var db = Db();
        db.Feedback.AddRange(Item("a"), Item("b"), new Feedback { DemoSlug = "c", SeenAt = Now });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>((await Controller(db).GetUnseenCount()).Result);

        Assert.Equal(2, result.Value);
    }

    [Fact]
    public async Task UpdateChangesOnlyTheFieldsSentAndKeepsTheFirstTime()
    {
        using var db = Db();
        var earlier = Now.AddDays(-1);
        db.Feedback.Add(new Feedback { DemoSlug = "a", SeenAt = earlier, CreatedAt = earlier });
        await db.SaveChangesAsync();
        var id = db.Feedback.Single().Id;

        var flagged = await Controller(db).Update(id, new FeedbackUpdate(Seen: true, Flagged: true));
        var resolved = await Controller(db).Update(id, new FeedbackUpdate(Resolved: true));

        var dto = Assert.IsType<FeedbackDto>(Assert.IsType<OkObjectResult>(resolved.Result).Value);
        Assert.Equal(earlier, dto.SeenAt);
        Assert.True(dto.Flagged);
        Assert.Equal(Now, dto.ResolvedAt);
        Assert.IsType<OkObjectResult>(flagged.Result);
    }

    [Fact]
    public async Task FeedbackCanBeMarkedUnseenAndUnresolvedAgain()
    {
        using var db = Db();
        db.Feedback.Add(new Feedback { DemoSlug = "a", SeenAt = Now, ResolvedAt = Now, Flagged = true });
        await db.SaveChangesAsync();
        var id = db.Feedback.Single().Id;

        await Controller(db).Update(id, new FeedbackUpdate(Seen: false, Flagged: false, Resolved: false));

        var feedback = db.Feedback.Single();
        Assert.Null(feedback.SeenAt);
        Assert.False(feedback.Flagged);
        Assert.Null(feedback.ResolvedAt);
    }

    [Fact]
    public async Task UpdateOfMissingFeedbackIsNotFound()
    {
        using var db = Db();

        var result = await Controller(db).Update(42, new FeedbackUpdate(Seen: true));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ReplyIsMailedWithTheOriginalAndKeptWithWhoSentIt()
    {
        using var db = Db();
        db.Feedback.Add(Item("a", text: "Chybí <rozvrh>", email: "visitor@example.com"));
        await db.SaveChangesAsync();
        var id = db.Feedback.Single().Id;
        var smtp = new FakeEmailSender();

        var result = await Controller(db, smtp).Reply(id, new FeedbackReplyRequest("  Díky, už se na tom pracuje.  "));

        var reply = Assert.IsType<FeedbackReplyDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Díky, už se na tom pracuje.", reply.Text);
        Assert.Equal("Jana Nováková", reply.SentBy);
        var mail = Assert.Single(smtp.Sent);
        Assert.Equal("visitor@example.com", mail.To);
        Assert.Contains("Díky, už se na tom pracuje.", mail.HtmlBody);
        Assert.Contains("Chybí &lt;rozvrh&gt;", mail.HtmlBody);
        Assert.Contains("info@soovioo.cz", mail.HtmlBody);
        var dto = (await List(db)).Items.Single();
        Assert.Equal(1, dto.ReplyCount);
        Assert.Equal(Now, dto.LastReplyAt);
        Assert.Equal(Now, dto.SeenAt);
        var replies = Assert.IsType<OkObjectResult>((await Controller(db).GetReplies(id)).Result);
        Assert.Equal(["Díky, už se na tom pracuje."], ((IEnumerable<FeedbackReplyDto>)replies.Value!).Select(r => r.Text));
    }

    [Fact]
    public async Task ReplyNeedsAnEmailAndAText()
    {
        using var db = Db();
        db.Feedback.AddRange(Item("no-email"), Item("with-email", email: "v@example.com"));
        await db.SaveChangesAsync();
        var smtp = new FakeEmailSender();

        var noEmail = await Controller(db, smtp).Reply(
            db.Feedback.Single(f => f.DemoSlug == "no-email").Id, new FeedbackReplyRequest("Ahoj"));
        var noText = await Controller(db, smtp).Reply(
            db.Feedback.Single(f => f.DemoSlug == "with-email").Id, new FeedbackReplyRequest("   "));

        Assert.IsType<BadRequestObjectResult>(noEmail.Result);
        Assert.IsType<BadRequestObjectResult>(noText.Result);
        Assert.Empty(smtp.Sent);
        Assert.Empty(db.FeedbackReplies);
    }

    [Fact]
    public async Task ReplyNeedsTheConsentToBeContacted()
    {
        using var db = Db();
        db.Feedback.Add(Item("a", email: "v@example.com", consent: false));
        await db.SaveChangesAsync();
        var smtp = new FakeEmailSender();

        var result = await Controller(db, smtp).Reply(db.Feedback.Single().Id, new FeedbackReplyRequest("Ahoj"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(smtp.Sent);
        Assert.Empty(db.FeedbackReplies);
    }

    [Fact]
    public async Task DeletingFeedbackTakesItsRepliesAlong()
    {
        using var db = Db();
        var doomed = Item("spam", email: "v@example.com");
        doomed.Replies = [new FeedbackReply { Text = "Díky", SentBy = "X", SentAt = Now }];
        db.Feedback.AddRange(doomed, Item("kept", 1));
        await db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Controller(db).Delete(doomed.Id));
        Assert.IsType<NotFoundResult>(await Controller(db).Delete(doomed.Id));

        Assert.Equal(["kept"], db.Feedback.Select(f => f.DemoSlug));
        Assert.Empty(db.FeedbackReplies);
    }

    [Fact]
    public async Task ReplyThatFailedToSendIsNotKept()
    {
        using var db = Db();
        db.Feedback.Add(Item("a", email: "v@example.com"));
        await db.SaveChangesAsync();

        var result = await Controller(db, new FailingEmailSender())
            .Reply(db.Feedback.Single().Id, new FeedbackReplyRequest("Ahoj"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(db.FeedbackReplies);
        Assert.Null(db.Feedback.Single().SeenAt);
    }

    private class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody) =>
            throw new InvalidOperationException("SMTP down");
    }
}
