using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Support;

public class SupportResetTests
{
    private readonly string dbName = Guid.NewGuid().ToString();

    // Monday 2026-11-09 11:00 Prague.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

    private AppDbContext SuperAdminDb() => new(Options(), new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private AppDbContext SchoolDb() => new(Options(), new FakeCurrentSchoolContext(w.School.Id));

    /// <summary>The school's data as the support changes it in <paramref name="sessionId"/>.</summary>
    private AppDbContext SupportDb(int sessionId)
    {
        var db = SchoolDb();
        db.SupportJournal = new SupportJournalScope(sessionId, w.TicketId, w.School.Id, clock);
        return db;
    }

    private readonly FakeBackofficeTickets backoffice = new();

    private SupportTicketsController Tickets(AppDbContext db, int userId, int schoolId, params Claim[] extra) =>
        new(db, new FakeCurrentSchoolContext(schoolId), backoffice,
            new SupportPlanClient(Soovioo.Api.Tests.Backoffice.FakeBackoffice.NotConnected(),
                Microsoft.Extensions.Options.Options.Create(new SupportPlanOptions()),
                new MemoryCache(new MemoryCacheOptions()), NullLogger<SupportPlanClient>.Instance),
            new SupportReset(db, clock), new Soovioo.Api.Tests.Live.RecordingHub().Publisher(), clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extra])),
                },
            },
        };

    private SupportTicketsController School(AppDbContext db, User? user = null) =>
        Tickets(db, (user ?? w.Admin).Id, w.School.Id);

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private record World(School School, User SuperAdmin, User Other, User Admin, User Admin2, int TicketId, int SessionId);

    private World w = null!;

    private async Task SeedAsync()
    {
        using var db = SuperAdminDb();
        var school = new School { Name = "ZŠ Test", Slug = "test" };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        var superAdmin = new User { Username = "superadmin", PasswordHash = "x", IsSuperAdmin = true };
        var other = new User { Username = "druhy", PasswordHash = "x", IsSuperAdmin = true };
        db.Users.AddRange(superAdmin, other);
        await db.SaveChangesAsync();
        var adminRole = school.Roles.Single(r => r.BuiltIn == UserRole.Admin);
        var admin = new User { Username = "reditel", PasswordHash = "x", SchoolId = school.Id, ActiveRole = adminRole, Roles = [adminRole] };
        var admin2 = new User { Username = "zastupce", PasswordHash = "x", SchoolId = school.Id, ActiveRole = adminRole, Roles = [adminRole] };
        db.Users.AddRange(admin, admin2);
        await db.SaveChangesAsync();
        var ticketId = backoffice.Seed(school.Id, new TicketActor(admin.Id, "reditel"), SupportTicketStatus.InProgress);
        w = new World(school, superAdmin, other, admin, admin2, ticketId, 0);
        w = w with { SessionId = await SessionAsync() };
    }

    private async Task<int> SessionAsync()
    {
        using var db = SuperAdminDb();
        var session = new SupportSession
        {
            SuperAdminUserId = w.SuperAdmin.Id, SuperAdminUsername = "superadmin", SchoolId = w.School.Id,
            TicketId = w.TicketId, StartedAt = clock.Now, ExpiresAt = clock.Now.AddHours(2),
        };
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<(int Classroom, int SchoolClass, int Event)> SchoolDataAsync()
    {
        using var db = SchoolDb();
        var classroom = new Classroom { Name = "U1", SchoolId = w.School.Id };
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = w.School.Id };
        var trip = new Event
        {
            Title = "Výlet", SchoolId = w.School.Id, CreatedByUserId = w.Admin.Id,
            StartAt = clock.Now, EndAt = clock.Now.AddHours(5),
        };
        trip.SchoolClasses.Add(new EventSchoolClass { SchoolClass = schoolClass, SchoolId = w.School.Id });
        db.AddRange(classroom, schoolClass, trip);
        await db.SaveChangesAsync();
        return (classroom.Id, schoolClass.Id, trip.Id);
    }

    [Fact]
    public async Task WhatTheSupportChangedIsJournaledAndTheResetReturnsIt()
    {
        await SeedAsync();
        var (classroomId, classId, tripId) = await SchoolDataAsync();

        using (var db = SupportDb(w.SessionId))
        {
            db.Classrooms.Add(new Classroom { Name = "U2", SchoolId = w.School.Id });
            (await db.SchoolClasses.SingleAsync(c => c.Id == classId)).Name = "5.B";
            // Its class goes with it, though not loaded.
            db.Events.Remove(await db.Events.SingleAsync(e => e.Id == tripId));
            var teacher = await db.Roles.SingleAsync(r => r.SchoolId == w.School.Id && r.BuiltIn == UserRole.Teacher);
            (await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == w.Admin2.Id)).Roles.Add(teacher);
            await db.SaveChangesAsync();
        }
        using (var db = SchoolDb())
        {
            // Outside support mode: not journaled.
            db.Classrooms.Add(new Classroom { Name = "U3", SchoolId = w.School.Id });
            await db.SaveChangesAsync();

            var journal = db.SupportChanges.OrderBy(c => c.Id).ToList();
            Assert.Equal(
                ["Added Classroom", "Added UserRoles", "Deleted Event", "Deleted EventSchoolClass", "Modified SchoolClass"],
                journal.Select(c => $"{c.Operation} {c.EntityType.Split('.')[^1]}").Order().ToList());
            Assert.All(journal, c => Assert.Equal(w.SessionId, c.SessionId));
            Assert.Equal(5, Ok(await School(db).GetSchoolTicket(w.TicketId)).Ticket.ResettableChanges);

            var preview = Ok(await School(db).PreviewSchoolReset(w.TicketId)).Items;
            Assert.Equal(5, preview.Count);
            Assert.All(preview, i => Assert.Null(i.Skipped));
            Assert.Contains(preview, i => i is { Type: "Učebna", Record: "U2", Action: SupportResetAction.Delete });
            Assert.Contains(preview, i => i is { Type: "Třída", Record: "5.A", Action: SupportResetAction.Revert });
            Assert.Contains(preview, i => i is { Type: "Akce", Record: "Výlet", Action: SupportResetAction.Restore });
            Assert.Contains(preview, i => i is { Type: "Role účtu", Action: SupportResetAction.Delete });
            Assert.Empty(db.ChangeTracker.Entries());
        }
        using (var db = SchoolDb())
        {
            var detail = Ok(await School(db).ResetSchoolTicket(w.TicketId, new ResetSupportChangesRequest(Reason: "Chyba")));
            Assert.Equal(0, detail.Ticket.ResettableChanges);
            var entry = Assert.Single(detail.Entries, e => e.Kind == nameof(SupportTicketEntryKind.Reset));
            Assert.Equal("**Důvod:** Chyba\n\nVráceno záznamů: 5.", entry.Text);
        }
        using (var db = SchoolDb())
        {
            Assert.Equal(["U1", "U3"], db.Classrooms.Select(c => c.Name).OrderBy(n => n).ToList());
            Assert.Equal("5.A", db.SchoolClasses.Single(c => c.Id == classId).Name);
            var trip = db.Events.Include(e => e.SchoolClasses).Single(e => e.Id == tripId);
            Assert.Equal("Výlet", trip.Title);
            Assert.Equal(classId, Assert.Single(trip.SchoolClasses).SchoolClassId);
            Assert.Single(db.Users.Where(u => u.Id == w.Admin2.Id).SelectMany(u => u.Roles));
            Assert.All(db.SupportChanges, c => Assert.Equal(clock.Now, c.ResetAt));
            Assert.Equal(classroomId, db.Classrooms.Single(c => c.Name == "U1").Id);
        }
    }

    [Fact]
    public async Task WhatChangedLaterOrHasTheSchoolsDataOnItStays()
    {
        await SeedAsync();
        var (classroomId, _, tripId) = await SchoolDataAsync();
        int newClassId, newEventId;
        using (var db = SupportDb(w.SessionId))
        {
            var schoolClass = new SchoolClass { Name = "6.A", SchoolId = w.School.Id };
            var meeting = new Event
            {
                Title = "Schůzka", SchoolId = w.School.Id, CreatedByUserId = w.Admin.Id,
                StartAt = clock.Now, EndAt = clock.Now.AddHours(1),
            };
            db.AddRange(schoolClass, meeting);
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1b";
            await db.SaveChangesAsync();
            (newClassId, newEventId) = (schoolClass.Id, meeting.Id);
        }
        using (var db = SchoolDb())
        {
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1c";
            db.EventSchoolClasses.Add(new EventSchoolClass { EventId = tripId, SchoolClassId = newClassId, SchoolId = w.School.Id });
            await db.SaveChangesAsync();

            var preview = Ok(await School(db).PreviewSchoolReset(w.TicketId)).Items;
            Assert.Equal(SupportReset.ChangedLater, preview.Single(i => i.Type == "Učebna").Skipped);
            Assert.Equal($"{SupportReset.HasDependents} (Třída na akci).", preview.Single(i => i.Type == "Třída").Skipped);
            Assert.Null(preview.Single(i => i.Type == "Akce").Skipped);

            var entry = Ok(await School(db).ResetSchoolTicket(w.TicketId, new ResetSupportChangesRequest(Reason: "Chyba")))
                .Entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.Reset));
            Assert.StartsWith("**Důvod:** Chyba\n\nVráceno záznamů: 1.\n\nNevráceno (2):\n- ", entry.Text);
        }
        using (var db = SchoolDb())
        {
            Assert.Equal("U1c", db.Classrooms.Single(c => c.Id == classroomId).Name);
            Assert.True(db.SchoolClasses.Any(c => c.Id == newClassId));
            Assert.False(db.Events.Any(e => e.Id == newEventId));
            // What stayed may be reset again later.
            Assert.Equal(2, db.SupportChanges.Count(c => c.ResetAt == null));
        }
    }

    [Fact]
    public async Task OneSessionIsResetOnItsOwn()
    {
        await SeedAsync();
        var (classroomId, classId, _) = await SchoolDataAsync();
        var second = await SessionAsync();
        using (var db = SupportDb(w.SessionId))
        {
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1b";
            await db.SaveChangesAsync();
        }
        using (var db = SupportDb(second))
        {
            (await db.SchoolClasses.SingleAsync(c => c.Id == classId)).Name = "5.B";
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1c";
            await db.SaveChangesAsync();
        }
        using (var db = SchoolDb())
        {
            var detail = Ok(await School(db).GetSchoolTicket(w.TicketId));
            Assert.Equal(3, detail.Ticket.ResettableChanges);
            Assert.Equal(1, detail.Entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.SessionStarted) && e.SessionId == w.SessionId).Changes);

            // The first session's change was changed again by the second.
            var first = Ok(await School(db).PreviewSchoolReset(w.TicketId, w.SessionId)).Items;
            Assert.Equal(SupportReset.ChangedLaterBySupport, Assert.Single(first).Skipped);

            var after = Ok(await School(db).ResetSchoolTicket(w.TicketId, new ResetSupportChangesRequest(second, "Chyba")));
            Assert.Equal(second, after.Entries.Single(e => e.Kind == nameof(SupportTicketEntryKind.Reset)).SessionId);
        }
        using (var db = SchoolDb())
        {
            Assert.Equal("5.A", db.SchoolClasses.Single(c => c.Id == classId).Name);
            Assert.Equal("U1b", db.Classrooms.Single(c => c.Id == classroomId).Name);
        }
        using (var db = SchoolDb())
        {
            Ok(await School(db).ResetSchoolTicket(w.TicketId, new ResetSupportChangesRequest(Reason: "Celé")));
        }
        using (var db = SchoolDb())
        {
            Assert.Equal("U1", db.Classrooms.Single(c => c.Id == classroomId).Name);
        }
    }

    [Fact]
    public async Task OnlyTheAuthorResetsAnOpenTicketOutsideSupportMode()
    {
        await SeedAsync();
        var (classroomId, _, _) = await SchoolDataAsync();
        using (var db = SupportDb(w.SessionId))
        {
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1b";
            await db.SaveChangesAsync();
        }
        using var school = SchoolDb();
        Assert.IsType<BadRequestObjectResult>((await School(school, w.Admin2).PreviewSchoolReset(w.TicketId)).Result);
        Assert.Equal(0, Ok(await School(school, w.Admin2).GetSchoolTicket(w.TicketId)).Ticket.ResettableChanges);
        Assert.IsType<ObjectResult>((await Tickets(school, w.Admin.Id, w.School.Id,
            new Claim(SupportClaims.SessionId, w.SessionId.ToString())).ResetSchoolTicket(w.TicketId, new(Reason: "Chyba"))).Result);
        Assert.IsType<NotFoundResult>((await School(school).PreviewSchoolReset(w.TicketId, w.SessionId + 100)).Result);

        backoffice.Support(w.TicketId, t => t with { Status = SupportTicketStatus.Closed });
        Assert.IsType<BadRequestObjectResult>((await School(school).PreviewSchoolReset(w.TicketId)).Result);
        using var later = SchoolDb();
        Assert.IsType<BadRequestObjectResult>((await School(later).ResetSchoolTicket(w.TicketId, new(Reason: "Chyba"))).Result);
    }

    [Fact]
    public async Task CancellingResetsFirstAndTheJournalGoesWithTheFilesAfterTheNextWorkingDay()
    {
        await SeedAsync();
        var (classroomId, _, _) = await SchoolDataAsync();
        var env = new FakeWebHostEnvironment();
        var kept = Path.Combine(w.School.Id.ToString(), "kept.pdf");
        var gone = Path.Combine(w.School.Id.ToString(), "gone.pdf");
        Directory.CreateDirectory(Path.Combine(env.ContentRootPath, "AttachmentStorage", w.School.Id.ToString()));
        foreach (var file in new[] { kept, gone })
        {
            await File.WriteAllTextAsync(Path.Combine(env.ContentRootPath, "AttachmentStorage", file), "x");
        }
        using (var db = SupportDb(w.SessionId))
        {
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1b";
            await db.SaveChangesAsync();
            var storage = new MessageAttachmentStorageService(env, db);
            storage.Delete(kept);
            storage.Delete(gone);
            Assert.True(File.Exists(storage.GetFullPath(gone)));
        }
        using (var db = SchoolDb())
        {
            // A row holding the file — it stays.
            var conversation = new Conversation { Subject = "Dotaz", SchoolId = w.School.Id };
            var message = new Message { Conversation = conversation, SchoolId = w.School.Id, SenderName = "x", Body = "y" };
            message.Attachments.Add(new MessageAttachment
            {
                SchoolId = w.School.Id, FileName = "kept.pdf", ContentType = "application/pdf", StoragePath = kept,
            });
            db.Messages.Add(message);
            await db.SaveChangesAsync();

            var cancelled = Ok(await School(db).CancelTicket(w.TicketId, new CancelSupportTicketRequest("Už ne", ResetChanges: true)));
            Assert.Equal(SupportTicketStatus.Cancelled, cancelled.Ticket.Status);
            Assert.Single(cancelled.Entries, e => e.Kind == nameof(SupportTicketEntryKind.Reset));
            Assert.Equal("U1", db.Classrooms.Single(c => c.Id == classroomId).Name);
        }

        // Cancelled Monday: reopenable till Tuesday's end (Prague), as the backoffice says.
        backoffice.Support(w.TicketId, t => t with { ReopenableUntil = new DateTimeOffset(2026, 11, 10, 23, 0, 0, TimeSpan.Zero) });
        using (var db = SuperAdminDb())
        {
            var job = new SupportChangeCleanupJob(db, backoffice, new MessageAttachmentStorageService(env), clock);
            clock.Now = new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.Zero);
            await job.RunAsync();
            Assert.NotEmpty(db.SupportChanges);
            clock.Now = new DateTimeOffset(2026, 11, 10, 23, 0, 0, TimeSpan.Zero);
            await job.RunAsync();
            Assert.Empty(db.SupportChanges);
        }
        var root = Path.Combine(env.ContentRootPath, "AttachmentStorage");
        Assert.True(File.Exists(Path.Combine(root, kept)));
        Assert.False(File.Exists(Path.Combine(root, gone)));
    }

    [Fact]
    public async Task TheSchoolSaysWhyAndTheRunningVisitEndsWithIt()
    {
        await SeedAsync();
        var (classroomId, _, _) = await SchoolDataAsync();
        using (var db = SupportDb(w.SessionId))
        {
            (await db.Classrooms.SingleAsync(c => c.Id == classroomId)).Name = "U1b";
            await db.SaveChangesAsync();
        }
        using (var db = SchoolDb())
        {
            Assert.IsType<BadRequestObjectResult>(
                (await School(db).ResetSchoolTicket(w.TicketId, new ResetSupportChangesRequest(Reason: " "))).Result);
            var detail = Ok(await School(db).ResetSchoolTicket(
                w.TicketId, new ResetSupportChangesRequest(w.SessionId, "Rozbilo to rozvrh")));
            Assert.Contains(detail.Entries, e => e.Kind == nameof(SupportTicketEntryKind.SessionEnded) && e.SessionId == w.SessionId);
        }
        using (var db = SuperAdminDb())
        {
            var session = db.SupportSessions.Single(s => s.Id == w.SessionId);
            Assert.Equal(clock.Now, session.EndedAt);
            Assert.Equal("reditel", session.EndedBy);
            Assert.Equal("Změny podpory byly resetovány.\n\n**Důvod:** Rozbilo to rozvrh", session.EndReason);

            // The tab inside learns it with its next request.
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SupportClaims.SessionId, w.SessionId.ToString())])),
            };
            http.Response.Body = new MemoryStream();
            await new SupportSessionGuardMiddleware(_ => Task.CompletedTask).InvokeAsync(http, db, clock);
            Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
            var body = System.Text.Json.JsonDocument.Parse(((MemoryStream)http.Response.Body).ToArray()).RootElement
                .GetProperty("supportEnded");
            Assert.Equal("reditel", body.GetProperty("by").GetString());
            Assert.Contains("Rozbilo to rozvrh", body.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task AMessageSomeoneReadStays()
    {
        await SeedAsync();
        int conversationId;
        using (var db = SchoolDb())
        {
            var conversation = new Conversation { Subject = "Dotaz", SchoolId = w.School.Id };
            conversation.Participants.Add(new ConversationParticipant { UserId = w.Admin.Id, SchoolId = w.School.Id });
            conversation.Participants.Add(new ConversationParticipant { UserId = w.Admin2.Id, SchoolId = w.School.Id });
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            conversationId = conversation.Id;
        }
        using (var db = SupportDb(w.SessionId))
        {
            db.Messages.Add(new Message
            {
                ConversationId = conversationId, SchoolId = w.School.Id, SenderUserId = w.Admin.Id,
                SenderName = "reditel", Body = "Ahoj", SentAt = clock.Now,
            });
            await db.SaveChangesAsync();
        }
        using (var db = SchoolDb())
        {
            db.ConversationParticipants.Single(p => p.UserId == w.Admin2.Id).LastReadAt = clock.Now.AddMinutes(1);
            await db.SaveChangesAsync();
            var item = Assert.Single(Ok(await School(db).PreviewSchoolReset(w.TicketId)).Items);
            Assert.Equal(SupportReset.MessageRead, item.Skipped);
        }
    }

    [Fact]
    public void DatabaseWritesBypassingTheJournalAreOnlyWhereItsAllowed()
    {
        var src = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(src, "src", "Soovioo.Api")))
        {
            src = Path.GetDirectoryName(src)!;
        }
        var root = Path.Combine(src, "src", "Soovioo.Api");
        var found = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Regex.IsMatch(File.ReadAllText(f), @"\.Execute(Delete|Update|SqlRaw|SqlInterpolated|Sql)(Async)?\("))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Order()
            .ToList();
        // BulkDelete journals in support mode; the others write rows the
        // journal leaves out (SupportJournal.NotJournaled).
        Assert.Equal(
            [
                "Auth/RefreshTokenService.cs", "Data/BulkDelete.cs", "Timetable/TimetableGenerationController.cs",
                "Timetable/TimetableGenerationJob.cs",
            ],
            found);
    }
}
