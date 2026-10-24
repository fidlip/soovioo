using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Retention;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Retention;

public class RetentionTests
{
    private const int SchoolId = 1;
    private const int AdminId = 900;

    // Noon in Prague, so "today" is the same day in either zone.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 24);

    private readonly AppDbContext db;
    private readonly SchoolClass schoolClass;
    private readonly Subject subject;

    public RetentionTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = Today };
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        db.Users.Add(new User { Id = AdminId, Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId });
        schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        db.SaveChanges();
    }

    private Student AddStudent(string name, DateOnly? leftAt = null)
    {
        var student = new Student { FirstName = name, LastName = "Novák", SchoolClassId = schoolClass.Id, SchoolId = SchoolId, LeftAt = leftAt };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    private User AddUser(string username, params UserRole[] roles)
    {
        var user = new User { Username = username, PasswordHash = "x", Role = roles[0], Roles = [.. roles], SchoolId = SchoolId };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private async Task<User> AddParentAsync(string username, params Student[] children)
    {
        var parent = AddUser(username, UserRole.Parent);
        foreach (var child in children)
        {
            await db.LinkChildAsync(parent.Id, child.Id, SchoolId);
        }
        await db.SaveChangesAsync();
        return parent;
    }

    private AccountEraser Accounts()
    {
        var photos = new PhotoStorage(new MessageAttachmentStorageService(new FakeWebHostEnvironment()));
        return new AccountEraser(db, AuthTestServices.RefreshTokens(db, clock), photos, clock);
    }

    private StudentEraser Students()
    {
        var storage = new MessageAttachmentStorageService(new FakeWebHostEnvironment());
        return new StudentEraser(db, Accounts(), new PhotoStorage(storage), storage);
    }

    private RetentionJob Job() => new(db, Accounts(), Students(), clock);

    private RetentionController Controller(int adminId = AdminId)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()), new Claim(ClaimTypes.Role, "Admin"),
        ]);
        return new RetentionController(
            db, new FakeCurrentSchoolContext(SchoolId), Students(), Accounts(), new LeaveNotifier(db, clock), new Notifier(db, clock), clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private async Task SetPolicyAsync(Action<RetentionPolicy> configure)
    {
        var policy = new RetentionPolicy { SchoolId = SchoolId };
        configure(policy);
        db.RetentionPolicies.Add(policy);
        await db.SaveChangesAsync();
    }

    private async Task<Student?> FindAnyStudentAsync(int id) =>
        await db.Students.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    [Fact]
    public async Task FormerPupilsAreLeftOutUnlessAskedFor()
    {
        AddStudent("Aktivní");
        AddStudent("Končí", leftAt: Today);
        AddStudent("Bývalý", leftAt: Today.AddDays(-1));

        Assert.Equal(["Aktivní", "Končí"], await db.Students.OrderBy(s => s.Id).Select(s => s.FirstName).ToListAsync());

        db.IncludeFormerStudents = true;
        Assert.Equal(3, await db.Students.CountAsync());
    }

    [Fact]
    public async Task ParentsLoseAccessToAFormerPupil()
    {
        var current = AddStudent("Petr");
        var former = AddStudent("Pavel", leftAt: Today.AddDays(-1));
        var parent = await AddParentAsync("rodic", current, former);

        Assert.Equal([current.Id], await db.ChildIdsOf(parent.Id).ToListAsync());
    }

    [Fact]
    public async Task JobDeactivatesAccountsOnceTheGracePeriodIsOver()
    {
        await SetPolicyAsync(p => p.AccountGraceDays = 30);
        var pupil = AddStudent("Pavel", leftAt: Today.AddDays(-31));
        var pupilAccount = AddUser("pavel", UserRole.Student);
        pupil.UserId = pupilAccount.Id;
        var sibling = AddStudent("Petr");
        var recent = AddStudent("Jana", leftAt: Today.AddDays(-5));
        var onlyFormer = await AddParentAsync("rodic1", pupil);
        var hasCurrentChild = await AddParentAsync("rodic2", pupil, sibling);
        var inGrace = await AddParentAsync("rodic3", recent);
        var teacherParent = AddUser("ucitelka", UserRole.Teacher, UserRole.Parent);
        await db.LinkChildAsync(teacherParent.Id, pupil.Id, SchoolId);
        await db.SaveChangesAsync();

        await Job().RunAsync();

        Assert.NotNull(pupilAccount.DeactivatedAt);
        Assert.NotNull(onlyFormer.DeactivatedAt);
        Assert.Null(hasCurrentChild.DeactivatedAt);
        Assert.Null(inGrace.DeactivatedAt);
        Assert.Null(teacherParent.DeactivatedAt);
    }

    [Fact]
    public async Task JobErasesOnlyInAutomaticModeAndOnlyWhatIsDue()
    {
        await SetPolicyAsync(p =>
        {
            p.Mode = ErasureMode.Confirm;
            p.ContactsYears = 1;
            p.GradesYears = 5;
        });
        var student = AddStudent("Pavel", leftAt: Today.AddYears(-2));
        student.ContactPhone = "777 000 111";
        db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        await Job().RunAsync();
        Assert.Equal("777 000 111", (await FindAnyStudentAsync(student.Id))!.ContactPhone);

        (await db.RetentionPolicies.SingleAsync()).Mode = ErasureMode.Automatic;
        await db.SaveChangesAsync();
        await Job().RunAsync();

        var after = (await FindAnyStudentAsync(student.Id))!;
        Assert.Null(after.ContactPhone);
        Assert.Contains(RetentionCategory.Contacts, after.ErasedCategories);
        Assert.DoesNotContain(RetentionCategory.Grades, after.ErasedCategories);
        Assert.Equal(1, await db.Grades.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ErasingTheProfileRemovesThePupilAndOrphanedParents()
    {
        var former = AddStudent("Pavel", leftAt: Today.AddDays(-1));
        var sibling = AddStudent("Petr");
        var own = AddUser("pavel", UserRole.Student);
        former.UserId = own.Id;
        var orphan = await AddParentAsync("rodic1", former);
        var stays = await AddParentAsync("rodic2", former, sibling);
        db.Grades.Add(new Grade { StudentId = former.Id, SubjectId = subject.Id, Value = 2, SchoolId = SchoolId });
        db.Absences.Add(new Absence { StudentId = former.Id, Date = Today.AddDays(-10), SchoolId = SchoolId });
        db.Conversations.Add(new Conversation { StudentId = former.Id, Subject = "Omluvenka", SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await Controller().EraseStudent(former.Id, new EraseRequest(Everything: true));

        Assert.IsType<NoContentResult>(result.Result);
        Assert.Null(await FindAnyStudentAsync(former.Id));
        Assert.Empty(await db.Grades.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Absences.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Conversations.IgnoreQueryFilters().ToListAsync());

        Assert.NotNull(own.ErasedAt);
        Assert.Equal($"vymazany-{own.Id}", own.Username);
        Assert.NotNull(orphan.ErasedAt);
        var orphanPerson = await db.Persons.IgnoreQueryFilters().SingleAsync(p => p.UserId == orphan.Id);
        Assert.Equal(AccountEraser.ErasedFirstName, orphanPerson.FirstName);
        Assert.Null(stays.ErasedAt);
        Assert.Equal([sibling.Id], await db.ChildIdsOf(stays.Id).ToListAsync());
    }

    [Fact]
    public async Task EraseRefusesACurrentPupil()
    {
        var student = AddStudent("Pavel", leftAt: Today);

        var result = await Controller().EraseStudent(student.Id, new EraseRequest(Everything: true));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(await FindAnyStudentAsync(student.Id));
    }

    [Fact]
    public async Task CancellingADepartureBringsTheAccountsBack()
    {
        var student = AddStudent("Pavel", leftAt: Today.AddDays(-40));
        var parent = await AddParentAsync("rodic", student);
        parent.DeactivatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Controller().CancelLeave(student.Id));

        Assert.Null((await FindAnyStudentAsync(student.Id))!.LeftAt);
        Assert.Null(parent.DeactivatedAt);
    }

    [Fact]
    public async Task ADepartureCanNotChangeOnceSomethingWasErased()
    {
        var student = AddStudent("Pavel", leftAt: Today.AddDays(-40));
        student.ErasedCategories = [RetentionCategory.Photos];
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await Controller().CancelLeave(student.Id));
        var result = await Controller().SetLeave(student.Id, new LeaveRequest(Today));
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetLeaveRecordsTheDeparture()
    {
        var student = AddStudent("Pavel");

        var result = await Controller().SetLeave(student.Id, new LeaveRequest(Today.AddDays(30), StudentLeaveReason.Transfer, " Přestup do Brna "));

        var dto = Assert.IsType<FormerStudentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(dto.Former);
        Assert.Equal("Přestup do Brna", dto.Note);
        Assert.Equal(StudentLeaveReason.Transfer, (await FindAnyStudentAsync(student.Id))!.LeaveReason);
    }

    [Fact]
    public async Task PolicyRejectsACategoryKeptLongerThanTheProfile()
    {
        var request = RetentionPolicyDto.From(new RetentionPolicy()) with { GradesYears = 20, ProfileYears = 10 };

        var result = await Controller().SetPolicy(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Contains("gradesYears", problem.Errors.Keys);
    }

    [Fact]
    public async Task PolicyIsSaved()
    {
        var request = RetentionPolicyDto.From(new RetentionPolicy()) with { Mode = ErasureMode.Automatic, AccountGraceDays = 7 };

        Assert.IsType<OkObjectResult>((await Controller().SetPolicy(request)).Result);

        var saved = await db.RetentionPolicies.SingleAsync();
        Assert.Equal(ErasureMode.Automatic, saved.Mode);
        Assert.Equal(7, saved.AccountGraceDays);
    }

    [Fact]
    public async Task DueStaffIsErased()
    {
        await SetPolicyAsync(p => p.StaffYears = 2);
        var old = AddUser("stary", UserRole.Teacher);
        old.DeactivatedAt = clock.GetUtcNow().AddYears(-3);
        var recent = AddUser("novy", UserRole.Teacher);
        recent.DeactivatedAt = clock.GetUtcNow().AddYears(-1);
        await db.SaveChangesAsync();

        var result = await Controller().EraseDue();

        Assert.Equal(new EraseDueResult(0, 1), Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(old.ErasedAt);
        Assert.Null(recent.ErasedAt);
    }

    [Fact]
    public async Task ADeactivatedAccountCanNotLogIn()
    {
        var hasher = new PasswordHasher<User>();
        var user = AddUser("rodic", UserRole.Parent);
        user.PasswordHash = hasher.HashPassword(user, "heslo");
        user.DeactivatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        var controller = new AuthController(
            db, AuthTestServices.Sessions(db, clock), AuthTestServices.RefreshTokens(db, clock), AuthTestServices.SupportSessions(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var wrongPassword = await controller.Login(new LoginRequest("rodic", "spatne", "skola"));
        var rightPassword = await controller.Login(new LoginRequest("rodic", "heslo", "skola"));

        Assert.IsType<UnauthorizedResult>(wrongPassword.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(rightPassword.Result).StatusCode);
    }

    [Fact]
    public async Task PeopleListsDeparturesAndWhatIsDue()
    {
        await SetPolicyAsync(p => p.PhotosYears = 0);
        AddStudent("Aktivní");
        AddStudent("Končí", leftAt: Today.AddDays(10));
        AddStudent("Bývalý", leftAt: Today.AddDays(-1));

        var result = await Controller().GetPeople();

        var overview = Assert.IsType<RetentionOverviewDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["Končí", "Bývalý"], overview.Students.Select(s => s.FirstName));
        var former = overview.Students.Single(s => s.Former);
        Assert.Equal([RetentionCategory.Photos], former.Due);
        Assert.Empty(overview.Students.Single(s => !s.Former).Due);
    }

    private async Task<List<(string Body, List<int> Recipients)>> NoticesAsync() =>
        (await db.Conversations.IgnoreQueryFilters().Include(c => c.Messages).Include(c => c.Participants).ToListAsync())
            .Select(c => (c.Messages.Single().Body, c.Participants.Select(p => p.UserId).Where(id => id != AdminId).Order().ToList()))
            .ToList();

    [Fact]
    public async Task ADepartureIsToldOnlyThoseChosenAndTheReasonOnlyToSome()
    {
        var student = AddStudent("Pavel");
        var own = AddUser("pavel", UserRole.Student);
        student.UserId = own.Id;
        var parent = await AddParentAsync("rodic", student);
        var teacher = AddUser("tridni", UserRole.Teacher);
        schoolClass.ClassTeacherId = teacher.Id;
        await db.SaveChangesAsync();

        await Controller().SetLeave(student.Id, new LeaveRequest(
            Today.AddDays(30), StudentLeaveReason.Expelled, "jen pro školu",
            Notices: [new(LeaveNoticeGroup.Parents), new(LeaveNoticeGroup.ClassTeacher, WithReason: true)]));

        var notices = await NoticesAsync();
        Assert.Equal(2, notices.Count);
        var toParent = notices.Single(n => n.Recipients.SequenceEqual([parent.Id]));
        var toTeacher = notices.Single(n => n.Recipients.SequenceEqual([teacher.Id]));
        Assert.DoesNotContain("Důvod", toParent.Body);
        Assert.Contains("Důvod: vyloučení ze školy.", toTeacher.Body);
        Assert.Contains("23. 11. 2026", toParent.Body);
        Assert.All(notices, n => Assert.DoesNotContain("jen pro školu", n.Body));
    }

    [Fact]
    public async Task OnlyANewLastDayIsToldAgain()
    {
        var student = AddStudent("Pavel");
        var parent = await AddParentAsync("rodic", student);
        IReadOnlyList<LeaveNotice> toParents = [new(LeaveNoticeGroup.Parents)];

        await Controller().SetLeave(student.Id, new LeaveRequest(Today.AddDays(30), Notices: toParents));
        await Controller().SetLeave(student.Id, new LeaveRequest(Today.AddDays(30), Note: "jen poznámka", Notices: toParents));
        await Controller().SetLeave(student.Id, new LeaveRequest(Today.AddDays(40), Notices: toParents));
        await Controller().CancelLeave(student.Id, new CancelLeaveRequest(toParents));

        var bodies = (await NoticesAsync()).Select(n => n.Body).ToList();
        Assert.Equal(3, bodies.Count);
        Assert.Contains(bodies, b => b.StartsWith("Mění se datum"));
        Assert.Contains(bodies, b => b.Contains("bylo zrušeno"));
    }

    [Fact]
    public async Task AnOtherReasonNeedsItsWords()
    {
        var student = AddStudent("Pavel");

        var missing = await Controller().SetLeave(student.Id, new LeaveRequest(Today, StudentLeaveReason.Other));
        var given = await Controller().SetLeave(student.Id, new LeaveRequest(Today, StudentLeaveReason.Other, ReasonDetail: " Stěhování do zahraničí "));

        Assert.IsType<BadRequestObjectResult>(missing.Result);
        var dto = Assert.IsType<FormerStudentDto>(Assert.IsType<OkObjectResult>(given.Result).Value);
        Assert.Equal("Stěhování do zahraničí", dto.ReasonDetail);
    }
}
