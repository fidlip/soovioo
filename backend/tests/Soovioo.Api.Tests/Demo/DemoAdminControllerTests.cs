using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;

namespace Soovioo.Api.Tests.Demo;

public class DemoAdminControllerTests
{
    [Fact]
    public async Task ContactsCsvNamesTheSourceInCzech()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(null, isSuperAdmin: true));
        db.DemoContacts.AddRange(
            new DemoContact { DemoSlug = "demo-a", Email = "a@example.com", Consent = true, Source = "wizard" },
            new DemoContact { DemoSlug = "demo-b", Email = "b@example.com", Source = "feedback" });
        await db.SaveChangesAsync();

        var file = Assert.IsType<FileContentResult>(await new DemoAdminController(db).GetContactsCsv());

        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("a@example.com;ano;průvodce;demo-a;", csv);
        Assert.Contains("b@example.com;ne;zpětná vazba;demo-b;", csv);
        Assert.DoesNotContain("wizard", csv);
    }

    [Fact]
    public async Task ContactsCsvShowsPragueTime()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(null, isSuperAdmin: true));
        db.DemoContacts.Add(new DemoContact
        {
            DemoSlug = "demo-a", Email = "a@example.com", Source = "wizard",
            CreatedAt = new DateTimeOffset(2026, 10, 24, 14, 10, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        var file = Assert.IsType<FileContentResult>(await new DemoAdminController(db).GetContactsCsv());

        Assert.Contains(";2026-10-24 16:10\r\n", Encoding.UTF8.GetString(file.FileContents));
    }
}
