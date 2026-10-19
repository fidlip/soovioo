using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Hosting;

namespace Soovioo.Api.Tests.Hosting;

public class TlsAskControllerTests
{
    private const string MainDomain = "soovioo.cloud";

    private static TlsAskController NewController()
    {
        // Caddy's ask comes with no logged-in user — the tenant filter would
        // hide every school, hence IgnoreQueryFilters in the controller.
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(null));
        db.Schools.Add(new School { Name = "ZŠ Demo", Slug = "demo" });
        db.SaveChanges();
        return new TlsAskController(db, Options.Create(new AppLinkOptions { MainDomain = MainDomain }));
    }

    [Theory]
    [InlineData("demo.soovioo.cloud", "demo")]
    [InlineData("DEMO.Soovioo.Cloud", "demo")]
    [InlineData("soovioo.cloud", null)]
    [InlineData("a.demo.soovioo.cloud", null)]
    [InlineData("demo.notsoovioo.cloud", null)]
    [InlineData("demo.soovioo.cz", null)]
    [InlineData("", null)]
    public void SchoolSlugOf(string domain, string? expected) =>
        Assert.Equal(expected, TlsAskController.SchoolSlugOf(domain, MainDomain));

    [Fact]
    public async Task Allows_an_existing_school_subdomain()
    {
        Assert.IsType<OkResult>(await NewController().Ask("demo.soovioo.cloud"));
    }

    [Fact]
    public async Task Refuses_an_unknown_school_and_foreign_hosts()
    {
        var controller = NewController();
        Assert.IsType<NotFoundResult>(await controller.Ask("neexistuje.soovioo.cloud"));
        Assert.IsType<NotFoundResult>(await controller.Ask("example.com"));
        Assert.IsType<NotFoundResult>(await controller.Ask(null));
    }
}
