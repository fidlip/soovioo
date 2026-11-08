using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Messaging;

public class ConversationsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static ConversationsController As(AppDbContext db, User user)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.ActiveRole?.Kind.ToString() ?? nameof(UserRole.SuperAdmin)),
            new Claim(PermissionAccess.RoleIdClaim, user.ActiveRole?.Id.ToString() ?? ""),
            new Claim(ClaimTypes.Name, user.Username),
        ]);
        return new ConversationsController(
            db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()),
            new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    /// <summary>5.A with class teacher "ucitel", a maths teacher "matikar"
    /// (timetable), admin "reditel", pupil Jan with own account "zak" and
    /// parent "rodic", plus an unrelated parent "cizi".</summary>
    private sealed class World
    {
        public required User Teacher, MathTeacher, Admin, Parent, StudentUser, Stranger;
        public required StudentProfile Student;
    }

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        User NewUser(string name, UserRole role) =>
            new() { Username = name, PasswordHash = "x", ActiveRole = db.Role(role, SchoolId), SchoolId = SchoolId };
        var w = new World
        {
            Teacher = NewUser("ucitel", UserRole.Teacher),
            MathTeacher = NewUser("matikar", UserRole.Teacher),
            Admin = NewUser("reditel", UserRole.Admin),
            Parent = NewUser("rodic", UserRole.Parent),
            StudentUser = NewUser("zak", UserRole.Student),
            Stranger = NewUser("cizi", UserRole.Parent),
            Student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolId = SchoolId },
        };
        db.Users.AddRange(w.Teacher, w.MathTeacher, w.Admin, w.Parent, w.StudentUser, w.Stranger);
        await db.SaveChangesAsync();
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId, ClassTeacherId = w.Teacher.Id };
        var math = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(math);
        await db.SaveChangesAsync();
        w.Student.SchoolClassId = schoolClass.Id;
        w.Student.Person!.UserId = w.StudentUser.Id;
        db.Students.Add(w.Student);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(w.Parent.Id, w.Student.Id, SchoolId);
        db.TimetableEntries.Add(new TimetableEntry
        {
            TimetableVersion = db.CurrentTimetableVersion(SchoolId),
            SchoolClassId = schoolClass.Id, SubjectId = math.Id, TeacherId = w.MathTeacher.Id,
            DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();
        return w;
    }

    [Fact]
    public async Task TeacherStartsConversationWithPupilsFamily()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var result = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Ve středu jedeme.", w.Student.Id));

        var dto = Assert.IsType<ConversationDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(
            new[] { w.Teacher.Id, w.Parent.Id, w.StudentUser.Id }.Order(),
            dto.Participants.Select(p => p.UserId).Order());
        Assert.Single(dto.Messages);
    }

    [Fact]
    public async Task ParentCanReplyAndTeacherThenSeesItUnread()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var started = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Ve středu jedeme.", w.Student.Id));
        var id = ((ConversationDto)((CreatedAtActionResult)started.Result!).Value!).Id;

        Assert.Equal(1, await db.CountUnreadConversationsAsync(w.Parent.Id));
        Assert.Equal(0, await db.CountUnreadConversationsAsync(w.Teacher.Id));

        var reply = await As(db, w.Parent).Reply(id, new ReplyRequest("Díky, bude mít svačinu."));

        Assert.IsType<OkObjectResult>(reply.Result);
        Assert.Equal(0, await db.CountUnreadConversationsAsync(w.Parent.Id));
        Assert.Equal(1, await db.CountUnreadConversationsAsync(w.Teacher.Id));

        await As(db, w.Teacher).MarkRead(id);
        Assert.Equal(0, await db.CountUnreadConversationsAsync(w.Teacher.Id));
    }

    [Fact]
    public async Task NonParticipantCannotReadOrReply()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var started = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Text", w.Student.Id));
        var id = ((ConversationDto)((CreatedAtActionResult)started.Result!).Value!).Id;

        Assert.IsType<NotFoundResult>((await As(db, w.Stranger).GetConversation(id)).Result);
        Assert.IsType<NotFoundResult>((await As(db, w.Stranger).Reply(id, new ReplyRequest("x"))).Result);
        // Admin isn't a participant either — no reading others' mail.
        Assert.IsType<NotFoundResult>((await As(db, w.Admin).GetConversation(id)).Result);
    }

    [Fact]
    public async Task ContactsAreClassTeacherSubjectTeachersAndLeadership()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var result = await As(db, w.Parent).GetContacts(w.Student.Id);

        var contacts = Assert.IsAssignableFrom<IEnumerable<ContactDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(["ucitel", "matikar", "reditel"], contacts.Select(c => c.Name));
        Assert.Equal("třídní učitel", contacts[0].Note);
        Assert.Equal("Matematika", contacts[1].Note);
        Assert.Equal("vedení školy", contacts[2].Note);
    }

    [Fact]
    public async Task ParentStartsConversationOnlyWithOfferedContacts()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var ok = await As(db, w.Parent).StartConversation(
            new StartConversationRequest("Omluva", "Jan bude chybět.", w.Student.Id, [w.Teacher.Id]));
        var bad = await As(db, w.Parent).StartConversation(
            new StartConversationRequest("Spam", "…", w.Student.Id, [w.Stranger.Id]));

        var dto = Assert.IsType<ConversationDto>(Assert.IsType<CreatedAtActionResult>(ok.Result).Value);
        Assert.Equal(new[] { w.Teacher.Id, w.Parent.Id }.Order(), dto.Participants.Select(p => p.UserId).Order());
        Assert.IsType<BadRequestObjectResult>(bad.Result);
    }

    [Fact]
    public async Task ParentCannotWriteAboutSomeoneElsesChild()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var result = await As(db, w.Stranger).StartConversation(
            new StartConversationRequest("x", "y", w.Student.Id, [w.Teacher.Id]));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task OnlySenderDeletesMessageAndEmptyConversationGoes()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var started = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Text", w.Student.Id));
        var dto = (ConversationDto)((CreatedAtActionResult)started.Result!).Value!;
        var messageId = dto.Messages[0].Id;

        Assert.IsType<NotFoundResult>(await As(db, w.Parent).DeleteMessage(dto.Id, messageId));
        Assert.IsType<NoContentResult>(await As(db, w.Teacher).DeleteMessage(dto.Id, messageId));
        Assert.Empty(db.Conversations);
    }

    [Fact]
    public async Task DeletedReplyTakesItsNotificationsReadOrNot()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var started = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Výlet", "Ve středu jedeme.", w.Student.Id));
        var id = ((ConversationDto)((CreatedAtActionResult)started.Result!).Value!).Id;
        var reply = (MessageDto)((OkObjectResult)(await As(db, w.Parent).Reply(id, new ReplyRequest("Nepojede."))).Result!).Value!;
        foreach (var n in db.Notifications)
        {
            n.ReadAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
        await As(db, w.Teacher).Reply(id, new ReplyRequest("Škoda."));

        Assert.IsType<NoContentResult>(await As(db, w.Parent).DeleteMessage(id, reply.Id));

        var left = db.Notifications.Where(n => n.TargetId == id).ToList();
        Assert.Equal(new[] { w.Parent.Id, w.Parent.Id, w.StudentUser.Id }.Order(), left.Select(n => n.UserId).Order());
        Assert.DoesNotContain(left, n => n.Body!.Contains("Nepojede"));
        Assert.Contains(left, n => n.UserId == w.StudentUser.Id && n.Body!.Contains("Škoda"));
    }

    [Fact]
    public async Task ListShowsOwnConversationsWithUnreadFlag()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        await As(db, w.Teacher).StartConversation(new StartConversationRequest("Výlet", "Text", w.Student.Id));

        var parentList = await As(db, w.Parent).GetConversations();
        var strangerList = await As(db, w.Stranger).GetConversations();

        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ConversationSummaryDto>>(
            Assert.IsType<OkObjectResult>(parentList.Result).Value));
        Assert.True(item.Unread);
        Assert.Equal("Jan Novák", item.StudentName);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ConversationSummaryDto>>(
            Assert.IsType<OkObjectResult>(strangerList.Result).Value));
    }

    private static MessageBroadcastController BroadcastAs(AppDbContext db, User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.ActiveRole?.Kind.ToString() ?? nameof(UserRole.SuperAdmin)),
            new Claim(PermissionAccess.RoleIdClaim, user.ActiveRole?.Id.ToString() ?? ""),
                        new Claim(ClaimTypes.Name, user.Username),
                    ])),
                },
            },
        };

    [Fact]
    public async Task SenderSeesBroadcastOnceAndItsFamiliesInDetail()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);
        var evaUser = new User { Username = "eva", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
        db.Users.Add(evaUser);
        await db.SaveChangesAsync();
        db.Students.Add(new StudentProfile
        {
            Person = new Person { FirstName = "Eva", LastName = "Malá", UserId = evaUser.Id, SchoolId = SchoolId },
            SchoolId = SchoolId,
            SchoolClassId = w.Student.SchoolClassId,
        });
        await db.SaveChangesAsync();

        await BroadcastAs(db, w.Teacher).BroadcastToClass(
            w.Student.SchoolClassId, new BroadcastRequest("Výlet", "Ve středu jedeme."));

        var list = Assert.IsAssignableFrom<IEnumerable<ConversationSummaryDto>>(
            Assert.IsType<OkObjectResult>((await As(db, w.Teacher).GetConversations()).Result).Value).ToList();
        var row = Assert.Single(list);
        Assert.NotNull(row.BroadcastId);
        Assert.Equal(2, row.BroadcastConversationCount);
        Assert.Equal("5.A", row.StudentName);

        // Recipients still see their own ordinary conversation.
        var parentList = Assert.IsAssignableFrom<IEnumerable<ConversationSummaryDto>>(
            Assert.IsType<OkObjectResult>((await As(db, w.Parent).GetConversations()).Result).Value);
        Assert.Null(Assert.Single(parentList).BroadcastId);

        var detail = Assert.IsType<BroadcastDto>(
            Assert.IsType<OkObjectResult>((await As(db, w.Teacher).GetBroadcast(row.BroadcastId!.Value)).Result).Value);
        Assert.Equal(2, detail.Conversations.Count);
        Assert.IsType<NotFoundResult>((await As(db, w.Parent).GetBroadcast(row.BroadcastId!.Value)).Result);
    }

    [Fact]
    public async Task StudentsOnlyBroadcastLeavesParentsOut()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        await BroadcastAs(db, w.Teacher).BroadcastToClass(
            w.Student.SchoolClassId, new BroadcastRequest("Úkol", "Nezapomeňte.", StudentsOnly: true));

        var participants = db.ConversationParticipants.Select(p => p.UserId).Order().ToList();
        Assert.Equal(new[] { w.Teacher.Id, w.StudentUser.Id }.Order(), participants);
    }

    [Fact]
    public async Task StudentOnlyConversationHasNoParents()
    {
        using var db = NewDb();
        var w = await SeedAsync(db);

        var result = await As(db, w.Teacher).StartConversation(
            new StartConversationRequest("Úkol", "Přines sešit.", w.Student.Id, StudentOnly: true));

        var dto = Assert.IsType<ConversationDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(new[] { w.Teacher.Id, w.StudentUser.Id }.Order(), dto.Participants.Select(p => p.UserId).Order());
    }
}
