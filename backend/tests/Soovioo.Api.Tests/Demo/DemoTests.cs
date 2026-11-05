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
using Soovioo.Api.People;
using Soovioo.Api.Timetable;
using Soovioo.Api.Timetable.Rvp;
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
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var featured = await new DemoDataGenerator(db).GenerateAsync(school, new DemoSchoolOptions(type, perGrade, seed), Now);
        db.ChangeTracker.Clear();
        return (school, featured);
    }

    [Fact]
    public async Task GivesPeopleIllustratedPhotos()
    {
        using var db = NewDb();
        var storage = new MessageAttachmentStorageService(new FakeWebHostEnvironment());
        var photos = new Soovioo.Api.People.PhotoStorage(storage);
        var school = new School { Name = "Ukázková škola", Slug = "demo-photos", IsDemo = true, ExpiresAt = Now.AddDays(30) };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var featured = await new DemoDataGenerator(db, photos).GenerateAsync(school, new DemoSchoolOptions(SchoolType.Elementary, 1, 7), Now);
        db.ChangeTracker.Clear();

        var staff = await db.Users.IgnoreQueryFilters().Include(u => u.Person)
            .Where(u => u.SchoolId == school.Id && u.Roles.Any(r => r.Kind == RoleKind.Staff))
            .ToListAsync();
        Assert.True(staff.Count(u => u.Person!.PhotoKey != null) > staff.Count / 2);
        var key = staff.First(u => u.Person!.PhotoKey != null).Person!.PhotoKey!;
        Assert.True(File.Exists(photos.FullPath(key, Soovioo.Api.People.PhotoSize.Thumb)));

        var students = await db.Students.IgnoreQueryFilters().Where(s => s.SchoolId == school.Id).ToListAsync();
        Assert.All(students.Where(s => s.Person!.PhotoKey != null), s => Assert.NotNull(s.PhotoConsentAt));
        Assert.Contains(students, s => s.PhotoConsentAt == null);
        Assert.Equal(Soovioo.Api.People.PhotoStatus.Approved, students.Single(s => s.Person!.UserId == featured.StudentId).Person!.PhotoStatus);
        var featuredChildren = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.ParentProfile!.Person!.UserId == featured.ParentId).Select(ps => ps.StudentId).ToListAsync();
        Assert.Contains(students, s => featuredChildren.Contains(s.Id) && s.Person!.PhotoStatus == Soovioo.Api.People.PhotoStatus.Pending);
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

    [Theory]
    [InlineData(SchoolType.Elementary, "ZV2021")]
    [InlineData(SchoolType.Secondary, "G4")]
    public async Task ClassesHaveAProgramOfTheSchoolTypeAndTheirPlanMeetsIt(SchoolType type, string program)
    {
        var name = Guid.NewGuid().ToString();
        int schoolId;
        using (var generating = NewDb(name: name))
        {
            schoolId = (await GenerateAsync(generating, type, perGrade: 1)).School.Id;
        }

        using var db = NewDb(name, schoolId);
        Assert.All(await db.SchoolClasses.ToListAsync(), c => Assert.Equal(program, c.RvpProgramCode));
        var version = await db.TimetableVersions.SingleAsync();
        var plan = await TeachingPlan.LoadAsync(db, version);
        // The timetable holds all the plan has, Tv (and seminars) in groups.
        var rows = plan.Classes.SelectMany(c => c.Rows).ToList();
        Assert.All(rows.Where(r => r.Groups.Count == 0), r => Assert.Equal(r.PlanHours ?? 0, r.TimetableHours));
        Assert.All(rows.Where(r => r.Groups.Count > 0), r => Assert.Equal(0, r.TimetableHours));
        Assert.NotEmpty(plan.Groups);
        Assert.All(plan.Groups, g => Assert.Equal(g.PlanHours, g.TimetableHours));
        var check = await RvpCheck.LoadAsync(db, plan);
        var programCheck = Assert.Single(check.Programs);
        Assert.Empty(programCheck.Problems);
        Assert.All(programCheck.Stages, s => Assert.Empty(s.MissingGrades));
        Assert.Empty(check.ClassesWithout);
    }

    [Theory]
    [InlineData(SchoolType.Elementary, 1)]
    [InlineData(SchoolType.Elementary, 3)]
    [InlineData(SchoolType.Secondary, 2)]
    public async Task DividesTvAndSeminarsIntoGroupsTaughtAtOnce(SchoolType type, int perGrade)
    {
        var name = Guid.NewGuid().ToString();
        int schoolId;
        using (var generating = NewDb(name: name))
        {
            schoolId = (await GenerateAsync(generating, type, perGrade)).School.Id;
        }

        using var db = NewDb(name, schoolId);
        var version = await db.TimetableVersions.SingleAsync();
        var divisions = await db.GroupDivisions.Include(d => d.Groups).ThenInclude(g => g.Members).ToListAsync();
        var classes = await db.SchoolClasses.ToListAsync();
        var students = await db.Students.ToListAsync();
        var entries = await db.TimetableEntries.ToListAsync();
        var upper = classes.Where(c => type == SchoolType.Secondary || c.Rocnik >= 6).ToList();
        Assert.Equal(upper.Count / perGrade + (type == SchoolType.Secondary ? 2 : 0), divisions.Count);
        Assert.Contains(divisions, d => d.Name == (type == SchoolType.Secondary ? "Semináře 3. ročník" : "Tv 6. ročník"));
        foreach (var division in divisions)
        {
            // Every pupil of its classes in one group of it, girls and boys apart.
            var pupils = students.Where(s => division.ClassIds.Contains(s.SchoolClassId)).Select(s => s.Id).Order();
            Assert.Equal(pupils, division.Groups.SelectMany(g => g.Members).Select(m => m.StudentId).Order());
            if (division.Name.StartsWith("Tv"))
            {
                Assert.All(division.Groups[0].Members, m => Assert.Equal(Gender.Female, students.Single(s => s.Id == m.StudentId).Gender));
            }
            // The groups at the same time, each in each class, by another teacher and in another room.
            var slots = division.Groups.Select(g => entries.Where(e => e.StudentGroupId == g.Id)
                .Select(e => (e.DayOfWeek, e.PeriodNumber, e.SchoolClassId)).Order().ToList()).ToList();
            Assert.All(slots, s => Assert.Equal(slots[0], s));
            Assert.Equal(division.ClassIds.Count * 2 * (division.Name.StartsWith("Tv") ? 2 : division.Name.Contains('3') ? 5 : 6),
                slots.Sum(s => s.Count));
            var lessons = entries.Where(e => division.Groups.Any(g => g.Id == e.StudentGroupId)).GroupBy(e => (e.DayOfWeek, e.PeriodNumber));
            Assert.All(lessons, l =>
            {
                Assert.Equal(division.Groups.Count, l.Select(e => e.TeacherId).Distinct().Count());
                Assert.Equal(division.Groups.Count, l.Select(e => e.ClassroomId).Distinct().Count());
            });
        }
        // No whole-class Tv in the divided classes.
        Assert.DoesNotContain(entries, e => e.StudentGroupId == null && upper.Any(c => c.Id == e.SchoolClassId)
            && divisions.Any(d => d.ClassIds.Contains(e.SchoolClassId) && d.Groups[0].SubjectId == e.SubjectId));

        var problems = await TimetableCheck.ProblemsAsync(db, version);
        Assert.DoesNotContain(problems, p => divisions.Any(d => p.Contains(d.Name)) || divisions.SelectMany(d => d.Groups).Any(g => p.Contains(g.Name)));
        if (type == SchoolType.Secondary)
        {
            var homework = await db.Homework.Where(h => h.StudentGroupId != null).ToListAsync();
            Assert.NotEmpty(homework);
            Assert.All(homework, h => Assert.Null(h.SchoolClassId));
        }
    }

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
            var child = students.First(s => s.Id == l.StudentId).Person!.LastName;
            Assert.True(DemoNames.Surnames.Any(n => (n.Male == child || n.Female == child) && (n.Male == l.LastName || n.Female == l.LastName)),
                $"{child} vs. {l.LastName}");
        });

        // Every class has its own homeroom, shown in the timetable.
        var homerooms = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.SchoolId == id).Select(c => c.HomeroomId).ToListAsync();
        Assert.DoesNotContain(null, homerooms);
        Assert.Equal(homerooms.Count, homerooms.Distinct().Count());

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
        Assert.Equal(UserRole.Admin, users.Single(u => u.Id == featured.AdminId).ActiveRole!.BuiltIn);
        Assert.Equal(UserRole.Teacher, users.Single(u => u.Id == featured.TeacherId).ActiveRole!.BuiltIn);
        Assert.Equal(UserRole.Parent, users.Single(u => u.Id == featured.ParentId).ActiveRole!.BuiltIn);
        Assert.Equal(UserRole.Student, users.Single(u => u.Id == featured.StudentId).ActiveRole!.BuiltIn);
        Assert.Single(users, u => u.ActiveRole!.BuiltIn == UserRole.Admin);
        Assert.All(users, u => Assert.Equal("", u.PasswordHash));
        var personUserIds = await db.Persons.IgnoreQueryFilters().Where(p => p.UserId != null).Select(p => p.UserId!.Value).ToListAsync();
        Assert.All(users, u => Assert.Contains(u.Id, personUserIds));
        Assert.Equal(users.Count, users.Select(u => u.Username).Distinct().Count());
        // Not every pupil or parent has an account.
        Assert.Contains(students, s => s.Person!.UserId is null);
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
                .OrderBy(s => s.Id).Select(s => s.Person!.FirstName + " " + s.Person!.LastName).ToListAsync();
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
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        db.Students.Add(new StudentProfile { Person = new Person { FirstName = "A", LastName = "B", SchoolId = 1 }, SchoolId = 1 });
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
        db.Users.Add(new User { Username = "admin", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, 1), SchoolId = 1 });
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
        Assert.Equal("Staff", response.Role);
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
    public async Task ClearKeepsOnlyTheLogAndOneAdmin()
    {
        using var db = NewDb();
        var (demo, _) = await Service(db, new FakeEmailSender()).CreateAsync(new DemoSetupRequest(SchoolType.Elementary, "Škola", 1));
        var school = await db.Schools.SingleAsync();
        db.AuditLogEntries.Add(new Soovioo.Api.Audit.AuditLogEntry { SchoolId = school.Id, Method = "POST", Path = "/api/grades", Area = "Známky" });
        await db.SaveChangesAsync();
        var adminId = demo!.Featured.AdminId;

        var kept = await Service(db, new FakeEmailSender()).ClearAsync(school);

        Assert.Equal(adminId, kept);
        Assert.Equal(adminId, Assert.Single(await db.Users.IgnoreQueryFilters().ToListAsync()).Id);
        Assert.Single(await db.Persons.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Students.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.SchoolClasses.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Grades.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.AuditLogEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal([adminId], (await db.Schools.SingleAsync()).DemoFeaturedUserIds);
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

    [Fact]
    public async Task FeedbackIsStoredMailedAndForgottenWithTheDemo()
    {
        using var db = NewDb();
        var school = new School { Name = "Demo", Slug = "demo-fb", IsDemo = true, ExpiresAt = Now.AddDays(30) };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var mail = new FakeEmailSender();
        var service = Service(db, mail);
        var controller = new DemoController(db, service, new DemoSchoolPurger(db, new MessageAttachmentStorageService(new FakeWebHostEnvironment())),
            AuthTestServices.Sessions(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Feedback("demo-fb", new DemoFeedbackRequest(null, " ", null)));
        Assert.IsType<NoContentResult>(await controller.Feedback("demo-fb", new DemoFeedbackRequest(4, "Líbí se mi rozvrh.", "a@example.com", Consent: false, Role: "Teacher")));

        var feedback = await db.DemoFeedback.SingleAsync();
        Assert.Equal(4, feedback.Rating);
        Assert.Equal("feedback", (await db.DemoContacts.SingleAsync()).Source);
        var (to, subject, body) = Assert.Single(mail.Sent);
        Assert.Equal("info@soovioo.cz", to);
        Assert.Contains("★★★★☆", subject);
        Assert.Contains("Líbí se mi rozvrh.", body);

        await new DemoSchoolPurger(db, new MessageAttachmentStorageService(new FakeWebHostEnvironment())).PurgeAsync(school.Id, deleteSchool: true);
        var kept = await db.DemoFeedback.SingleAsync();
        Assert.Null(kept.Email);
        Assert.Null(kept.SchoolId);
        Assert.Empty(db.DemoContacts);
    }

    [Fact]
    public async Task DemoSchoolsHaveSmallerAttachmentLimits()
    {
        using var db = NewDb();
        db.Schools.AddRange(
            new School { Id = 1, Name = "Demo", Slug = "demo-x", IsDemo = true },
            new School { Id = 2, Name = "Ostrá", Slug = "ostra" });
        db.MessageAttachments.Add(new MessageAttachment
        {
            MessageId = 1, SchoolId = 1, FileName = "a.pdf", ContentType = "application/pdf",
            SizeBytes = MessageAttachmentRules.DemoMaxTotalBytes - 1000, StoragePath = "1/a.pdf",
        });
        await db.SaveChangesAsync();

        Assert.Null(await MessageAttachmentRules.DemoLimitErrorAsync(db, 2, 5_000_000));
        Assert.NotNull(await MessageAttachmentRules.DemoLimitErrorAsync(db, 1, 3_000_000));
        Assert.NotNull(await MessageAttachmentRules.DemoLimitErrorAsync(db, 1, 2_000));
        Assert.Null(await MessageAttachmentRules.DemoLimitErrorAsync(db, 1, 500));
    }

    private static DemoController Controller(AppDbContext db) =>
        new(db, Service(db, new FakeEmailSender()), new DemoSchoolPurger(db, new MessageAttachmentStorageService(new FakeWebHostEnvironment())),
            AuthTestServices.Sessions(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
