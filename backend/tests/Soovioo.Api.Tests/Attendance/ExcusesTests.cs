using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Attendance;

public class ExcusesTests
{
    private const int SchoolId = 1;

    private static readonly DateOnly Friday = new(2026, 10, 30);
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    // Monday noon in Prague.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext db;
    private readonly SchoolClass classA;
    private readonly User novak, mala, kral, parent;
    private readonly StudentProfile jan;

    /// <summary>Jan of 5.A (class teacher Novák) and his parent. Monday: 1st
    /// maths by Novák, 2nd Czech by Malá, 3rd choir (Jan is in it) by Novák;
    /// Tuesday: 1st Czech by Malá. Král teaches nothing.</summary>
    public ExcusesTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));
        User Teacher(string name) => new()
        {
            Username = name, PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)],
            ActiveRole = db.Role(UserRole.Teacher), Person = new Person { FirstName = name, LastName = "Učitel", SchoolId = SchoolId },
        };
        novak = Teacher("Novák");
        mala = Teacher("Malá");
        kral = Teacher("Král");
        parent = new User { Username = "rodic", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = db.Role(UserRole.Parent) };
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola" });
        db.Users.AddRange(novak, mala, kral, parent);
        db.SaveChanges();
        classA = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId, ClassTeacherId = novak.Id };
        var maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        var czech = new Subject { Name = "Český jazyk", Abbreviation = "Čj", SchoolId = SchoolId };
        var music = new Subject { Name = "Hudební výchova", Abbreviation = "Hv", SchoolId = SchoolId };
        var version = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Rozvrh", ValidFrom = new DateOnly(2026, 9, 1), Status = TimetableVersionStatus.Published,
        };
        db.AddRange(classA, maths, czech, music, version);
        db.SaveChanges();
        jan = new StudentProfile
        {
            Person = new Person { FirstName = "Jan", LastName = "Žák", SchoolId = SchoolId },
            SchoolClassId = classA.Id, SchoolId = SchoolId,
        };
        db.Students.Add(jan);
        db.SaveChanges();
        db.LinkChildAsync(parent.Id, jan.Id, SchoolId).GetAwaiter().GetResult();
        var choir = new StudentGroup { SchoolId = SchoolId, Name = "Sbor", SubjectId = music.Id, TimetableVersionId = version.Id };
        db.StudentGroups.Add(choir);
        db.SaveChanges();
        db.StudentGroupClasses.Add(new StudentGroupClass { SchoolId = SchoolId, StudentGroupId = choir.Id, SchoolClassId = classA.Id });
        db.StudentGroupMembers.Add(new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = choir.Id, StudentId = jan.Id });
        TimetableEntry Lesson(Subject subject, DayOfWeek day, int period, User teacher, StudentGroup? group = null) => new()
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = classA.Id, SubjectId = subject.Id,
            DayOfWeek = day, PeriodNumber = period, TeacherId = teacher.Id, StudentGroup = group,
        };
        db.TimetableEntries.AddRange(
            Lesson(maths, DayOfWeek.Monday, 1, novak),
            Lesson(czech, DayOfWeek.Monday, 2, mala),
            Lesson(music, DayOfWeek.Monday, 3, novak, choir),
            Lesson(czech, DayOfWeek.Tuesday, 1, mala));
        db.SaveChanges();
    }

    private T As<T>(T controller, User user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = TestRoles.PrincipalOf(user) },
        };
        return controller;
    }

    private ExcusesController Excuses(User user) => As(new ExcusesController(
        db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, clock), clock,
        new MessageAttachmentStorageService(new FakeWebHostEnvironment())), user);

    private AbsencesController Absences(User user) => As(new AbsencesController(
        db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, clock), clock,
        new MessageAttachmentStorageService(new FakeWebHostEnvironment())), user);

    private async Task<int> ExcuseAsync(DateOnly from, DateOnly to, string reason = "Nemoc", List<int>? periods = null, User? by = null)
    {
        var result = await Excuses(by ?? parent).CreateExcuse(new ExcuseRequest(jan.Id, from, to, reason, periods));
        return Assert.IsType<ExcuseResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value).ExcuseId!.Value;
    }

    private Task<List<Absence>> AbsencesAsync() => db.Absences.AsNoTracking().OrderBy(a => a.Date).ToListAsync();

    private Task<List<int>> NotifiedAsync(int excuseId) => db.Notifications
        .Where(n => n.Type == NotificationType.NewExcuse && n.TargetId == excuseId).Select(n => n.UserId).ToListAsync();

    [Fact]
    public async Task EachSchoolDayOfTheRangeWaitsForApprovalWithTheDaysLessons()
    {
        var excuseId = await ExcuseAsync(Friday, Tuesday, "Hory");

        var absences = await AbsencesAsync();
        Assert.Equal([Friday, Monday, Tuesday], absences.Select(a => a.Date));
        Assert.All(absences, a =>
        {
            Assert.Equal(AbsenceStatus.PendingExcuse, a.Status);
            Assert.Equal(excuseId, a.ExcuseId);
            Assert.Equal(parent.Id, a.CreatedById);
        });
        Assert.Equal([], absences[0].Periods);
        Assert.Equal([1, 2, 3], absences[1].Periods);
        Assert.Equal([1], absences[2].Periods);
        Assert.Equal([novak.Id], await NotifiedAsync(excuseId));
        var dto = Assert.Single(Assert.IsType<OkObjectResult>((await Excuses(novak).GetExcuses()).Result).Value as IEnumerable<ExcuseDto>);
        Assert.Equal((ExcuseStatus.Pending, "Hory", 3), (dto.Status, dto.Reason, dto.Days.Count));
    }

    [Fact]
    public async Task OneDayTakesThePickedPeriods()
    {
        await ExcuseAsync(Monday, Monday, "Lékař", [2, 1]);

        Assert.Equal([1, 2], Assert.Single(await AbsencesAsync()).Periods);
    }

    [Fact]
    public async Task TheTeachersUnexcusedDayJoinsAndAnExcusedOneIsSkipped()
    {
        db.Absences.AddRange(
            new Absence { SchoolId = SchoolId, StudentId = jan.Id, Date = Monday, Periods = [2], CreatedById = mala.Id },
            new Absence { SchoolId = SchoolId, StudentId = jan.Id, Date = Tuesday, Status = AbsenceStatus.Excused, CreatedById = mala.Id });
        await db.SaveChangesAsync();

        var result = await Excuses(parent).CreateExcuse(new ExcuseRequest(jan.Id, Monday, Tuesday, "Nemoc"));

        var dto = Assert.IsType<ExcuseResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([new SkippedDayDto(Tuesday, AbsenceStatus.Excused)], dto.Skipped);
        var monday = (await AbsencesAsync()).Single(a => a.Date == Monday);
        Assert.Equal((AbsenceStatus.PendingExcuse, dto.ExcuseId), (monday.Status, monday.ExcuseId));
        Assert.Equal([2], monday.Periods);
    }

    [Fact]
    public async Task NothingToExcuseIsAnError()
    {
        var weekend = await Excuses(parent).CreateExcuse(
            new ExcuseRequest(jan.Id, Monday.AddDays(-2), Monday.AddDays(-1), "Nemoc"));

        Assert.IsType<BadRequestObjectResult>(weekend.Result);
        Assert.Empty(db.Excuses);
    }

    [Fact]
    public async Task TheClassTeachersExcuseIsApprovedUpToToday()
    {
        var excuseId = await ExcuseAsync(Friday, Tuesday, "Rodiče volali", by: novak);

        Assert.Equal([AbsenceStatus.Excused, AbsenceStatus.Excused, AbsenceStatus.PendingExcuse],
            (await AbsencesAsync()).Select(a => a.Status));
        Assert.Empty(await NotifiedAsync(excuseId));
        Assert.Single(db.Notifications.Where(n => n.UserId == parent.Id && n.Type == NotificationType.Excuse && n.TargetId == excuseId));
    }

    [Fact]
    public async Task AnotherTeacherCannotWriteOne()
    {
        var result = await Excuses(mala).CreateExcuse(new ExcuseRequest(jan.Id, Monday, Monday, "Nemoc"));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task DeletingDropsTheParentsDaysAndReturnsTheTeachersToUnexcused()
    {
        db.Absences.Add(new Absence { SchoolId = SchoolId, StudentId = jan.Id, Date = Monday, Periods = [2], CreatedById = mala.Id });
        await db.SaveChangesAsync();
        var excuseId = await ExcuseAsync(Monday, Tuesday);

        Assert.IsType<NoContentResult>(await Excuses(parent).DeleteExcuse(excuseId));

        var monday = Assert.Single(await AbsencesAsync());
        Assert.Equal((Monday, AbsenceStatus.Unexcused, null), (monday.Date, monday.Status, monday.ExcuseId));
        Assert.Empty(db.Excuses);
    }

    [Fact]
    public async Task EditingChangesOnlyTheDaysStillWaitingAndADecidedOneCannotBeChanged()
    {
        var excuseId = await ExcuseAsync(Friday, Tuesday);
        Assert.IsType<NoContentResult>(await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(true)));

        await Excuses(parent).UpdateExcuse(excuseId, new ExcuseRequest(jan.Id, Tuesday, Tuesday, "Angína"));

        Assert.Equal([(Friday, AbsenceStatus.Excused), (Monday, AbsenceStatus.Excused), (Tuesday, AbsenceStatus.PendingExcuse)],
            (await AbsencesAsync()).Select(a => (a.Date, a.Status)));
        Assert.Equal("Angína", (await db.Excuses.FindAsync(excuseId))!.Reason);

        clock.Now = clock.Now.AddDays(1);
        await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(true));
        Assert.IsType<BadRequestObjectResult>((await Excuses(parent).UpdateExcuse(excuseId,
            new ExcuseRequest(jan.Id, Tuesday, Tuesday, "Jiný důvod"))).Result);
        Assert.IsType<BadRequestObjectResult>(await Excuses(parent).DeleteExcuse(excuseId));
    }

    [Fact]
    public async Task ADayIsApprovedAtTheEarliestThatDayAndRejectedOnlyWithAReason()
    {
        var excuseId = await ExcuseAsync(Tuesday, Tuesday);
        var tuesday = Assert.Single(await AbsencesAsync());

        Assert.IsType<BadRequestObjectResult>(await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(true)));
        Assert.IsType<BadRequestObjectResult>(await Absences(novak).ReviewAbsence(tuesday.Id, new ReviewAbsenceRequest(true)));
        Assert.IsType<BadRequestObjectResult>(await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(false)));

        Assert.IsType<NoContentResult>(await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(false, "Bez lékařské zprávy")));

        var rejected = Assert.Single(await AbsencesAsync());
        Assert.Equal((AbsenceStatus.Rejected, "Bez lékařské zprávy"), (rejected.Status, rejected.ReviewNote));
        var told = Assert.Single(db.Notifications.Where(n => n.UserId == parent.Id && n.Type == NotificationType.Excuse));
        Assert.Equal(("Omluvenka zamítnuta", excuseId), (told.Title, told.TargetId));
        Assert.Contains("Bez lékařské zprávy", told.Body);
    }

    [Fact]
    public async Task DaysDecidedDifferentlyMakeItPartlyApproved()
    {
        var excuseId = await ExcuseAsync(Friday, Monday);
        var friday = (await AbsencesAsync()).First();
        await Absences(novak).ReviewAbsence(friday.Id, new ReviewAbsenceRequest(false, "Neověřeno"));
        await Excuses(novak).ReviewExcuse(excuseId, new ReviewAbsenceRequest(true));

        var dto = Assert.Single(Assert.IsType<OkObjectResult>((await Excuses(parent).GetExcuses()).Result).Value as IEnumerable<ExcuseDto>);
        Assert.Equal(ExcuseStatus.PartlyApproved, dto.Status);
    }

    [Fact]
    public async Task TheTeacherRecordingAnExcusedDayGivesItTheirPeriods()
    {
        var excuseId = await ExcuseAsync(Monday, Monday);

        var result = await Absences(novak).AddAbsence(new CreateAbsenceRequest(jan.Id, Monday, [1]));

        var dto = Assert.IsType<AbsenceDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal((AbsenceStatus.PendingExcuse, excuseId), (dto.Status, dto.ExcuseId));
        Assert.Equal([1], dto.Periods);
        Assert.Single(db.Absences);
    }

    [Fact]
    public async Task WhoSubstitutesTheAbsentClassTeacherInTheClassHearsOfIt()
    {
        db.TeacherAbsences.Add(new TeacherAbsence { SchoolId = SchoolId, TeacherId = novak.Id, DateFrom = Monday, DateTo = Monday });
        await db.SaveChangesAsync();
        var excuseId = await ExcuseAsync(Monday, Monday);
        Assert.Equal([novak.Id], await NotifiedAsync(excuseId));

        db.SubstitutionDays.Add(new SubstitutionDay
        {
            SchoolId = SchoolId, Date = Monday, PublishedAt = DateTimeOffset.UtcNow,
            Changes = [new LessonChangeData(Monday, 1, classA.Id, null, false, false, kral.Id, null, null, null, null, null, null)],
        });
        await db.SaveChangesAsync();
        var book = new ExcuseBook(db, new Notifier(db, clock), clock);
        await book.PublishedAsync(Monday);
        await db.SaveChangesAsync();
        await book.PublishedAsync(Monday);
        await db.SaveChangesAsync();

        Assert.Equal([novak.Id, kral.Id], (await NotifiedAsync(excuseId)).Order());
    }

    [Fact]
    public async Task AFileGoesWithTheExcuseWhileItWaits()
    {
        var excuseId = await ExcuseAsync(Monday, Monday);
        var bytes = "%PDF-1.4"u8.ToArray();
        IFormFile File(string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(), ContentType = "application/pdf",
        };

        Assert.IsType<OkObjectResult>((await Excuses(parent).UploadAttachment(excuseId, File("zprava.pdf"))).Result);
        Assert.IsType<BadRequestObjectResult>((await Excuses(parent).UploadAttachment(excuseId, File("virus.exe"))).Result);
        Assert.IsType<ForbidResult>((await Excuses(mala).UploadAttachment(excuseId, File("zprava.pdf"))).Result);

        var dto = Assert.Single(Assert.IsType<OkObjectResult>((await Excuses(novak).GetExcuses()).Result).Value as IEnumerable<ExcuseDto>);
        Assert.Equal("zprava.pdf", Assert.Single(dto.Attachments).FileName);
    }
}
