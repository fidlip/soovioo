using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Retention;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Retention;

public class StaffLeavingTests
{
    private const int SchoolId = 1;

    // Noon in Prague.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 24);

    private readonly AppDbContext db;
    private readonly User admin, teacher, colleague, parent;
    private readonly SchoolClass classA, classB;
    private readonly Subject maths;
    private readonly StudentProfile pupil;

    /// <summary>5.A is led and taught maths (Monday 1st) by the teacher who
    /// leaves; the colleague leads 6.B and teaches it on Monday 1st too.</summary>
    public StaffLeavingTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = Today };
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        admin = NewUser("admin", UserRole.Admin);
        teacher = NewUser("ucitel", UserRole.Teacher);
        colleague = NewUser("kolega", UserRole.Teacher);
        parent = NewUser("rodic", UserRole.Parent);
        db.Users.AddRange(admin, teacher, colleague, parent);
        db.SaveChanges();
        db.Persons.AddRange(
            new Person { UserId = teacher.Id, SchoolId = SchoolId, FirstName = "Tereza", LastName = "Učitelová" },
            new Person { UserId = colleague.Id, SchoolId = SchoolId, FirstName = "Karel", LastName = "Kolega" });
        classA = new SchoolClass { Name = "5.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        classB = new SchoolClass { Name = "6.B", SchoolId = SchoolId, ClassTeacherId = colleague.Id };
        maths = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        db.Subjects.Add(maths);
        db.SaveChanges();
        db.TimetableEntries.AddRange(
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1 },
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 2 },
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = classB.Id, SubjectId = maths.Id, TeacherId = colleague.Id, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, WeekParity = WeekParity.Odd });
        pupil = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = classA.Id, SchoolId = SchoolId };
        db.Students.Add(pupil);
        db.SaveChanges();
        db.LinkChildAsync(parent.Id, pupil.Id, SchoolId).GetAwaiter().GetResult();
        db.SaveChanges();
    }

    private User NewUser(string username, UserRole role) => new()
    {
        Username = username, PasswordHash = "x", Roles = [db.Role(role, SchoolId)], ActiveRole = db.Role(role, SchoolId), SchoolId = SchoolId,
    };

    private AccountEraser Accounts() =>
        new(db, AuthTestServices.RefreshTokens(db, clock),
            new PhotoStorage(new MessageAttachmentStorageService(new FakeWebHostEnvironment())), clock);

    private StaffLeaving Leaving() => new(db, Accounts(), clock);

    private RetentionController Controller()
    {
        var storage = new MessageAttachmentStorageService(new FakeWebHostEnvironment());
        return new RetentionController(
            db, new FakeCurrentSchoolContext(SchoolId),
            new StudentEraser(db, Accounts(), new PhotoStorage(storage), storage), Accounts(),
            new LeaveNotifier(db, clock), new Notifier(db, clock), Leaving(), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(admin)),
        };
    }

    private StaffLeaveRequest HandOver(DateOnly lastDay, params StaffLeaveNoticeGroup[] notices) => new(
        lastDay, StaffLeaveReason.Notice, "Odchází k jiné škole",
        [new ClassSuccessorDto(classA.Id, colleague.Id)], [new SubjectSubstituteDto(maths.Id, colleague.Id)], notices);

    [Fact]
    public async Task PreviewListsWhatToHandOverAndConflictsOfTheSubstitute()
    {
        var preview = Assert.IsType<StaffLeavePreviewDto>(Assert.IsType<OkObjectResult>(
            (await Controller().GetStaffLeave(teacher.Id)).Result).Value);
        Assert.Equal(["5.A"], preview.LedClasses.Select(c => c.Name));
        var taught = Assert.Single(preview.Subjects);
        Assert.Equal(("Matematika", 2), (taught.Name, taught.Lessons));
        Assert.Equal(["Karel Kolega", "admin"], preview.Staff.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(preview.NoticeDefaults!);

        var conflicts = Assert.IsAssignableFrom<IReadOnlyList<HandoverConflictDto>>(Assert.IsType<OkObjectResult>(
            (await Controller().GetHandoverConflicts(teacher.Id, new([new(maths.Id, colleague.Id)]))).Result).Value);
        // Monday 1st: the colleague teaches 6.B there in odd weeks; Tuesday is free.
        var conflict = Assert.Single(conflicts);
        Assert.Equal((DayOfWeek.Monday, 1, "5.A", "6.B"), (conflict.DayOfWeek, conflict.PeriodNumber, conflict.Class, conflict.OtherClass));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(5, 9, true)]
    [InlineData(6, 9, false)]
    [InlineData(1, 4, false)]
    public async Task PreviewOffersStaffWhoseAprobaceCoversTheSubjectInTheGrade(int? from, int? to, bool qualified)
    {
        classA.Rocnik = 5;
        var history = new Subject { Name = "Dějepis", SchoolId = SchoolId };
        db.TeacherProfiles.Add(new TeacherProfile
        {
            Person = await db.Persons.SingleAsync(p => p.UserId == colleague.Id),
            SchoolId = SchoolId,
            Qualifications =
            [
                new TeacherQualification { SchoolId = SchoolId, SubjectId = maths.Id, GradeFrom = from, GradeTo = to },
                new TeacherQualification { SchoolId = SchoolId, Subject = history },
            ],
        });
        await db.SaveChangesAsync();

        var preview = Assert.IsType<StaffLeavePreviewDto>(Assert.IsType<OkObjectResult>(
            (await Controller().GetStaffLeave(teacher.Id)).Result).Value);

        Assert.Equal(qualified ? [colleague.Id] : [], Assert.Single(preview.Subjects).QualifiedIds);
    }

    [Fact]
    public async Task HandsOverTheLessonsAndAssignmentsOfTheCurrentAndLaterVersionsButNotOfPastOnes()
    {
        var past = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Loni", ValidFrom = new DateOnly(1999, 9, 1), ValidTo = new DateOnly(2000, 8, 31),
            Status = TimetableVersionStatus.Published,
        };
        var draft = new TimetableVersion { SchoolId = SchoolId, Name = "Koncept", ValidFrom = Today.AddMonths(3), Status = TimetableVersionStatus.Draft };
        db.TimetableEntries.AddRange(
            new TimetableEntry { TimetableVersion = past, SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id, DayOfWeek = DayOfWeek.Friday, PeriodNumber = 1 },
            new TimetableEntry { TimetableVersion = draft, SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id, DayOfWeek = DayOfWeek.Friday, PeriodNumber = 1 });
        var history = new Subject { Name = "Dějepis", SchoolId = SchoolId };
        db.TeachingAssignments.AddRange(
            new TeachingAssignment { TimetableVersion = past, SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id },
            new TeachingAssignment { TimetableVersion = draft, SchoolId = SchoolId, SchoolClassId = classA.Id, SubjectId = maths.Id, TeacherId = teacher.Id },
            new TeachingAssignment { TimetableVersion = draft, SchoolId = SchoolId, SchoolClassId = classA.Id, Subject = history, TeacherId = teacher.Id });
        await db.SaveChangesAsync();
        await Controller().SetStaffLeave(teacher.Id, HandOver(Today));

        clock.Now = clock.Now.AddDays(1);
        await Leaving().ApplyDueAsync(SchoolId);

        Assert.Equal(teacher.Id, (await db.TimetableEntries.SingleAsync(t => t.TimetableVersionId == past.Id)).TeacherId);
        Assert.Equal(colleague.Id, (await db.TimetableEntries.SingleAsync(t => t.TimetableVersionId == draft.Id)).TeacherId);
        Assert.All(await db.TimetableEntriesOn(Today.AddDays(1)).Where(t => t.SubjectId == maths.Id).ToListAsync(),
            t => Assert.Equal(colleague.Id, t.TeacherId));
        // History isn't handed over: chosen for automatically again.
        Assert.Equal(
            [(past.Id, maths.Id, teacher.Id), (draft.Id, maths.Id, colleague.Id)],
            (await db.TeachingAssignments.ToListAsync()).Select(a => (a.TimetableVersionId, a.SubjectId, a.TeacherId)).Order());
    }

    [Fact]
    public async Task WorksThroughTheLastDayThenHandsOverAndStops()
    {
        Assert.IsType<OkObjectResult>((await Controller().SetStaffLeave(teacher.Id, HandOver(Today.AddDays(3)))).Result);
        Assert.Equal(2, await db.StaffHandovers.CountAsync());
        Assert.Empty(await db.Conversations.ToListAsync());
        var planned = Assert.Single(Assert.IsType<RetentionOverviewDto>(
            Assert.IsType<OkObjectResult>((await Controller().GetPeople()).Result).Value).Staff);
        Assert.Equal((0, 0), (planned.ClassesLed, planned.Lessons));

        clock.Now = clock.Now.AddDays(3);
        await Leaving().ApplyDueAsync(SchoolId);
        Assert.Null(teacher.DeactivatedAt);
        Assert.Equal(teacher.Id, classA.ClassTeacherId);

        clock.Now = clock.Now.AddDays(1);
        await Leaving().ApplyDueAsync(SchoolId);
        Assert.NotNull(teacher.DeactivatedAt);
        Assert.Equal(colleague.Id, classA.ClassTeacherId);
        Assert.All(await db.TimetableEntries.ToListAsync(), t => Assert.Equal(colleague.Id, t.TeacherId));
        Assert.Empty(await db.StaffHandovers.ToListAsync());

        db.Today = Today.AddDays(4);
        var overview = Assert.IsType<RetentionOverviewDto>(Assert.IsType<OkObjectResult>((await Controller().GetPeople()).Result).Value);
        var former = Assert.Single(overview.Staff);
        Assert.Equal((Today.AddDays(3), 0, 0), (former.LeftAt, former.ClassesLed, former.Lessons));
    }

    [Fact]
    public async Task WhatIsNotHandedOverStaysAndShowsInTheArchive()
    {
        Assert.IsType<OkObjectResult>((await Controller().SetStaffLeave(teacher.Id,
            new StaffLeaveRequest(Today))).Result);

        var announced = Assert.IsType<RetentionOverviewDto>(Assert.IsType<OkObjectResult>((await Controller().GetPeople()).Result).Value);
        var row = Assert.Single(announced.Staff);
        Assert.Null(row.DeactivatedAt);
        Assert.Equal((1, 2), (row.ClassesLed, row.Lessons));

        clock.Now = clock.Now.AddDays(1);
        await Leaving().ApplyDueAsync(SchoolId);
        Assert.NotNull(teacher.DeactivatedAt);
        Assert.Equal(teacher.Id, classA.ClassTeacherId);
    }

    [Fact]
    public async Task ChecksTheRequest()
    {
        var past = await Controller().SetStaffLeave(teacher.Id, new StaffLeaveRequest(Today.AddDays(-1)));
        Assert.IsType<ObjectResult>(past.Result, exactMatch: false);
        Assert.Null(teacher.LeftAt);

        var toParent = await Controller().SetStaffLeave(teacher.Id, new StaffLeaveRequest(
            Today, Substitutes: [new SubjectSubstituteDto(maths.Id, parent.Id)]));
        Assert.IsType<BadRequestObjectResult>(toParent.Result);

        var notTheirs = await Controller().SetStaffLeave(teacher.Id, new StaffLeaveRequest(
            Today, ClassTeachers: [new ClassSuccessorDto(classB.Id, colleague.Id)]));
        Assert.IsType<BadRequestObjectResult>(notTheirs.Result);

        // The only admin can't leave.
        Assert.IsType<BadRequestObjectResult>((await Controller().SetStaffLeave(admin.Id, new StaffLeaveRequest(Today))).Result);
        Assert.IsType<NotFoundResult>((await Controller().SetStaffLeave(parent.Id, new StaffLeaveRequest(Today))).Result);
    }

    [Fact]
    public async Task TheNoticeGoesToTheChosenGroupsWithTheSuccessorAndNeverTheReason()
    {
        await Controller().SetStaffLeave(teacher.Id, HandOver(Today.AddDays(3),
            StaffLeaveNoticeGroup.LedClasses, StaffLeaveNoticeGroup.Colleagues));

        var conversations = await db.Conversations.Include(c => c.Participants).Include(c => c.Messages).ToListAsync();
        Assert.Equal(2, conversations.Count);
        var family = conversations.Single(c => c.StudentId == pupil.Id);
        Assert.Contains(family.Participants, p => p.UserId == parent.Id);
        var body = family.Messages.Single().Body;
        Assert.Contains("Tereza Učitelová ve škole Škola končí. Poslední den ve škole: 27. 10. 2026.", body);
        Assert.Contains("Třídním učitelem třídy 5.A bude Karel Kolega.", body);
        Assert.DoesNotContain("výpověď", body);
        Assert.DoesNotContain("jiné škole", body);
        var staff = conversations.Single(c => c.StudentId == null);
        Assert.Equal([colleague.Id], staff.Participants.Where(p => p.UserId != admin.Id).Select(p => p.UserId));

        // The same day again: nobody is told twice. A new day is.
        await Controller().SetStaffLeave(teacher.Id, HandOver(Today.AddDays(3), StaffLeaveNoticeGroup.LedClasses));
        Assert.Equal(2, await db.Conversations.CountAsync());
        await Controller().SetStaffLeave(teacher.Id, HandOver(Today.AddDays(5), StaffLeaveNoticeGroup.LedClasses));
        Assert.Equal(3, await db.Conversations.CountAsync());
    }

    [Fact]
    public async Task CancelledAndReactivatedDeparturesAreForgotten()
    {
        await Controller().SetStaffLeave(teacher.Id, HandOver(Today.AddDays(3)));
        Assert.IsType<NoContentResult>(await Controller().CancelStaffLeave(teacher.Id,
            new CancelStaffLeaveRequest([StaffLeaveNoticeGroup.Colleagues])));
        Assert.Null(teacher.LeftAt);
        Assert.Empty(await db.StaffHandovers.ToListAsync());
        Assert.Contains("byl zrušen", (await db.Messages.SingleAsync()).Body);

        // Deactivated after the last day, then back: the past last day mustn't deactivate it again.
        await Controller().SetStaffLeave(teacher.Id, new StaffLeaveRequest(Today));
        clock.Now = clock.Now.AddDays(1);
        await Leaving().ApplyDueAsync(SchoolId);
        Accounts().Reactivate(teacher);
        await db.SaveChangesAsync();
        await Leaving().ApplyDueAsync(SchoolId);
        Assert.Null(teacher.DeactivatedAt);
        Assert.Null(teacher.LeftAt);
    }
}
