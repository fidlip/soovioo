using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Live;
using Soovioo.Api.Notifications;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Support;

public class SupportTicketsControllerTests
{
    private readonly string dbName = Guid.NewGuid().ToString();

    // Monday 2026-11-09 11:00 Prague.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));
    private readonly Soovioo.Api.Tests.Live.RecordingHub hub = new();
    private readonly FakeBackofficeTickets backoffice = new();

    private AppDbContext SuperAdminDb() => new(Options(), new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private AppDbContext SchoolDb(int schoolId) => new(Options(), new FakeCurrentSchoolContext(schoolId));

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private static HttpContext Http(int userId, params Claim[] extra) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extra])),
    };

    private static SupportPlanClient Plans(
        SupportPlanOptions? options = null, Soovioo.Api.Tests.Backoffice.FakeBackoffice? client = null, IMemoryCache? cache = null) =>
        new(client ?? Soovioo.Api.Tests.Backoffice.FakeBackoffice.NotConnected(),
            Microsoft.Extensions.Options.Options.Create(options ?? new SupportPlanOptions()),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SupportPlanClient>.Instance);

    private SupportTicketsController Tickets(
        AppDbContext db, int userId, int schoolId, IBackofficeTickets? tickets = null, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(schoolId), tickets ?? backoffice,
            Plans(new SupportPlanOptions { Default = new SupportPlanDto("Paid", "Placený", 2) }),
            new SupportReset(db, clock), hub.Publisher(), clock, AuthTestServices.SupportKeys(db, clock),
            Microsoft.Extensions.Options.Options.Create(new SupportOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId, extra) },
        };

    private SupportTicketsController School(AppDbContext db, World w, User? user = null, params Claim[] extra) =>
        Tickets(db, (user ?? w.Admin).Id, w.School.Id, null, extra);

    /// <summary>With the user's active role, as their token has it.</summary>
    private SupportPolicyController Policy(AppDbContext db, int userId, int schoolId, params Claim[] extra)
    {
        var roleId = db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.ActiveRoleId).Single();
        return new(db, new FakeCurrentSchoolContext(schoolId),
            Microsoft.Extensions.Options.Options.Create(new SupportOptions()), clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = Http(userId, [new Claim(PermissionAccess.RoleIdClaim, roleId.ToString()), .. extra]),
            },
        };
    }

    private record World(School School, User SuperAdmin, User Other, User Admin, User Deputy, User Teacher);

    /// <summary>Two admins, and a teacher with Administrace who isn't one.</summary>
    private async Task<World> SeedAsync(SupportAccess access = SupportAccess.Approval)
    {
        using var db = SuperAdminDb();
        var school = new School { Name = "ZŠ Test", Slug = "test", SupportAccess = access, SupportAs = SupportAsScope.Admin };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        var superAdmin = new User { Username = "superadmin", PasswordHash = "x", IsSuperAdmin = true };
        var other = new User { Username = "druhy", PasswordHash = "x", IsSuperAdmin = true };
        db.Users.AddRange(superAdmin, other);
        await db.SaveChangesAsync();
        var adminRole = school.Roles.Single(r => r.BuiltIn == UserRole.Admin);
        var teacherRole = school.Roles.Single(r => r.BuiltIn == UserRole.Teacher);
        User Account(string name, Role role) => new()
        {
            Username = name, PasswordHash = "x", SchoolId = school.Id, ActiveRole = role, Roles = [role],
        };
        var admin = Account("reditel", adminRole);
        var deputy = Account("zastupce", adminRole);
        var teacher = Account("ucitel", teacherRole);
        db.Users.AddRange(admin, deputy, teacher);
        await db.SaveChangesAsync();
        return new World(school, superAdmin, other, admin, deputy, teacher);
    }

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static ObjectResult Problem<T>(ActionResult<T> result) => Assert.IsAssignableFrom<ObjectResult>(result.Result);

    private async Task<int> SchoolTicketAsync(World w, User? author = null)
    {
        using var db = SchoolDb(w.School.Id);
        return Ok(await School(db, w, author)
            .CreateSchoolTicket(new CreateSchoolTicketRequest("Import žáků", "Z **Bakalářů**"))).Ticket.Id;
    }

    private async Task<int> SessionAsync(World w, int ticketId)
    {
        using var db = SuperAdminDb();
        var session = new SupportSession
        {
            SuperAdminUserId = w.SuperAdmin.Id, SuperAdminUsername = "superadmin", SchoolId = w.School.Id,
            TicketId = ticketId, TicketTitle = "Import žáků", TicketAuthorUserId = w.Admin.Id,
            StartedAt = clock.Now, ExpiresAt = clock.Now.AddHours(2), DisconnectedAt = clock.Now,
        };
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    [Fact]
    public async Task TheSchoolSeesItsPlan()
    {
        var w = await SeedAsync();
        using var db = SchoolDb(w.School.Id);
        Assert.Equal(new SupportPlanDto("Paid", "Placený", 2), await School(db, w).GetSchoolPlan());
    }

    [Fact]
    public async Task TheSchoolAsksAndAnswersInTheBackofficeAsItsUserWithTheThreadTheBackofficeKeeps()
    {
        var w = await SeedAsync();
        using var db = SchoolDb(w.School.Id);
        Assert.IsType<BadRequestObjectResult>((await School(db, w)
            .CreateSchoolTicket(new CreateSchoolTicketRequest(" ", "x"))).Result);
        Assert.IsType<BadRequestObjectResult>((await School(db, w)
            .CreateSchoolTicket(new CreateSchoolTicketRequest(new string('x', 201), "x"))).Result);
        Assert.Empty(backoffice.Calls);

        var created = Ok(await School(db, w)
            .CreateSchoolTicket(new CreateSchoolTicketRequest(" Import žáků ", "Z **Bakalářů**", EstimateRequested: true)));
        var id = created.Ticket.Id;
        var call = Assert.Single(backoffice.Calls);
        Assert.Equal((w.School.Id, "create", new TicketActor(w.Admin.Id, "reditel"), "Import žáků"),
            (call.SchoolId, call.Action, call.By, call.Body));
        Assert.Equal(["Created", "EstimateRequested"], created.Entries.Select(e => e.Kind));

        Assert.IsType<BadRequestObjectResult>((await School(db, w).AddSchoolMessage(id, new SupportTicketMessageRequest(" "))).Result);
        backoffice.Support(id, t => t with { Entries = [.. t.Entries, backoffice.Entry("Message", "Pavel z podpory", true, "Pošlete export")] });
        var answered = Ok(await School(db, w, w.Deputy).AddSchoolMessage(id, new SupportTicketMessageRequest(" Posílám ")));
        Assert.Equal(new TicketActor(w.Deputy.Id, "zastupce"), backoffice.Calls[^1].By);
        Assert.Equal("""{"text":"Posílám"}""", backoffice.Calls[^1].Body);
        var support = answered.Entries.Single(e => e.BySupport);
        Assert.False(support.CanEdit);
        // A colleague with the same role may change it, as the author may.
        Assert.True(answered.Entries.Single(e => e.Text == "Posílám").CanEdit);
        Assert.True(Ok(await School(db, w).GetSchoolTicket(id)).Entries.Single(e => e.Text == "Posílám").CanEdit);
        Assert.False(Ok(await School(db, w, w.Teacher).GetSchoolTicket(id)).Entries.Single(e => e.Text == "Posílám").CanEdit);

        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Ticket && s.To.SequenceEqual([LiveGroups.Ticket(w.School.Id, id)]));
        var listed = Assert.Single(Ok(await School(db, w).GetSchoolTickets()));
        Assert.Equal(("Import žáků", SupportTicketStatus.New), (listed.Title, listed.Status));
    }

    [Fact]
    public async Task OnlyTheAuthorsRoleChangesAMessageAndNeverTheSupports()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        backoffice.Support(id, t => t with { Entries = [.. t.Entries, backoffice.Entry("Message", "Pavel z podpory", true, "Hotovo?")] });
        using var db = SchoolDb(w.School.Id);
        var mine = Ok(await School(db, w).AddSchoolMessage(id, new SupportTicketMessageRequest("Ještě ne"))).Entries
            .Single(e => e.Text == "Ještě ne").Id;
        var theirs = backoffice.Tickets[id].Entries.Single(e => e.BySupport).Id;
        var calls = backoffice.Calls.Count;

        Assert.IsType<BadRequestObjectResult>((await School(db, w, w.Teacher)
            .EditSchoolEntry(id, mine, new EditSupportTicketEntryRequest("Hotovo"))).Result);
        Assert.IsType<NotFoundResult>((await School(db, w).DeleteSchoolEntry(id, theirs)).Result);
        Assert.Equal(calls, backoffice.Calls.Count);

        Assert.Equal("Hotovo", Ok(await School(db, w, w.Deputy)
            .EditSchoolEntry(id, mine, new EditSupportTicketEntryRequest(" Hotovo "))).Entries.Single(e => e.Id == mine).Text);
        Assert.True(Ok(await School(db, w).DeleteSchoolEntry(id, mine)).Entries.Single(e => e.Id == mine).Deleted);
    }

    [Fact]
    public async Task TheAuthorConfirmsAResolvedTicketAnyAdminWhenTheAuthorIsNoLongerOne()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        backoffice.Support(id, t => t with { Status = SupportTicketStatus.Resolved });
        using (var db = SchoolDb(w.School.Id))
        {
            Assert.False(Ok(await School(db, w, w.Deputy).GetSchoolTicket(id)).Ticket.CanConfirm);
            Assert.Equal("O vyřešení rozhoduje autor požadavku (reditel).", Assert.IsType<BadRequestObjectResult>(
                (await School(db, w, w.Deputy).CloseTicket(id)).Result).Value);
            Assert.True(Ok(await School(db, w).GetSchoolTicket(id)).Ticket.CanConfirm);
            Assert.IsType<BadRequestObjectResult>((await School(db, w).ReturnTicket(id, new ReturnSupportTicketRequest(""))).Result);
            Assert.Equal(SupportTicketStatus.Assigned, Ok(await School(db, w)
                .ReturnTicket(id, new ReturnSupportTicketRequest("Nefunguje to"))).Ticket.Status);
        }

        backoffice.Support(id, t => t with { Status = SupportTicketStatus.Resolved });
        using (var db = SchoolDb(w.School.Id))
        {
            var author = db.Users.Include(u => u.Roles).Single(u => u.Id == w.Admin.Id);
            author.Roles = [db.Roles.Single(r => r.SchoolId == w.School.Id && r.BuiltIn == UserRole.Teacher)];
            await db.SaveChangesAsync();
            Assert.Equal(SupportTicketStatus.Closed, Ok(await School(db, w, w.Deputy).CloseTicket(id)).Ticket.Status);
        }
    }

    [Fact]
    public async Task CancellingEndsTheVisitsAndInSupportModeTheSchoolManagesNothing()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        var sessionId = await SessionAsync(w, id);
        using (var db = SchoolDb(w.School.Id))
        {
            var supportMode = new Claim(SupportClaims.SessionId, sessionId.ToString());
            Assert.Equal(StatusCodes.Status403Forbidden, Problem(await School(db, w, null, supportMode)
                .CreateSchoolTicket(new CreateSchoolTicketRequest("A", ""))).StatusCode);
            Assert.Equal(StatusCodes.Status403Forbidden, Problem(await School(db, w, null, supportMode)
                .AddSchoolMessage(id, new SupportTicketMessageRequest("A"))).StatusCode);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await Policy(db, w.Admin.Id, w.School.Id, supportMode)
                .SetAccess(new SetSupportAccessRequest(SupportAccess.Always))).Result).StatusCode);

            Assert.IsType<BadRequestObjectResult>((await School(db, w).CancelTicket(id, new CancelSupportTicketRequest(" "))).Result);
            Assert.Equal(SupportTicketStatus.Cancelled, Ok(await School(db, w)
                .CancelTicket(id, new CancelSupportTicketRequest(" Vyřešili jsme sami "))).Ticket.Status);
            Assert.Equal("""{"reason":"Vyřešili jsme sami"}""", backoffice.Calls[^1].Body);
            Assert.IsType<BadRequestObjectResult>((await School(db, w).CancelTicket(id, new CancelSupportTicketRequest("Znovu"))).Result);
        }
        Assert.Equal(StatusCodes.Status401Unauthorized, await GuardAsync(sessionId));
        using (var db = SuperAdminDb())
        {
            var session = db.SupportSessions.Single();
            Assert.Equal("reditel", session.EndedBy);
            Assert.Equal("Škola požadavek zrušila.\n\n**Důvod:** Vyřešili jsme sami", session.EndReason);
        }
    }

    [Fact]
    public async Task OnlyTheSchoolsAdminDecidesTheSupportsEstimates()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        using var db = SchoolDb(w.School.Id);
        Assert.False(Ok(await School(db, w, w.Teacher).GetSchoolTicket(id)).Ticket.CanDecideEstimates);
        Assert.Equal(StatusCodes.Status403Forbidden, Problem(await School(db, w, w.Teacher)
            .ApproveEstimate(id, 5, new EstimateDecisionRequest(null))).StatusCode);

        Assert.True(Ok(await School(db, w).GetSchoolTicket(id)).Ticket.CanDecideEstimates);
        Ok(await School(db, w).RejectEstimate(id, 5, new EstimateDecisionRequest(" Moc drahé ")));
        Assert.Equal(("POST estimates/5/reject", """{"note":"Moc drahé"}"""), (backoffice.Calls[^1].Action, backoffice.Calls[^1].Body));
        Ok(await School(db, w, w.Teacher).DisputeTime(id, 7, new DisputeTimeRequest("Bug", " Chyba aplikace ")));
        Assert.Equal(("POST time/7/dispute", """{"category":"Bug","reason":"Chyba aplikace"}"""),
            (backoffice.Calls[^1].Action, backoffice.Calls[^1].Body));
        Ok(await School(db, w, w.Teacher).RequestEstimate(id));
        Assert.Equal("POST request-estimate", backoffice.Calls[^1].Action);
    }

    [Fact]
    public async Task TheBackofficesRefusalReachesTheSchoolAndWithoutItTheSupportIsUnavailable()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        using var db = SchoolDb(w.School.Id);
        var refused = Problem(await School(db, w).CloseTicket(id));
        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Equal("Uzavřít jde jen vyřešený požadavek.", Assert.IsType<ProblemDetails>(refused.Value).Detail);
        Assert.Equal(StatusCodes.Status404NotFound, Problem(await School(db, w).GetSchoolTicket(id + 1)).StatusCode);

        backoffice.Down = true;
        var down = Problem(await School(db, w).GetSchoolTickets());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, down.StatusCode);
        Assert.Equal(SupportTicketsController.Unavailable, Assert.IsType<ProblemDetails>(down.Value).Detail);

        var none = Problem(await Tickets(db, w.Admin.Id, w.School.Id, FakeBackofficeTickets.NotConnected()).GetSchoolTickets());
        Assert.Equal(StatusCodes.Status404NotFound, none.StatusCode);
    }

    [Fact]
    public async Task WhatTheSupportDidTheSchoolsAdminsHearOfAndAnOpenDetailReloads()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        backoffice.Support(id, t => t with
        {
            Status = SupportTicketStatus.WaitingForSchool,
            Entries = [.. t.Entries, backoffice.Entry("Message", "Pavel z podpory", true, "Pošlete **export**")],
        });
        var entryId = backoffice.Tickets[id].Entries[^1].Id;
        hub.Sent.Clear();
        using (var db = SchoolDb(w.School.Id))
        {
            var changes = new SupportTicketChanges(db, backoffice, new Notifier(db, clock), hub.Publisher(),
                NullLogger<SupportTicketChanges>.Instance);
            Assert.Equal(StatusCodes.Status200OK, (await changes.HandleAsync(Payload("Message", entryId))).StatusCode);
            Assert.Equal(
                [(w.Admin.Id, "Zpráva od podpory: Import žáků", "Pošlete **export**", id), (w.Deputy.Id, "Zpráva od podpory: Import žáků", "Pošlete **export**", id)],
                Heard(db));
            Assert.Equal(StatusCodes.Status200OK, (await changes.HandleAsync(Payload("StatusChanged"))).StatusCode);
            Assert.All(Heard(db), n => Assert.Equal("Podpora čeká na vaši odpověď: Import žáků", n.Title));
            Assert.Equal(StatusCodes.Status200OK, (await changes.HandleAsync(Payload("Time"))).StatusCode);
            Assert.Equal(StatusCodes.Status422UnprocessableEntity, (await changes.HandleAsync(
                JsonDocument.Parse($$"""{"ticketId":{{id}},"schoolId":999,"change":"Message"}""").RootElement)).StatusCode);
        }
        Assert.Equal(3, hub.Sent.Count(s => s.Method == LiveEvents.Ticket && s.To.SequenceEqual([LiveGroups.Ticket(w.School.Id, id)])));

        List<(int UserId, string Title, string? Body, int? TargetId)> Heard(AppDbContext db) => db.Notifications
            .IgnoreQueryFilters().Where(n => n.Type == NotificationType.SupportTicketUpdate && n.ReadAt == null)
            .OrderBy(n => n.UserId).Select(n => new { n.UserId, n.Title, n.Body, n.TargetId }).ToList()
            .Select(n => (n.UserId, n.Title, n.Body, n.TargetId)).ToList();

        JsonElement Payload(string change, int? entry = null) => JsonDocument.Parse(JsonSerializer.Serialize(
            new { ticketId = id, schoolId = w.School.Id, change, status = "WaitingForSchool", entryId = entry })).RootElement;
    }

    [Fact]
    public async Task WhatWasDoneOnLeavingGoesToTheThreadAndTheLog()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        var sessionId = await SessionAsync(w, id);
        using (var db = SchoolDb(w.School.Id))
        {
            SupportController Leaving() => new(
                db, AuthTestServices.SupportSessions(db, clock), AuthTestServices.RefreshTokens(db, clock), clock)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = Http(w.SuperAdmin.Id, new Claim(SupportClaims.SessionId, sessionId.ToString())),
                },
            };
            Assert.IsNotType<NoContentResult>(await Leaving().EndCurrentSession(
                new EndSupportSessionRequest(new string('x', SupportTicketsController.MaxTextLength + 1))));
            Assert.IsType<NoContentResult>(await Leaving().EndCurrentSession(
                new EndSupportSessionRequest("  Import **opraven** ")));

            var entries = Ok(await School(db, w).GetSchoolTicket(id)).Entries;
            var visit = entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.Visit));
            Assert.Equal(("Import **opraven**", true, sessionId), (visit.Text, visit.BySupport, visit.SessionId));
            Assert.Contains(entries, e => e.Kind == nameof(SupportTicketEntryKind.SessionStarted) && e.SessionId == sessionId);
            Assert.DoesNotContain(entries, e => e.Kind == nameof(SupportTicketEntryKind.SessionEnded));
        }
    }

    [Fact]
    public async Task EachRequestOfAVisitMarksItSeenOnceAMinute()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        var sessionId = await SessionAsync(w, id);
        var started = clock.Now;
        Assert.Equal(StatusCodes.Status200OK, await GuardAsync(sessionId));
        clock.Now = started.AddSeconds(30);
        await GuardAsync(sessionId);
        using (var db = SuperAdminDb())
        {
            Assert.Equal(started, db.SupportSessions.Single().LastSeenAt);
        }
        clock.Now = started.AddMinutes(20);
        await GuardAsync(sessionId);
        using (var db = SchoolDb(w.School.Id))
        {
            Assert.Equal(clock.Now, db.SupportSessions.IgnoreQueryFilters().Single().LastSeenAt);
            var visit = Ok(await School(db, w).GetSchoolTicket(id)).Entries
                .Single(e => e.Kind == nameof(SupportTicketEntryKind.SessionStarted));
            Assert.Equal(started, visit.InactiveSince);
        }
    }

    [Fact]
    public async Task AVisitWithNoTabOpenEndsAQuarterAfterItWentQuietAsOfThen()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        var sessionId = await SessionAsync(w, id);
        var started = clock.Now;
        await SetDisconnectedAsync(null);
        clock.Now = started.AddMinutes(5);
        await GuardAsync(sessionId);
        await SetDisconnectedAsync(clock.Now);

        clock.Now = started.AddMinutes(19);
        using (var db = SuperAdminDb())
        {
            await new SupportSessionIdleJob(db, hub.Publisher(), clock).RunAsync();
            Assert.Equal(started.AddHours(2), db.SupportSessions.Single().ExpiresAt);
        }

        clock.Now = started.AddMinutes(20);
        using (var db = SuperAdminDb())
        {
            await new SupportSessionIdleJob(db, hub.Publisher(), clock).RunAsync();
            Assert.Equal(started.AddMinutes(5), db.SupportSessions.Single().ExpiresAt);
        }
        using (var db = SchoolDb(w.School.Id))
        {
            var entries = Ok(await School(db, w).GetSchoolTicket(id)).Entries;
            Assert.Null(entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.SessionStarted)).InactiveSince);
            Assert.Equal(started.AddMinutes(5), entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.SessionExpired)).At);
        }
        Assert.Equal(StatusCodes.Status401Unauthorized, await GuardAsync(sessionId));

        async Task SetDisconnectedAsync(DateTimeOffset? at)
        {
            using var db = SuperAdminDb();
            db.SupportSessions.Single().DisconnectedAt = at;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AVisitRunningOutTellsItsTabTheTicketAndTheBannersOnce()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        var sessionId = await SessionAsync(w, id);
        hub.Sent.Clear();
        var started = clock.Now;
        clock.Now = started.AddHours(2).AddSeconds(-30);
        await GuardAsync(sessionId);
        using (var db = SuperAdminDb())
        {
            await new SupportSessionIdleJob(db, hub.Publisher(), clock).RunAsync();
        }
        Assert.Empty(hub.Sent);

        clock.Now = started.AddHours(2).AddSeconds(30);
        using (var db = SuperAdminDb())
        {
            await new SupportSessionIdleJob(db, hub.Publisher(), clock).RunAsync();
        }
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Support && s.To.SequenceEqual([LiveGroups.Session(sessionId)]));
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Ticket && s.To.SequenceEqual([LiveGroups.Ticket(w.School.Id, id)]));
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Counts && s.To.SequenceEqual([w.Admin.Id.ToString()]));

        hub.Sent.Clear();
        clock.Now = started.AddHours(2).AddMinutes(3);
        using (var db = SuperAdminDb())
        {
            await new SupportSessionIdleJob(db, hub.Publisher(), clock).RunAsync();
        }
        Assert.Empty(hub.Sent);
    }

    [Fact]
    public async Task OnlyTheSchoolsAdminLetsTheSupportIn()
    {
        var w = await SeedAsync();
        using var db = SchoolDb(w.School.Id);
        // Administrace given to another role isn't enough.
        Assert.False(Ok(await Policy(db, w.Teacher.Id, w.School.Id).GetSchoolSupport()).CanChange);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await Policy(db, w.Teacher.Id, w.School.Id)
            .SetAccess(new SetSupportAccessRequest(SupportAccess.Always))).Result).StatusCode);

        Assert.True(Ok(await Policy(db, w.Admin.Id, w.School.Id).GetSchoolSupport()).CanChange);
        Assert.Equal(SupportAccess.Always, Ok(await Policy(db, w.Admin.Id, w.School.Id)
            .SetAccess(new SetSupportAccessRequest(SupportAccess.Always))).Access);
        // Nobody outside the school hears of it.
        Assert.Empty(db.Notifications.IgnoreQueryFilters().Where(n => n.Type == NotificationType.SupportTicket));
    }

    [Fact]
    public async Task SchoolsAreAskedByDefault()
    {
        Assert.Equal(SupportAccess.Approval, new School { Name = "ZŠ", Slug = "zs" }.SupportAccess);
        var w = await SeedAsync();
        using var db = SchoolDb(w.School.Id);
        var policy = Ok(await Policy(db, w.Admin.Id, w.School.Id).GetSchoolSupport());
        Assert.Equal(SupportAccess.Approval, policy.Access);
        Assert.Equal(24 * 60, policy.ApprovalMinutes);
        Assert.Equal(SupportAsScope.Users, new School { Name = "ZŠ", Slug = "zs" }.SupportAs);
    }

    [Fact]
    public void OnlyTheSchoolsAdminsHearAboutTheirTickets()
    {
        var school = new School { Name = "ZŠ", Slug = "zs" };
        RoleSeeder.AddMissing(school);
        Role Built(UserRole r) => school.Roles.Single(x => x.BuiltIn == r);

        Assert.Contains(NotificationType.SupportTicketUpdate, NotificationsController.TypesFor([Built(UserRole.Admin)]));
        Assert.DoesNotContain(NotificationType.SupportTicketUpdate, NotificationsController.TypesFor([Built(UserRole.Teacher)]));
        Assert.DoesNotContain(NotificationType.SupportTicketUpdate, NotificationsController.TypesFor([Built(UserRole.Parent)]));
        Assert.DoesNotContain(NotificationType.SupportTicket, NotificationsController.TypesFor([Built(UserRole.Admin)]));
    }

    [Fact]
    public async Task TheClientCallsTheBackofficeAsTheSchoolsUserAndPassesItsRefusalOn()
    {
        var client = Soovioo.Api.Tests.Backoffice.FakeBackoffice.Answering(
            HttpStatusCode.Conflict, """{"status":409,"detail":"Požadavek už je uzavřený."}""");
        var tickets = new BackofficeTickets(client, NullLogger<BackofficeTickets>.Instance);
        var refused = await Assert.ThrowsAsync<TicketRefusedException>(() => tickets.ActAsync(
            7, 12, new TicketActor(3, "Jana Nováková"), HttpMethod.Post, "messages", new { text = "Ahoj" }));
        Assert.Equal((409, "Požadavek už je uzavřený."), (refused.Status, refused.Message));
        var (request, body) = client.Sent.Single();
        Assert.Equal("http://backoffice.test/instance-api/v1/schools/7/tickets/12/messages", request.RequestUri!.ToString());
        Assert.Equal("""{"text":"Ahoj"}""", body);
        var actor = request.Headers.GetValues(BackofficeTickets.ActorHeader).Single();
        Assert.Equal("""{"userId":3,"name":"Jana Nováková"}""",
            System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(actor)));

        var down = new BackofficeTickets(
            Soovioo.Api.Tests.Backoffice.FakeBackoffice.Answering(HttpStatusCode.BadGateway), NullLogger<BackofficeTickets>.Instance);
        Assert.True((await Assert.ThrowsAsync<SupportUnavailableException>(() => down.ListAsync(7))).Connected);
        var none = new BackofficeTickets(
            Soovioo.Api.Tests.Backoffice.FakeBackoffice.NotConnected(), NullLogger<BackofficeTickets>.Instance);
        Assert.False((await Assert.ThrowsAsync<SupportUnavailableException>(() => none.ListAsync(7))).Connected);
    }

    [Fact]
    public async Task PlanComesFromTheBackofficeWithTheDefaultForUnknownSchoolsAndFailures()
    {
        var options = new SupportPlanOptions();
        var ok = Answering(HttpStatusCode.OK, """{"code":"Priority","name":"Prioritní","responseWorkingDays":1}""");
        var plan = await Plans(options, ok).GetPlanAsync(School(7));
        Assert.Equal(new SupportPlanDto("Priority", "Prioritní", 1), plan);
        Assert.Equal("http://backoffice.test/instance-api/v1/schools/7/plan", ok.Sent.Single().Request.RequestUri!.ToString());

        Assert.Equal(options.Default, await Plans(options, Answering(HttpStatusCode.NotFound)).GetPlanAsync(School(7)));
        Assert.Equal(options.Default, await Plans(options, Answering(HttpStatusCode.InternalServerError)).GetPlanAsync(School(7)));
        Assert.Equal(options.Default, await Plans(options).GetPlanAsync(School(7)));
    }

    [Fact]
    public async Task WhenTheBackofficeFailsTheSchoolKeepsItsLastKnownPlan()
    {
        var options = new SupportPlanOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var priority = new SupportPlanDto("Priority", "Prioritní", 1);
        var ok = Answering(HttpStatusCode.OK, """{"code":"Priority","name":"Prioritní","responseWorkingDays":1}""");
        var down = Answering(HttpStatusCode.InternalServerError);
        var invalid = Answering(HttpStatusCode.OK, """{"code":"","name":"","responseWorkingDays":-1}""");

        await Plans(options, ok, cache).GetPlanAsync(School(7));

        Assert.Equal(priority, await Plans(options, down, cache).GetPlanAsync(School(7)));
        Assert.Equal(priority, await Plans(options, invalid, cache).GetPlanAsync(School(7)));
        Assert.Equal(options.Default, await Plans(options, down, cache).GetPlanAsync(School(8)));

        Assert.Equal(options.Default, await Plans(options, Answering(HttpStatusCode.NotFound), cache).GetPlanAsync(School(7)));
        Assert.Equal(options.Default, await Plans(options, down, cache).GetPlanAsync(School(7)));
    }

    [Fact]
    public async Task APlanTheBackofficeChangedNoLongerStandsInWhenItIsDown()
    {
        var options = new SupportPlanOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var down = Answering(HttpStatusCode.InternalServerError);
        await Plans(options, Answering(HttpStatusCode.OK, """{"code":"Priority","name":"Prioritní","responseWorkingDays":1}"""), cache)
            .GetPlanAsync(School(7));

        Plans(options, down, cache).Forget(7);

        Assert.Equal(options.Default, await Plans(options, down, cache).GetPlanAsync(School(7)));
    }

    [Fact]
    public async Task ADemoSchoolHasTheDemoPlanWhoeverItIs()
    {
        var options = new SupportPlanOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var demo = new SupportPlanDto("Demo", "Demo", 5);
        var ok = Answering(HttpStatusCode.OK, """{"code":"Demo","name":"Demo","responseWorkingDays":5}""");

        Assert.Equal(demo, await Plans(options, ok, cache).GetPlanAsync(School(7, isDemo: true)));
        Assert.Equal("http://backoffice.test/instance-api/v1/plans/Demo", ok.Sent.Single().Request.RequestUri!.ToString());

        var down = Answering(HttpStatusCode.InternalServerError);
        Assert.Equal(demo, await Plans(options, down, cache).GetPlanAsync(School(8, isDemo: true)));
        Assert.Equal(options.Default, await Plans(options, down, cache).GetPlanAsync(School(8)));
    }

    private static Soovioo.Api.Tests.Backoffice.FakeBackoffice Answering(HttpStatusCode status, string body = "") =>
        Soovioo.Api.Tests.Backoffice.FakeBackoffice.Answering(status, body);

    private static School School(int id, bool isDemo = false) =>
        new() { Id = id, Name = "Škola", Slug = $"skola-{id}", IsDemo = isDemo };

    /// <summary>The status a request with this session's support token ends with.</summary>
    private async Task<int> GuardAsync(int sessionId)
    {
        using var db = SuperAdminDb();
        var http = Http(1, new Claim(SupportClaims.SessionId, sessionId.ToString()));
        await new SupportSessionGuardMiddleware(_ => Task.CompletedTask).InvokeAsync(http, db, clock);
        return http.Response.StatusCode;
    }
}
