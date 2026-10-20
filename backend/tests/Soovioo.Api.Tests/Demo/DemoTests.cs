using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Demo;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Demo;

public class DemoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(2));

    private static AppDbContext NewDb(string? name = null, int? schoolId = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
                .Options,
            new FakeCurrentSchoolContext(schoolId));

    private static async Task<(School School, DemoFeaturedUsers Featured)> GenerateAsync(
        AppDbContext db, SchoolType type = SchoolType.Elementary, int perGrade = 2, int seed = 42)
    {
        var school = new School { Name = "Ukázková škola", Slug = "demo-test" + seed, Type = type, IsDemo = true, ExpiresAt = Now.AddDays(30) };
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var featured = await new DemoDataGenerator(db).GenerateAsync(school, new DemoSchoolOptions(type, perGrade, seed), Now);
        db.ChangeTracker.Clear();
        return (school, featured);
    }

    private static DemoSchoolService Service(AppDbContext db, FakeEmailSender mail, DateTimeOffset? now = null, MessageAttachmentStorageService? storage = null) =>
        new(db,
            new DemoDataGenerator(db),
            new DemoSchoolPurger(db, storage ?? new MessageAttachmentStorageService(new FakeWebHostEnvironment())),
            mail,
            Options.Create(new AppLinkOptions { PathBasedTenant = true, FrontendBaseUrl = "http://localhost:4200" }),
            Options.Create(new DemoOptions { MaxActive = 3 }),
            new FakeClock(now ?? Now),
            NullLogger<DemoSchoolService>.Instance);

    [Fact]
    public async Task GeneratesAFullElementarySchool()
    {
        using var db = NewDb();
        var (school, featured) = await GenerateAsync(db);
        var id = school.Id;

        var classes = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.SchoolId == id).ToListAsync();
        Assert.Equal(18, classes.Count);
        Assert.All(classes, c => Assert.NotNull(c.ClassTeacherId));
        Assert.Equal(classes.Count, classes.Select(c => c.ClassTeacherId).Distinct().Count());

        var students = await db.Students.IgnoreQueryFilters().Where(s => s.SchoolId == id).ToListAsync();
        Assert.All(students.GroupBy(s => s.SchoolClassId), g => Assert.InRange(g.Count(), 10, 20));

        // Every pupil has a parent; parents and children share the surname
        // stem (Novák / Nováková).
        var links = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == id)
            .Select(ps => new { ps.StudentId, ps.ParentProfile!.Person!.LastName })
            .ToListAsync();
        Assert.All(students, s => Assert.Contains(links, l => l.StudentId == s.Id));
        Assert.All(links, l =>
        {
            var child = students.First(s => s.Id == l.StudentId).LastName;
            Assert.True(DemoNames.Surnames.Any(n => (n.Male == child || n.Female == child) && (n.Male == l.LastName || n.Female == l.LastName)),
                $"{child} vs. {l.LastName}");
        });

        // Agendas aren't empty.
        Assert.True(await db.TimetableEntries.IgnoreQueryFilters().CountAsync(t => t.SchoolId == id) > 18 * 19);
        Assert.True(await db.Grades.IgnoreQueryFilters().CountAsync(g => g.SchoolId == id) > 1000);
        Assert.True(await db.Absences.IgnoreQueryFilters().AnyAsync(a => a.SchoolId == id));
        Assert.True(await db.Homework.IgnoreQueryFilters().AnyAsync(h => h.SchoolId == id));
        Assert.True(await db.Events.IgnoreQueryFilters().AnyAsync(e => e.SchoolId == id));
        Assert.True(await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.SchoolId == id));
        Assert.True(await db.Conversations.IgnoreQueryFilters().AnyAsync(c => c.SchoolId == id));
        Assert.True(await db.BehaviorRecords.IgnoreQueryFilters().AnyAsync(b => b.SchoolId == id));
        Assert.False(await db.Grades.IgnoreQueryFilters().AnyAsync(g => g.SchoolId == id && g.GradedOn > DateOnly.FromDateTime(Now.Date)));

        // Featured users exist with the right roles; nobody has a password.
        var users = await db.Users.IgnoreQueryFilters().Where(u => u.SchoolId == id).ToListAsync();
        Assert.Equal(UserRole.Admin, users.Single(u => u.Id == featured.AdminId).Role);
        Assert.Equal(UserRole.Teacher, users.Single(u => u.Id == featured.TeacherId).Role);
        Assert.Equal(UserRole.Parent, users.Single(u => u.Id == featured.ParentId).Role);
        Assert.Equal(UserRole.Student, users.Single(u => u.Id == featured.StudentId).Role);
        Assert.Single(users, u => u.Role == UserRole.Admin);
        Assert.All(users, u => Assert.Equal("", u.PasswordHash));
        Assert.Equal(users.Count, users.Select(u => u.Username).Distinct().Count());
        // Not every pupil or parent has an account.
        Assert.Contains(students, s => s.UserId is null);
    }

    [Fact]
    public async Task SecondarySchoolHasFourGrades()
    {
        using var db = NewDb();
        var (school, _) = await GenerateAsync(db, SchoolType.Secondary, perGrade: 1);

        var classes = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.SchoolId == school.Id).ToListAsync();
        Assert.Equal(["1.A", "2.A", "3.A", "4.A"], classes.Select(c => c.Name).Order());
    }

    [Fact]
    public async Task SameSeedSameSchool()
    {
        static async Task<List<string>> Summary(int seed)
        {
            using var db = NewDb();
            var (school, _) = await GenerateAsync(db, seed: seed);
            var students = await db.Students.IgnoreQueryFilters().Where(s => s.SchoolId == school.Id)
                .OrderBy(s => s.Id).Select(s => s.FirstName + " " + s.LastName).ToListAsync();
            var grades = await db.Grades.IgnoreQueryFilters().Where(g => g.SchoolId == school.Id)
                .OrderBy(g => g.Id).Select(g => g.Value).ToListAsync();
            return [.. students, string.Join("", grades)];
        }

        Assert.Equal(await Summary(7), await Summary(7));
        Assert.NotEqual(await Summary(7), await Summary(8));
    }

    [Fact]
    public async Task PurgeLeavesNothingOfTheSchool()
    {
        var name = Guid.NewGuid().ToString();
        using var db = NewDb(name);
        var (school, _) = await GenerateAsync(db, perGrade: 1);
        var (other, _) = await GenerateAsync(db, perGrade: 1, seed: 5);
        var env = new FakeWebHostEnvironment();
        var storage = new MessageAttachmentStorageService(env);
        var file = Path.Combine(env.ContentRootPath, "AttachmentStorage", school.Id.ToString(), "x.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "x");

        Assert.True(await new DemoSchoolPurger(db, storage).PurgeAsync(school.Id, deleteSchool: true));

        // Every tenant table, found through the model so a new one can't be forgotten.
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var property = entity.FindProperty("SchoolId") ?? (entity.ClrType == typeof(School) ? entity.FindProperty("Id") : null);
            if (property is null)
            {
                continue;
            }
            var rows = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                .MakeGenericMethod(entity.ClrType).Invoke(db, null)!;
            var left = rows.IgnoreQueryFilters().AsEnumerable()
                .Count(r => Equals(property.PropertyInfo!.GetValue(r), school.Id));
            Assert.True(left == 0, $"{entity.ClrType.Name}: {left} rows left");
        }
        Assert.False(File.Exists(file));
        Assert.Empty(await db.Users.IgnoreQueryFilters().Where(u => u.SchoolId == school.Id).ToListAsync());
        Assert.NotEmpty(await db.Students.IgnoreQueryFilters().Where(s => s.SchoolId == other.Id).ToListAsync());
    }

    [Fact]
    public async Task PurgeRefusesARealSchool()
    {
        using var db = NewDb();
        var school = new School { Name = "Ostrá", Slug = "ostra" };
        db.Schools.Add(school);
        db.Students.Add(new Student { FirstName = "A", LastName = "B", SchoolId = 1 });
        await db.SaveChangesAsync();

        Assert.False(await new DemoSchoolPurger(db, new MessageAttachmentStorageService(new FakeWebHostEnvironment()))
            .PurgeAsync(school.Id, deleteSchool: true));
        Assert.Single(db.Schools);
        Assert.Single(db.Students.IgnoreQueryFilters());
    }

    [Fact]
    public async Task UserListAndLoginRefuseARealSchool()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Name = "Ostrá", Slug = "ostra" });
        db.Users.Add(new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = 1 });
        await db.SaveChangesAsync();
        var controller = Controller(db);

        Assert.IsType<NotFoundResult>((await controller.GetUsers("ostra")).Result);
        Assert.IsType<NotFoundResult>((await controller.Login("ostra", new DemoLoginRequest(1))).Result);
        Assert.IsType<NotFoundResult>(await controller.Delete("ostra"));
        Assert.Single(db.Schools);
    }

    [Fact]
    public async Task UserListOffersFeaturedUsersAndLoginWorksWithoutPassword()
    {
        using var db = NewDb();
        var (school, featured) = await GenerateAsync(db, perGrade: 1);
        var tracked = await db.Schools.FirstAsync(s => s.Id == school.Id);
        tracked.DemoFeaturedUserIds = [featured.AdminId, featured.TeacherId, featured.ParentId, featured.StudentId];
        await db.SaveChangesAsync();
        var controller = Controller(db);

        var list = Assert.IsType<OkObjectResult>((await controller.GetUsers(school.Slug)).Result).Value as IEnumerable<DemoUserDto>;
        var users = list!.ToList();
        Assert.Equal(4, users.Count(u => u.Featured));
        Assert.Equal("Admin", users[0].Role);
        Assert.Contains(users, u => u.Role == "Teacher" && u.Detail.StartsWith("třídní "));
        Assert.Contains(users, u => u.Role == "Parent" && u.Detail.Contains('('));

        var login = await controller.Login(school.Slug, new DemoLoginRequest(featured.TeacherId));
        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(login.Result).Value);
        Assert.Equal("Teacher", response.Role);
        Assert.Equal(school.Slug, response.SchoolSlug);

        // A user of another school can't be smuggled in.
        Assert.IsType<NotFoundResult>((await controller.Login(school.Slug, new DemoLoginRequest(999_999))).Result);
    }

    [Fact]
    public async Task CreateMakesADemoAndMailsTheLink()
    {
        using var db = NewDb();
        var mail = new FakeEmailSender();

        var (demo, error) = await Service(db, mail).CreateAsync(
            new DemoSetupRequest(SchoolType.Secondary, "Gymnázium Ukázkov", 1, "navstevnik@example.com", Consent: true));

        Assert.Equal(DemoCreateError.None, error);
        Assert.StartsWith(DemoSlugs.Prefix, demo!.Slug);
        Assert.Equal(DemoSlugs.Prefix.Length + DemoSlugs.IdLength, demo.Slug.Length);
        var school = await db.Schools.SingleAsync();
        Assert.True(school.IsDemo);
        Assert.Equal(Now.AddDays(30), school.ExpiresAt);
        Assert.Equal(4, school.DemoFeaturedUserIds!.Count);
        var contact = await db.DemoContacts.SingleAsync();
        Assert.True(contact.Consent);
        var (to, _, body) = Assert.Single(mail.Sent);
        Assert.Equal("navstevnik@example.com", to);
        Assert.Contains($"http://localhost:4200/{demo.Slug}/login", body);
    }

    [Fact]
    public async Task CreateRefusesBeyondTheActiveLimit()
    {
        using var db = NewDb();
        for (var i = 0; i < 3; i++)
        {
            db.Schools.Add(new School { Name = "D", Slug = $"demo-{i}", IsDemo = true });
        }
        await db.SaveChangesAsync();

        var (demo, error) = await Service(db, new FakeEmailSender()).CreateAsync(new DemoSetupRequest(SchoolType.Elementary, "Škola", 1));

        Assert.Null(demo);
        Assert.Equal(DemoCreateError.TooManyActive, error);
    }

    [Fact]
    public async Task ResetRegeneratesButKeepsTheExpiry()
    {
        using var db = NewDb();
        var service = Service(db, new FakeEmailSender());
        var (demo, _) = await service.CreateAsync(new DemoSetupRequest(SchoolType.Elementary, "Škola", 1));
        var school = await db.Schools.SingleAsync();
        var studentsBefore = await db.Students.IgnoreQueryFilters().Select(s => s.Id).ToListAsync();

        var reset = await Service(db, new FakeEmailSender(), Now.AddDays(10))
            .ResetAsync(school, new DemoSetupRequest(SchoolType.Secondary, "Gymnázium", 1));

        Assert.Equal(demo!.Slug, reset!.Slug);
        Assert.Equal(demo.ExpiresAt, reset.ExpiresAt);
        var fresh = await db.Schools.SingleAsync();
        Assert.Equal(SchoolType.Secondary, fresh.Type);
        Assert.Equal("Gymnázium", fresh.Name);
        var studentsAfter = await db.Students.IgnoreQueryFilters().Select(s => s.Id).ToListAsync();
        Assert.Empty(studentsBefore.Intersect(studentsAfter));
        Assert.Equal(4, await db.SchoolClasses.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task DailyJobDeletesExpiredDemosAndRemindsVisitors()
    {
        using var db = NewDb();
        db.Schools.AddRange(
            new School { Name = "Prošlá", Slug = "demo-old", IsDemo = true, ExpiresAt = Now.AddHours(-1) },
            new School { Name = "Za týden", Slug = "demo-week", IsDemo = true, ExpiresAt = Now.AddDays(6) },
            new School { Name = "Ostrá", Slug = "ostra", ExpiresAt = Now.AddDays(-100) });
        await db.SaveChangesAsync();
        db.DemoContacts.AddRange(
            new DemoContact { SchoolId = 1, DemoSlug = "demo-old", Email = "a@example.com", Consent = false, Source = "wizard" },
            new DemoContact { SchoolId = 2, DemoSlug = "demo-week", Email = "b@example.com", Consent = true, Source = "wizard" });
        await db.SaveChangesAsync();
        var mail = new FakeEmailSender();

        await Service(db, mail).RunDailyAsync();

        Assert.Equal(["demo-week", "ostra"], await db.Schools.OrderBy(s => s.Slug).Select(s => s.Slug).ToListAsync());
        // The expired demo's contact had no consent — gone with it.
        var contact = await db.DemoContacts.SingleAsync();
        Assert.True(contact.Warned7Days);
        Assert.False(contact.Warned1Day);
        Assert.Equal("b@example.com", Assert.Single(mail.Sent).To);

        await Service(db, mail).RunDailyAsync();
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task DemoSchoolEmailsAreStoredNotSent()
    {
        var name = Guid.NewGuid().ToString();
        using (var setup = NewDb(name))
        {
            setup.Schools.AddRange(
                new School { Id = 1, Name = "Demo", Slug = "demo-x", IsDemo = true },
                new School { Id = 2, Name = "Ostrá", Slug = "ostra" });
            await setup.SaveChangesAsync();
        }
        var smtp = new FakeEmailSender();

        using (var demoDb = NewDb(name, schoolId: 1))
        {
            await new DemoAwareEmailSender(demoDb, new FakeCurrentSchoolContext(1), smtp).SendAsync("x@example.com", "Pozvánka", "<p>odkaz</p>");
            Assert.Equal("Pozvánka", (await demoDb.DemoEmails.SingleAsync()).Subject);
        }
        Assert.Empty(smtp.Sent);

        using var realDb = NewDb(name, schoolId: 2);
        await new DemoAwareEmailSender(realDb, new FakeCurrentSchoolContext(2), smtp).SendAsync("y@example.com", "Pozvánka", "<p>odkaz</p>");
        Assert.Single(smtp.Sent);
    }

    private static DemoController Controller(AppDbContext db) =>
        new(db, Service(db, new FakeEmailSender()), new DemoSchoolPurger(db, new MessageAttachmentStorageService(new FakeWebHostEnvironment())),
            AuthTestServices.Sessions(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
