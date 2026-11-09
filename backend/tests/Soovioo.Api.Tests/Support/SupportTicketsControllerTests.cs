using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Support;

public class SupportTicketsControllerTests
{
    private readonly string dbName = Guid.NewGuid().ToString();

    // Monday 2026-11-09 11:00 Prague.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    private AppDbContext SuperAdminDb() => new(Options(), new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private AppDbContext SchoolDb(int schoolId) => new(Options(), new FakeCurrentSchoolContext(schoolId));

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private static HttpContext Http(int userId, params Claim[] extra) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extra])),
    };

    private static SupportPlanClient Plans(SupportPlanOptions? options = null, HttpMessageHandler? handler = null) =>
        new(new HttpClient(handler ?? new StubHandler(HttpStatusCode.NotFound, "")),
            Microsoft.Extensions.Options.Options.Create(options ?? new SupportPlanOptions()),
            NullLogger<SupportPlanClient>.Instance);

    private SupportTicketsController Tickets(AppDbContext db, int userId, int? schoolId = null, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(schoolId, isSuperAdmin: schoolId is null), new Notifier(db, clock),
            Plans(new SupportPlanOptions { Default = new SupportPlanDto("Paid", "Placený", 2) }),
            AuthTestServices.SupportSessions(db, clock),
            Microsoft.Extensions.Options.Options.Create(new SupportOptions { ApprovalMinutes = 2 }), clock)
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId, extra) },
        };

    private SupportPolicyController Policy(AppDbContext db, int userId, int schoolId, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(schoolId), Microsoft.Extensions.Options.Options.Create(new SupportOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = Http(userId, extra) },
        };

    private record World(School School, User SuperAdmin, User Other, User Admin);

    private async Task<World> SeedAsync(SupportAccess access = SupportAccess.Approval)
    {
        using var db = SuperAdminDb();
        var school = new School { Name = "ZŠ Test", Slug = "test", SupportAccess = access };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        var superAdmin = new User { Username = "superadmin", PasswordHash = "x", IsSuperAdmin = true };
        var other = new User { Username = "druhy", PasswordHash = "x", IsSuperAdmin = true };
        db.Users.AddRange(superAdmin, other);
        await db.SaveChangesAsync();
        var admin = new User
        {
            Username = "reditel", PasswordHash = "x", SchoolId = school.Id,
            ActiveRole = school.Roles.Single(r => r.BuiltIn == UserRole.Admin),
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return new World(school, superAdmin, other, admin);
    }

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<int> SchoolTicketAsync(World w, bool allowAccess = false)
    {
        using var db = SchoolDb(w.School.Id);
        return Ok(await Tickets(db, w.Admin.Id, w.School.Id)
            .CreateSchoolTicket(new CreateSchoolTicketRequest("Import žáků", "Z **Bakalářů**", allowAccess))).Ticket.Id;
    }

    [Fact]
    public async Task TicketGoesFromTheSchoolThroughItsAssigneeToResolvedWithTheThreadOnBothSides()
    {
        var w = await SeedAsync();
        using (var db = SchoolDb(w.School.Id))
        {
            Assert.IsNotType<OkObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id)
                .CreateSchoolTicket(new CreateSchoolTicketRequest(" ", "x"))).Result);
        }
        var id = await SchoolTicketAsync(w);

        using (var db = SuperAdminDb())
        {
            // Unassigned: every SuperAdmin hears of it.
            var notification = db.Notifications.Single(n => n.UserId == w.SuperAdmin.Id);
            Assert.Equal(NotificationType.SupportTicket, notification.Type);
            Assert.Equal($"superadmin/support?ticket={id}", NotificationLinks.For(notification));
            Assert.Single(db.Notifications.Where(n => n.UserId == w.Other.Id));

            var ticket = Assert.Single(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets(SupportTicketFilter.Unassigned)));
            Assert.Equal(SupportTicketStatus.New, ticket.Status);
            Assert.Equal("Placený", ticket.PlanName);
            Assert.Equal(new DateOnly(2026, 11, 11), ticket.DueDate);

            // Only the assignee changes the state; others still write to it.
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id)
                .SetTicketStatus(id, new SetSupportTicketStatusRequest(SupportTicketStatus.InProgress))).Result);
            var assigned = Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id)).Ticket;
            Assert.Equal(SupportTicketStatus.Assigned, assigned.Status);
            Assert.Equal(w.SuperAdmin.Id, assigned.AssigneeUserId);
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Other.Id).AssignTicket(id)).Result);
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Other.Id)
                .SetTicketStatus(id, new SetSupportTicketStatusRequest(SupportTicketStatus.Resolved))).Result);
            Ok(await Tickets(db, w.Other.Id).AddMessage(id, new SupportTicketMessageRequest("Zkus import znovu", Internal: true)));
            Assert.Contains(db.Notifications, n => n.UserId == w.SuperAdmin.Id && n.Title.StartsWith("Interní poznámka"));
            Assert.Empty(Ok(await Tickets(db, w.Other.Id).GetTickets(SupportTicketFilter.Mine)));
            Assert.Single(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets(SupportTicketFilter.Mine)));

            Ok(await Tickets(db, w.SuperAdmin.Id).AddMessage(id, new SupportTicketMessageRequest("Pošlete export?")));
            Ok(await Tickets(db, w.SuperAdmin.Id)
                .SetTicketStatus(id, new SetSupportTicketStatusRequest(SupportTicketStatus.WaitingForSchool)));
        }

        using (var db = SchoolDb(w.School.Id))
        {
            var detail = Ok(await Tickets(db, w.Admin.Id, w.School.Id).GetSchoolTicket(id));
            Assert.Equal("superadmin", detail.Ticket.AssigneeName);
            Assert.DoesNotContain(detail.Entries, e => e.Internal);
            Assert.Contains(detail.Entries, e => e.Kind == SupportTicketEntryKind.Message && e.Text == "Pošlete export?");
            var answered = Ok(await Tickets(db, w.Admin.Id, w.School.Id)
                .AddSchoolMessage(id, new SupportTicketMessageRequest("Posláno")));
            Assert.Equal(SupportTicketStatus.Assigned, answered.Ticket.Status);
        }

        using (var db = SuperAdminDb())
        {
            // The assignee only now — not everyone.
            Assert.Contains(db.Notifications, n => n.UserId == w.SuperAdmin.Id && n.Title.StartsWith("Odpověď školy"));
            Assert.DoesNotContain(db.Notifications, n => n.UserId == w.Other.Id && n.Title.StartsWith("Odpověď školy"));
            var detail = Ok(await Tickets(db, w.SuperAdmin.Id).GetTicket(id));
            Assert.Contains(detail.Entries, e => e.Kind == SupportTicketEntryKind.Note);

            clock.Now = clock.Now.AddDays(3);
            Assert.True(Assert.Single(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets())).Overdue);
            var resolved = Ok(await Tickets(db, w.SuperAdmin.Id)
                .SetTicketStatus(id, new SetSupportTicketStatusRequest(SupportTicketStatus.Resolved))).Ticket;
            Assert.False(resolved.Overdue);
            Assert.Empty(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets()));
            Assert.Single(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets(SupportTicketFilter.All)));
            Assert.Single(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets(SupportTicketFilter.All, w.School.Id)));
            Assert.Empty(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets(SupportTicketFilter.All, w.School.Id + 1)));
            // Closed: no more messages to the school, notes still.
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id)
                .AddMessage(id, new SupportTicketMessageRequest("Ještě"))).Result);
            Ok(await Tickets(db, w.Other.Id).AddMessage(id, new SupportTicketMessageRequest("Poznámka", Internal: true)));
        }

        using (var db = SchoolDb(w.School.Id))
        {
            var updates = db.Notifications
                .Where(n => n.UserId == w.Admin.Id && n.Type == NotificationType.SupportTicketUpdate).ToList();
            // An unread one about the same ticket is replaced by the newer.
            Assert.Equal("Požadavek vyřešen: Import žáků", Assert.Single(updates).Title);
            Assert.Equal($"admin?group=app&section=support&ticket={id}", NotificationLinks.For(updates[0]));
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id).CancelTicket(id)).Result);
        }
    }

    [Fact]
    public async Task ReleasingNeedsASummaryAndPutsTheTicketBackForTheOthers()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w);
        using var db = SuperAdminDb();
        Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id));
        Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Other.Id)
            .ReleaseTicket(id, new ReleaseSupportTicketRequest("x"))).Result);
        Assert.IsNotType<OkObjectResult>((await Tickets(db, w.SuperAdmin.Id)
            .ReleaseTicket(id, new ReleaseSupportTicketRequest(" "))).Result);

        var released = Ok(await Tickets(db, w.SuperAdmin.Id)
            .ReleaseTicket(id, new ReleaseSupportTicketRequest("Import prověřen, chyba v exportu")));
        Assert.Null(released.Ticket.AssigneeUserId);
        Assert.Equal(SupportTicketStatus.New, released.Ticket.Status);
        var summary = released.Entries.Single(e => e.Kind == SupportTicketEntryKind.Released);
        Assert.True(summary.Internal);
        Assert.Contains(db.Notifications, n => n.UserId == w.Other.Id && n.Title.StartsWith("Požadavek vrácen"));
        Assert.DoesNotContain(db.Notifications, n => n.UserId == w.SuperAdmin.Id && n.Title.StartsWith("Požadavek vrácen"));
        Assert.Equal(w.Other.Id, Ok(await Tickets(db, w.Other.Id).AssignTicket(id)).Ticket.AssigneeUserId);
    }

    [Fact]
    public async Task SuperAdminOpensATicketForASchoolAndTakesCareOfIt()
    {
        var w = await SeedAsync();
        using (var db = SuperAdminDb())
        {
            Assert.IsNotType<OkObjectResult>((await Tickets(db, w.SuperAdmin.Id)
                .CreateTicket(new CreateSupportTicketRequest(w.School.Id + 1, "Kontrola", ""))).Result);
            var detail = Ok(await Tickets(db, w.SuperAdmin.Id)
                .CreateTicket(new CreateSupportTicketRequest(w.School.Id, "Kontrola rozvrhu", "Chybí **úvazky**")));
            Assert.True(detail.Ticket.CreatedBySupport);
            Assert.Equal(w.SuperAdmin.Id, detail.Ticket.AssigneeUserId);
            Assert.Equal(SupportTicketStatus.Assigned, detail.Ticket.Status);
        }
        using (var schoolDb = SchoolDb(w.School.Id))
        {
            var ticket = Assert.Single(Ok(await Tickets(schoolDb, w.Admin.Id, w.School.Id).GetSchoolTickets()));
            Assert.Equal("Kontrola rozvrhu", ticket.Title);
            Assert.Contains(schoolDb.Notifications, n => n.UserId == w.Admin.Id && n.Title.StartsWith("Podpora založila"));
        }
    }

    [Fact]
    public async Task WithApprovalOnlyTheAssigneeEntersWhileTheTicketAllowsItAndRevokingEndsTheVisit()
    {
        var w = await SeedAsync();
        var id = await SchoolTicketAsync(w, allowAccess: true);
        int sessionId;
        using (var db = SuperAdminDb())
        {
            var ticket = Ok(await Tickets(db, w.SuperAdmin.Id).GetTicket(id)).Ticket;
            Assert.Equal(clock.Now.AddMinutes(2), ticket.AccessUntil);
            // Allowed, but only for whoever takes care of it.
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id).Enter(id)).Result);
            Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id));
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Other.Id).Enter(id)).Result);
            var started = Ok(await Tickets(db, w.SuperAdmin.Id).Enter(id));
            Assert.Equal("test", started.SchoolSlug);
            sessionId = started.SessionId;
            Assert.Equal(id, db.SupportSessions.Single().TicketId);
            Assert.Equal(SupportTicketStatus.InProgress, db.SupportTickets.Single().Status);
            // Already allowed — nothing to ask for.
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id).RequestAccess(id)).Result);
        }
        Assert.Equal(StatusCodes.Status200OK, await GuardAsync(sessionId));

        using (var db = SchoolDb(w.School.Id))
        {
            var refused = await Tickets(db, w.Admin.Id, w.School.Id, new Claim(SupportClaims.SessionId, "1")).RevokeAccess(id);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(refused.Result).StatusCode);
            var revoked = Ok(await Tickets(db, w.Admin.Id, w.School.Id).RevokeAccess(id));
            Assert.Null(revoked.Ticket.AccessUntil);
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id).RevokeAccess(id)).Result);
        }
        Assert.Equal(StatusCodes.Status401Unauthorized, await GuardAsync(sessionId));

        using (var db = SuperAdminDb())
        {
            Assert.Contains(db.Notifications, n => n.UserId == w.SuperAdmin.Id && n.Title.StartsWith("Škola odebrala"));
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id).Enter(id)).Result);
            var asked = Ok(await Tickets(db, w.SuperAdmin.Id).RequestAccess(id));
            Assert.Equal(clock.Now, asked.Ticket.AccessRequestedAt);
        }
        using (var db = SchoolDb(w.School.Id))
        {
            Assert.Contains(db.Notifications, n => n.UserId == w.Admin.Id && n.Title.StartsWith("Podpora žádá o vstup"));
            var granted = Ok(await Tickets(db, w.Admin.Id, w.School.Id).GrantAccess(id));
            Assert.Null(granted.Ticket.AccessRequestedAt);
            Assert.Equal(clock.Now.AddMinutes(2), granted.Ticket.AccessUntil);
        }
        using (var db = SuperAdminDb())
        {
            Ok(await Tickets(db, w.SuperAdmin.Id).Enter(id));
            clock.Now = clock.Now.AddMinutes(3);
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id).Enter(id)).Result);
            Assert.Null(Ok(await Tickets(db, w.SuperAdmin.Id).GetTicket(id)).Ticket.AccessUntil);
        }
    }

    [Theory]
    [InlineData(SupportAccess.Always, true)]
    [InlineData(SupportAccess.Never, false)]
    public async Task AlwaysNeedsNoAllowingNeverLetsNobodyIn(SupportAccess access, bool entersRightAway)
    {
        var w = await SeedAsync(access);
        var id = await SchoolTicketAsync(w, allowAccess: true);
        using (var db = SuperAdminDb())
        {
            Assert.Null(Ok(await Tickets(db, w.SuperAdmin.Id).GetTicket(id)).Ticket.AccessUntil);
            Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id));
            Assert.Equal(entersRightAway, (await Tickets(db, w.SuperAdmin.Id).Enter(id)).Result is OkObjectResult);
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.SuperAdmin.Id).RequestAccess(id)).Result);
        }
        using (var db = SchoolDb(w.School.Id))
        {
            Assert.IsType<BadRequestObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id).GrantAccess(id)).Result);
        }
    }

    [Fact]
    public async Task WhatWasDoneOnLeavingGoesToTheThreadAndTheLog()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        int sessionId;
        using (var db = SuperAdminDb())
        {
            Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id));
            sessionId = Ok(await Tickets(db, w.SuperAdmin.Id).Enter(id)).SessionId;
        }
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
                new EndSupportSessionRequest(new string('x', SupportTicket.MaxTextLength + 1))));
            Assert.IsType<NoContentResult>(await Leaving().EndCurrentSession(
                new EndSupportSessionRequest("  Import **opraven** ")));

            var visit = Ok(await Tickets(db, w.Admin.Id, w.School.Id).GetSchoolTicket(id)).Entries
                .Single(e => e.Kind == SupportTicketEntryKind.Visit);
            Assert.Equal("Import **opraven**", visit.Text);
            Assert.True(visit.BySupport);
        }
        using (var db = SuperAdminDb())
        {
            var support = new SupportController(
                db, AuthTestServices.SupportSessions(db, clock), AuthTestServices.RefreshTokens(db, clock), clock)
            {
                ControllerContext = new ControllerContext { HttpContext = Http(w.SuperAdmin.Id) },
            };
            var session = Assert.Single(Assert.IsAssignableFrom<IEnumerable<SupportSessionDto>>(
                Assert.IsType<OkObjectResult>((await support.GetSupportSessions()).Result).Value));
            Assert.Equal("Import **opraven**", session.Result);
            Assert.Equal("Import žáků", session.Purpose);
        }
    }

    [Fact]
    public async Task CancellingEndsTheVisitsAndInSupportModeTheSchoolManagesNothing()
    {
        var w = await SeedAsync(SupportAccess.Always);
        var id = await SchoolTicketAsync(w);
        int sessionId;
        using (var db = SuperAdminDb())
        {
            Ok(await Tickets(db, w.SuperAdmin.Id).AssignTicket(id));
            sessionId = Ok(await Tickets(db, w.SuperAdmin.Id).Enter(id)).SessionId;
        }
        using (var db = SchoolDb(w.School.Id))
        {
            var supportMode = new Claim(SupportClaims.SessionId, sessionId.ToString());
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id, supportMode)
                .CreateSchoolTicket(new CreateSchoolTicketRequest("A", ""))).Result).StatusCode);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await Tickets(db, w.Admin.Id, w.School.Id, supportMode)
                .AddSchoolMessage(id, new SupportTicketMessageRequest("A"))).Result).StatusCode);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await Policy(db, w.Admin.Id, w.School.Id, supportMode)
                .SetAccess(new SetSupportAccessRequest(SupportAccess.Always))).Result).StatusCode);
            Assert.Equal(SupportAccess.Never, Ok(await Policy(db, w.Admin.Id, w.School.Id)
                .SetAccess(new SetSupportAccessRequest(SupportAccess.Never))).Access);

            var cancelled = Ok(await Tickets(db, w.Admin.Id, w.School.Id).CancelTicket(id));
            Assert.Equal(SupportTicketStatus.Cancelled, cancelled.Ticket.Status);
        }
        Assert.Equal(StatusCodes.Status401Unauthorized, await GuardAsync(sessionId));
        using (var db = SuperAdminDb())
        {
            Assert.Contains(db.Notifications, n => n.UserId == w.SuperAdmin.Id && n.Title.StartsWith("Škola zrušila"));
            Assert.Empty(Ok(await Tickets(db, w.SuperAdmin.Id).GetTickets()));
        }
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
    public async Task PlanComesFromTheServiceWithTheDefaultForUnknownSchoolsAndFailures()
    {
        var options = new SupportPlanOptions { BaseUrl = "https://plans.test/api", ApiKey = "k" };
        var ok = new StubHandler(HttpStatusCode.OK, """{"code":"Priority","name":"Prioritní","responseWorkingDays":1}""");
        var plan = await Plans(options, ok).GetPlanAsync(7);
        Assert.Equal(new SupportPlanDto("Priority", "Prioritní", 1), plan);
        Assert.Equal("https://plans.test/api/schools/7/plan", ok.Last!.RequestUri!.ToString());
        Assert.Equal("k", ok.Last.Headers.GetValues("X-Api-Key").Single());

        Assert.Equal(options.Default, await Plans(options, new StubHandler(HttpStatusCode.NotFound, "")).GetPlanAsync(7));
        Assert.Equal(options.Default, await Plans(options, new StubHandler(HttpStatusCode.InternalServerError, "")).GetPlanAsync(7));
        Assert.Equal(options.Default, await Plans(new SupportPlanOptions()).GetPlanAsync(7));
    }

    /// <summary>The status a request with this session's support token ends with.</summary>
    private async Task<int> GuardAsync(int sessionId)
    {
        using var db = SuperAdminDb();
        var http = Http(1, new Claim(SupportClaims.SessionId, sessionId.ToString()));
        await new SupportSessionGuardMiddleware(_ => Task.CompletedTask).InvokeAsync(http, db, clock);
        return http.Response.StatusCode;
    }

    private class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
