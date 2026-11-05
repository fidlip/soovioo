using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableChangeNoticeTests
{
    private const int SchoolId = 1;

    // Noon in Prague, a Monday.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Spring = new(2027, 2, 1);

    private readonly FakeClock clock = new(Now);
    private readonly DbContextOptions<AppDbContext> options =
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly AppDbContext db;

    private readonly SchoolClass classA, classB;
    private readonly Subject maths, czech;
    private readonly User teacherA, teacherB, parentJan, pupilJan, parentEva;
    private readonly StudentProfile jan, eva;
    private readonly TimetableVersion current;

    /// <summary>Since September maths in 5.A by teacher A on Monday 1st and
    /// Czech in 5.B by teacher B on Monday 2nd. Jan of 5.A has an account and a
    /// parent, Eva of 5.B a parent.</summary>
    public TimetableChangeNoticeTests()
    {
        db = new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId)) { Today = new DateOnly(2026, 11, 2) };
        User NewUser(string name, UserRole role) =>
            new() { Username = name, PasswordHash = "x", ActiveRole = db.Role(role, SchoolId), SchoolId = SchoolId };
        teacherA = NewUser("ucitelA", UserRole.Teacher);
        teacherB = NewUser("ucitelB", UserRole.Teacher);
        parentJan = NewUser("rodicJan", UserRole.Parent);
        pupilJan = NewUser("jan", UserRole.Student);
        parentEva = NewUser("rodicEva", UserRole.Parent);
        classA = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        classB = new SchoolClass { Name = "5.B", SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        czech = new Subject { Name = "Český jazyk", Abbreviation = "Čj", SchoolId = SchoolId };
        current = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Rozvrh 2026/27", ValidFrom = new DateOnly(2026, 9, 1),
            Status = TimetableVersionStatus.Published,
        };
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola" });
        db.Users.AddRange(teacherA, teacherB, parentJan, pupilJan, parentEva);
        db.AddRange(classA, classB, maths, czech, current);
        db.SaveChanges();
        jan = Pupil("Jan", classA, pupilJan.Id);
        eva = Pupil("Eva", classB);
        db.LinkChildAsync(parentJan.Id, jan.Id, SchoolId).GetAwaiter().GetResult();
        db.LinkChildAsync(parentEva.Id, eva.Id, SchoolId).GetAwaiter().GetResult();
        db.TimetableEntries.AddRange(
            Lesson(current, classA, maths, 1, teacherA),
            Lesson(current, classB, czech, 2, teacherB));
        db.SaveChanges();
    }

    private StudentProfile Pupil(string name, SchoolClass schoolClass, int? userId = null)
    {
        var pupil = new StudentProfile
        {
            Person = new Person { FirstName = name, LastName = "Žák", SchoolId = SchoolId, UserId = userId },
            SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(pupil);
        db.SaveChanges();
        return pupil;
    }

    private static TimetableEntry Lesson(
        TimetableVersion version, SchoolClass schoolClass, Subject subject, int period, User? teacher, StudentGroup? group = null) => new()
    {
        SchoolId = SchoolId, TimetableVersion = version, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
        DayOfWeek = DayOfWeek.Monday, PeriodNumber = period, TeacherId = teacher?.Id, StudentGroup = group,
    };

    private TimetableVersionsController Versions(TimeProvider? at = null) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), at ?? clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 100, UserRole.Admin)),
        };

    private async Task<TimetableVersion> CopyAsync(DateOnly validFrom)
    {
        var dto = Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(
            (await Versions().CreateVersion(new("Změna", validFrom, current.Id))).Result).Value);
        return await db.TimetableVersions.FirstAsync(v => v.Id == dto.Id);
    }

    /// <summary>A draft from <paramref name="validFrom"/> with Czech instead of
    /// maths in 5.A's first period.</summary>
    private async Task<TimetableVersion> ChangedAsync(DateOnly validFrom)
    {
        var version = await CopyAsync(validFrom);
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classA.Id);
        lesson.SubjectId = czech.Id;
        await db.SaveChangesAsync();
        return version;
    }

    private async Task PublishAsync(TimetableVersion version, int? daysBefore = null) =>
        Assert.IsType<OkObjectResult>((await Versions().PublishVersion(version.Id, new(daysBefore))).Result);

    private Task<List<Notification>> OfAsync(User user) =>
        db.Notifications.Where(n => n.UserId == user.Id && n.Type == NotificationType.TimetableChange).ToListAsync();

    private async Task<List<int>> RecipientsAsync() =>
        await db.Notifications.Where(n => n.Type == NotificationType.TimetableChange).Select(n => n.UserId).Distinct().ToListAsync();

    private TimetableChangeNoticeJob Job(DateTimeOffset at) =>
        new(db, options, new FakeClock(at), NullLogger<TimetableChangeNoticeJob>.Instance);

    /// <summary>The job of the day <see cref="Now"/> is in, or of <paramref name="at"/>.</summary>
    private async Task EveningAsync(DateTimeOffset? at = null)
    {
        await Job(at ?? new DateTimeOffset(2026, 11, 2, 19, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
    }

    private static readonly DateTimeOffset NextEvening = new(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnlyThoseWhoseLessonsChangedHearAboutItWithWhatChanged()
    {
        await PublishAsync(await ChangedAsync(Spring));
        await EveningAsync();

        Assert.Equal(
            new[] { teacherA.Id, parentJan.Id, pupilJan.Id }.Order(),
            (await RecipientsAsync()).Order());

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Jan Žák", parent.Title);
        Assert.Equal("Přibylo: Po 1. Čj (ucitelA). Ubylo: Po 1. M (ucitelA).", parent.Body);
        Assert.Equal("timetable", parent.Page);
        Assert.Equal(jan.Id, parent.StudentId);

        var teacher = Assert.Single(await OfAsync(teacherA));
        Assert.Equal("Změna rozvrhu od 1. 2. 2027", teacher.Title);
        Assert.Equal("Přibylo: Po 1. Čj 5.A. Ubylo: Po 1. M 5.A.", teacher.Body);
    }

    [Fact]
    public async Task ACopyWithoutChangesTellsNobody()
    {
        await PublishAsync(await CopyAsync(Spring));
        await EveningAsync();

        Assert.Empty(await RecipientsAsync());
    }

    [Fact]
    public async Task ATeacherWhoLostTheirLessonsHearsAboutIt()
    {
        var version = await ChangedAsync(Spring);
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classA.Id);
        lesson.TeacherId = teacherB.Id;
        await db.SaveChangesAsync();

        await PublishAsync(version);

        await EveningAsync();

        Assert.Equal("Ubylo: Po 1. M 5.A.", Assert.Single(await OfAsync(teacherA)).Body);
        Assert.Equal("Přibylo: Po 1. Čj 5.A.", Assert.Single(await OfAsync(teacherB)).Body);
    }

    [Fact]
    public async Task APupilMovedToAnotherGroupHearsAboutItOthersDoNot()
    {
        var mates = Pupil("Petr", classA);
        var parentPetr = new User { Username = "rodicPetr", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(parentPetr);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentPetr.Id, mates.Id, SchoolId);
        StudentGroup Group(string name, TimetableVersion version, params StudentProfile[] members) => new()
        {
            SchoolId = SchoolId, TimetableVersion = version, Name = name, SubjectId = czech.Id,
            Members = [.. members.Select(m => new StudentGroupMember { SchoolId = SchoolId, StudentId = m.Id })],
        };
        var one = Group("Čj1", current, jan, mates);
        var two = Group("Čj2", current);
        db.TimetableEntries.AddRange(Lesson(current, classA, czech, 3, teacherA, one), Lesson(current, classA, czech, 3, teacherA, two));
        await db.SaveChangesAsync();

        var version = await CopyAsync(Spring);
        var groups = await db.StudentGroups.Include(g => g.Members).Where(g => g.TimetableVersionId == version.Id).ToListAsync();
        var from = groups.Single(g => g.Name == "Čj1");
        from.Members.RemoveAll(m => m.StudentId == jan.Id);
        groups.Single(g => g.Name == "Čj2").Members.Add(new StudentGroupMember { SchoolId = SchoolId, StudentId = jan.Id });
        await db.SaveChangesAsync();

        await PublishAsync(version);

        await EveningAsync();

        Assert.Equal(
            "Přibylo: Po 3. Čj Čj2 (ucitelA). Ubylo: Po 3. Čj Čj1 (ucitelA).",
            Assert.Single(await OfAsync(parentJan)).Body);
        Assert.Empty(await OfAsync(parentPetr));
        Assert.Empty(await OfAsync(teacherA));
        Assert.Empty(await OfAsync(parentEva));
    }

    [Fact]
    public async Task AParentOfTwoGetsOneNoticePerChild()
    {
        await db.LinkChildAsync(parentJan.Id, eva.Id, SchoolId);
        await db.SaveChangesAsync();
        var version = await ChangedAsync(Spring);
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classB.Id);
        lesson.PeriodNumber = 5;
        await db.SaveChangesAsync();

        await PublishAsync(version);

        await EveningAsync();

        Assert.Equal(new int?[] { jan.Id, eva.Id }.Order(), (await OfAsync(parentJan)).Select(n => n.StudentId).Order());
    }

    [Fact]
    public async Task TheFirstVersionOfASchoolYearTellsEveryoneWithALesson()
    {
        var idle = new User { Username = "bezHodin", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(idle);
        var version = await CopyAsync(new DateOnly(2027, 9, 1));
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classB.Id);
        lesson.TeacherId = teacherA.Id;
        await db.SaveChangesAsync();

        await PublishAsync(version);

        await EveningAsync();

        Assert.Equal(
            new[] { teacherA.Id, parentJan.Id, pupilJan.Id, parentEva.Id }.Order(),
            (await RecipientsAsync()).Order());
        var parent = Assert.Single(await OfAsync(parentEva));
        Assert.Equal("Nový rozvrh od 1. 9. 2027 — Eva Žák", parent.Title);
        Assert.Equal("Škola zveřejnila rozvrh „Změna“.", parent.Body);
    }

    [Fact]
    public async Task AVersionAfterOneWithoutLessonsTellsEveryoneWithALesson()
    {
        db.TimetableEntries.RemoveRange(db.TimetableEntries);
        await db.SaveChangesAsync();
        var version = await CopyAsync(Spring);
        db.TimetableEntries.Add(Lesson(version, classB, czech, 2, teacherB));
        await db.SaveChangesAsync();

        await PublishAsync(version);

        await EveningAsync();

        Assert.Equal("Nový rozvrh od 1. 2. 2027", Assert.Single(await OfAsync(teacherB)).Title);
        Assert.Single(await OfAsync(parentEva));
        Assert.Empty(await OfAsync(parentJan));
    }

    [Fact]
    public async Task ANoticePlannedAheadGoesOutOnItsDayOnce()
    {
        var version = await ChangedAsync(Spring);

        await PublishAsync(version, 7);
        Assert.Empty(await RecipientsAsync());
        Assert.Equal(new DateOnly(2027, 1, 25), version.ChangeNoticeOn);

        await Job(new DateTimeOffset(2027, 1, 24, 6, 0, 0, TimeSpan.Zero)).RunAsync();
        Assert.Empty(await RecipientsAsync());

        await Job(new DateTimeOffset(2027, 1, 25, 6, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
        var sent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Jan Žák", sent.Title);

        sent.ReadAt = Now;
        await db.SaveChangesAsync();
        await Job(new DateTimeOffset(2027, 1, 26, 6, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
        Assert.Single(await OfAsync(parentJan));
    }

    [Fact]
    public async Task ANoticePlannedForADayAlreadyPastGoesOutThatEvening()
    {
        await PublishAsync(await ChangedAsync(new DateOnly(2026, 11, 5)), 7);
        await EveningAsync();

        Assert.Single(await OfAsync(parentJan));
    }

    [Fact]
    public async Task ThePlannedNoticeComparesWithTheVersionsOfItsDay()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version, 7);

        // Published in between with the same lessons: nothing is left to tell about the later one.
        var between = await ChangedAsync(new DateOnly(2027, 1, 4));
        await PublishAsync(between);
        await EveningAsync();
        Assert.Equal("Změna rozvrhu od 4. 1. 2027 — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);

        await Job(new DateTimeOffset(2027, 1, 25, 6, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
        Assert.Single(await OfAsync(parentJan));
    }

    [Fact]
    public async Task UnpublishingBeforeTheNoticeWentTellsNobody()
    {
        var planned = await ChangedAsync(Spring);
        await PublishAsync(planned, 7);

        await Versions().UnpublishVersion(planned.Id);

        await EveningAsync();

        Assert.Null(planned.ChangeNoticeOn);
        await Job(new DateTimeOffset(2027, 1, 25, 6, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
        Assert.Empty(await RecipientsAsync());
    }

    [Fact]
    public async Task UnpublishingTellsWhoHeardReadOrNot()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();
        var read = Assert.Single(await OfAsync(parentJan));
        read.ReadAt = Now;
        read.PushedAt = Now;
        await db.SaveChangesAsync();

        await Versions().UnpublishVersion(version.Id);

        await EveningAsync();

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Jan Žák", parent.Title);
        Assert.Equal("Škola zrušila rozvrh „Změna“, který měl platit od 1. 2. 2027. Neřiďte se jím.", parent.Body);
        Assert.Null(parent.ReadAt);
        Assert.Equal(jan.Id, parent.StudentId);
        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší", Assert.Single(await OfAsync(teacherA)).Title);
        Assert.Equal(
            new[] { teacherA.Id, parentJan.Id, pupilJan.Id }.Order(),
            (await RecipientsAsync()).Order());
    }

    [Fact]
    public async Task ArchivingTellsWhoHeard()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();

        await Versions().ArchiveVersion(version.Id);

        await EveningAsync();

        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);
    }

    [Fact]
    public async Task AWithdrawnVersionPublishedAgainStartsAnew()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();
        await Versions().UnpublishVersion(version.Id);
        await EveningAsync();

        // Jan's first period is as it was: the family has nothing new coming,
        // and the notice that it's off must stay with them.
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classA.Id);
        lesson.SubjectId = maths.Id;
        var other = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classB.Id);
        other.SubjectId = maths.Id;
        await db.SaveChangesAsync();
        await PublishAsync(await db.TimetableVersions.FirstAsync(v => v.Id == version.Id));
        await EveningAsync();

        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);
        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Eva Žák", Assert.Single(await OfAsync(parentEva)).Title);

        // Withdrawn again: only who heard the second time hears it's off.
        await Versions().UnpublishVersion(version.Id);
        await EveningAsync();

        Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Eva Žák", Assert.Single(await OfAsync(parentEva)).Title);
    }

    [Fact]
    public async Task ThoseWhoSwitchedTheKindOffDontHearItsOff()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();
        var muted = await db.Users.FirstAsync(u => u.Id == parentJan.Id);
        muted.MutedNotifications = [NotificationType.TimetableChange];
        muted.MutedEmailNotifications = [NotificationType.TimetableChange];
        await db.SaveChangesAsync();

        await Versions().UnpublishVersion(version.Id);

        await EveningAsync();

        Assert.Empty(await OfAsync(parentJan));
        Assert.Single(await OfAsync(pupilJan));
    }

    private StudentGroupsController Groups() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 100, UserRole.Admin)),
        };

    /// <summary>In <paramref name="version"/>: Němčina on Monday 3rd by teacher B,
    /// with nobody in it, in 5.A.</summary>
    private async Task<StudentGroup> GermanAsync(TimetableVersion version, bool withLesson = true)
    {
        var group = new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersion = version, Name = "Nj", SubjectId = czech.Id,
            Classes = [new StudentGroupClass { SchoolId = SchoolId, SchoolClassId = classA.Id }],
        };
        db.StudentGroups.Add(group);
        if (withLesson)
        {
            db.TimetableEntries.Add(Lesson(version, classA, czech, 3, teacherB, group));
        }
        await db.SaveChangesAsync();
        return group;
    }

    private async Task SetMembersAsync(TimetableVersion version, StudentGroup group, params int[] pupils) =>
        Assert.IsType<OkObjectResult>((await Groups().UpdateGroupMembers(version.Id, group.Id, new(pupils))).Result);

    [Fact]
    public async Task APupilJoiningAGroupOfTheValidVersionTellsTheirFamilyOnly()
    {
        var group = await GermanAsync(current);

        await SetMembersAsync(current, group, jan.Id);

        await EveningAsync();

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Změna rozvrhu — Jan Žák", parent.Title);
        Assert.Equal("Zařazení do skupiny Nj. Přibylo: Po 3. Čj Nj (ucitelB).", parent.Body);
        Assert.Equal(current.Id, parent.TargetId);
        Assert.Equal(jan.Id, parent.StudentId);
        Assert.Single(await OfAsync(pupilJan));
        Assert.Empty(await OfAsync(teacherB));
        Assert.Empty(await OfAsync(parentEva));
    }

    [Fact]
    public async Task APupilLeavingAGroupTellsTheirFamily()
    {
        var group = await GermanAsync(current);
        await SetMembersAsync(current, group, jan.Id);
        await EveningAsync();
        Assert.Single(await OfAsync(parentJan)).ReadAt = Now;
        await db.SaveChangesAsync();

        await SetMembersAsync(current, group);

        await EveningAsync();

        var parent = await OfAsync(parentJan);
        Assert.Equal(2, parent.Count);
        Assert.Contains(parent, n => n.ReadAt == null && n.Body == "Vyřazení ze skupiny Nj. Ubylo: Po 3. Čj Nj (ucitelB).");
    }

    [Fact]
    public async Task ChangesOfDaysApartMeetInTheOneUnreadNotice()
    {
        var one = await GermanAsync(current);
        var two = await GermanAsync(current);
        var other = await db.StudentGroups.FirstAsync(g => g.Id == two.Id);
        other.Name = "Nj2";
        await db.SaveChangesAsync();
        await SetMembersAsync(current, one, jan.Id);
        await EveningAsync();

        await SetMembersAsync(current, one);
        await SetMembersAsync(current, two, jan.Id);
        await EveningAsync(NextEvening);

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.StartsWith("Zařazení do skupiny Nj. Přibylo: Po 3. Čj Nj (ucitelB). ", parent.Body);
        Assert.Contains("Vyřazení ze skupiny Nj. Ubylo: Po 3. Čj Nj (ucitelB).", parent.Body);
        Assert.Contains("Zařazení do skupiny Nj2. Přibylo: Po 3. Čj Nj2 (ucitelB).", parent.Body);
    }

    [Fact]
    public async Task JoiningAndLeavingTheSameDayTellsNobody()
    {
        var group = await GermanAsync(current);

        await SetMembersAsync(current, group, jan.Id);
        await SetMembersAsync(current, group);
        await EveningAsync();

        Assert.Empty(await RecipientsAsync());
        Assert.Empty(await db.TimetableGroupChanges.ToListAsync());
    }

    [Fact]
    public async Task NothingBeforeTheDaysEndAndOnceAfterIt()
    {
        var changed = await ChangedAsync(Spring);
        var group = await GermanAsync(current);
        await SetMembersAsync(current, group, jan.Id);
        await PublishAsync(changed);

        Assert.Empty(await RecipientsAsync());

        await EveningAsync();
        Assert.Equal(2, (await OfAsync(parentJan)).Count);

        foreach (var n in await db.Notifications.ToListAsync())
        {
            n.ReadAt = Now;
        }
        await db.SaveChangesAsync();
        await EveningAsync(NextEvening);
        Assert.Equal(2, (await OfAsync(parentJan)).Count);
    }

    [Fact]
    public async Task PublishedAndTakenBackTheSameDayTellsNobody()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await Versions().UnpublishVersion(version.Id);
        await EveningAsync();

        Assert.Empty(await RecipientsAsync());
    }

    [Fact]
    public async Task ReworkedTheSameDayItsOneNoticeOfTheLastState()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await Versions().UnpublishVersion(version.Id);
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classA.Id);
        lesson.PeriodNumber = 4;
        lesson.SubjectId = maths.Id;
        await db.SaveChangesAsync();
        await PublishAsync(version);
        await EveningAsync();

        Assert.Equal("Přibylo: Po 4. M (ucitelA). Ubylo: Po 1. M (ucitelA).", Assert.Single(await OfAsync(parentJan)).Body);
    }

    [Fact]
    public async Task ReworkedADayLaterWhoHeardIsCorrected()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();

        // Next day only Eva's lessons differ: Jan's family was told something untrue.
        await Versions().UnpublishVersion(version.Id);
        var lesson = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classA.Id);
        lesson.SubjectId = maths.Id;
        var other = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == version.Id && e.SchoolClassId == classB.Id);
        other.SubjectId = maths.Id;
        await db.SaveChangesAsync();
        await PublishAsync(version);
        await EveningAsync(NextEvening);

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Rozvrh od 1. 2. 2027 se upravil — Jan Žák", parent.Title);
        Assert.Equal("Škola upravila rozvrh „Změna“. Proti dosavadnímu rozvrhu se vám nic nemění.", parent.Body);
        Assert.Null(parent.TargetId);
        Assert.Equal("Rozvrh od 1. 2. 2027 se upravil", Assert.Single(await OfAsync(teacherA)).Title);
        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Eva Žák", Assert.Single(await OfAsync(parentEva)).Title);
    }

    [Fact]
    public async Task PublishedAgainWithTheNoticeAheadWhoHeardHearsItsOffMeanwhile()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();

        await Versions().UnpublishVersion(version.Id);
        await PublishAsync(version, 7);
        await EveningAsync(NextEvening);

        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);

        await EveningAsync(new DateTimeOffset(2027, 1, 25, 19, 0, 0, TimeSpan.Zero));

        Assert.Contains(await OfAsync(parentJan), n => n.Title == "Změna rozvrhu od 1. 2. 2027 — Jan Žák");
    }

    [Fact]
    public async Task WithdrawnTheSameDayThePupilsChangesAreForgotten()
    {
        var version = await CopyAsync(Spring);
        var group = await GermanAsync(version);
        await PublishAsync(version);
        await EveningAsync();

        await SetMembersAsync(version, group, jan.Id);
        await Versions().UnpublishVersion(version.Id);
        await EveningAsync(NextEvening);

        Assert.Empty(await OfAsync(parentJan));
        Assert.Empty(await db.TimetableGroupChanges.ToListAsync());
    }

    [Fact]
    public async Task TheSameListOfPupilsTellsNobody()
    {
        var group = await GermanAsync(current);
        await SetMembersAsync(current, group, jan.Id);
        await EveningAsync();
        Assert.Single(await OfAsync(parentJan)).ReadAt = Now;
        await db.SaveChangesAsync();

        await SetMembersAsync(current, group, jan.Id);

        await EveningAsync();

        Assert.Single(await OfAsync(parentJan));
    }

    [Fact]
    public async Task AGroupWithoutLessonsIsToldToo()
    {
        var group = await GermanAsync(current, withLesson: false);

        await SetMembersAsync(current, group, jan.Id);
        await EveningAsync();

        Assert.Equal("Zařazení do skupiny Nj.", Assert.Single(await OfAsync(parentJan)).Body);
    }

    [Fact]
    public async Task WithTheNoticeYetToGoAGroupWithoutLessonsIsToldAnyway()
    {
        var version = await CopyAsync(Spring);
        var group = await GermanAsync(version, withLesson: false);
        await PublishAsync(version, 7);

        await SetMembersAsync(version, group, jan.Id);
        await EveningAsync();

        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Jan Žák", parent.Title);
        Assert.Equal("Zařazení do skupiny Nj.", parent.Body);
    }

    [Fact]
    public async Task AVersionToComeSaysSinceWhen()
    {
        var version = await CopyAsync(Spring);
        var group = await GermanAsync(version);
        await PublishAsync(version);
        await EveningAsync();

        await SetMembersAsync(version, group, jan.Id);

        await EveningAsync();

        Assert.Equal("Změna rozvrhu od 1. 2. 2027 — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);
    }

    [Fact]
    public async Task WithTheNoticeYetToGoItComparesWithTheNewPupils()
    {
        var version = await CopyAsync(Spring);
        var group = await GermanAsync(version);
        await PublishAsync(version, 7);

        await SetMembersAsync(version, group, jan.Id);

        await EveningAsync();

        Assert.Empty(await OfAsync(parentJan));
        await Job(new DateTimeOffset(2027, 1, 25, 19, 0, 0, TimeSpan.Zero)).RunAsync();
        db.ChangeTracker.Clear();
        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Equal("Přibylo: Po 3. Čj Nj (ucitelB).", parent.Body);
    }

    [Fact]
    public async Task WhoHeardOnlyOfTheirGroupHearsItsOffToo()
    {
        var version = await CopyAsync(Spring);
        var group = await GermanAsync(version);
        await PublishAsync(version);
        await EveningAsync();
        await SetMembersAsync(version, group, jan.Id);
        await EveningAsync();
        Assert.Single(await OfAsync(parentJan));

        await Versions().UnpublishVersion(version.Id);

        await EveningAsync();

        Assert.Equal("Rozvrh od 1. 2. 2027 se ruší — Jan Žák", Assert.Single(await OfAsync(parentJan)).Title);
    }

    [Fact]
    public async Task MovingTheStartTellsThemAgainWithTheNewDate()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();
        var read = Assert.Single(await OfAsync(parentJan));
        read.ReadAt = Now;
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>((await Versions().UpdateVersion(version.Id, new("Změna", new DateOnly(2027, 3, 1)))).Result);
        await EveningAsync(NextEvening);

        Assert.Equal("Změna rozvrhu od 1. 3. 2027", Assert.Single(await OfAsync(teacherA)).Title);
        var parent = Assert.Single(await OfAsync(parentJan));
        Assert.Null(parent.ReadAt);
        Assert.Equal("Změna rozvrhu od 1. 3. 2027 — Jan Žák", parent.Title);
    }

    [Fact]
    public async Task MovingTheStartMovesThePlannedNoticeToo()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version, 7);

        await Versions().UpdateVersion(version.Id, new("Změna", new DateOnly(2027, 3, 1)));

        Assert.Equal(new DateOnly(2027, 2, 22), version.ChangeNoticeOn);
        Assert.Empty(await RecipientsAsync());
    }

    [Fact]
    public async Task RenamingAPublishedVersionTellsNobodyAgain()
    {
        var version = await ChangedAsync(Spring);
        await PublishAsync(version);
        await EveningAsync();
        var before = (await OfAsync(teacherA)).Single().CreatedAt;
        clock.Now += TimeSpan.FromHours(1);

        await Versions().UpdateVersion(version.Id, new("Jiný název", Spring));

        Assert.Equal(before, (await OfAsync(teacherA)).Single().CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task HowFarAheadIsLimited(int days)
    {
        var version = await ChangedAsync(Spring);

        Assert.IsType<BadRequestObjectResult>((await Versions().PublishVersion(version.Id, new(days))).Result);
        Assert.Equal(TimetableVersionStatus.Draft, version.Status);
    }

    [Fact]
    public async Task WhoSwitchedTheKindOffDoesNotGetIt()
    {
        parentJan.MutedNotifications = [NotificationType.TimetableChange];
        await db.SaveChangesAsync();

        await PublishAsync(await ChangedAsync(Spring));

        await EveningAsync();

        Assert.Empty(await OfAsync(parentJan));
        Assert.Single(await OfAsync(pupilJan));
    }

    [Fact]
    public void TheSummarySumsUpTheRest()
    {
        var now = Enumerable.Range(1, TimetableChangeNotice.MaxLessons + 2).Select(i => $"L{i}").ToList();

        var summary = TimetableChangeNotice.Summary(now, ["L1", "Z"]);

        Assert.Equal("Přibylo: L2, L3, L4, L5, L6, L7, L8, L9 a další (1). Ubylo: Z.", summary);
        Assert.Null(TimetableChangeNotice.Summary(["A"], ["A"]));
    }

    [Fact]
    public void TheDailyEmailLinksToTheWeekOfTheChange()
    {
        var body = NotificationDigestJob.Body("http://app/skola", "t", [new Notification
        {
            Title = "Změna rozvrhu", Page = "timetable", Type = NotificationType.TimetableChange, TargetId = 7,
        }]);

        Assert.Contains("http://app/skola/timetable?version=7&change=start", body);
    }

    [Fact]
    public void ThePushWithoutFullTextSaysOnlyTheKind()
    {
        Assert.Equal("Změna rozvrhu", PushJob.GenericTitle(NotificationType.TimetableChange));
    }
}
