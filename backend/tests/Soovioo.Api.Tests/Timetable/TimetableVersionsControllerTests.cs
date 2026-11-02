using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableVersionsControllerTests
{
    private const int SchoolId = 1;

    // Noon in Prague, a Monday.
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 2, 11, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 11, 2);

    private readonly AppDbContext db;
    private readonly SchoolClass schoolClass;
    private readonly Subject maths, czech;
    private readonly TimetableVersion current;

    /// <summary>The version valid since September has maths on Monday 1st.</summary>
    public TimetableVersionsControllerTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = Today };
        schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", SchoolId = SchoolId };
        czech = new Subject { Name = "Český jazyk", SchoolId = SchoolId };
        current = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Rozvrh 2026/27", ValidFrom = new DateOnly(2026, 9, 1),
            Status = TimetableVersionStatus.Published,
        };
        db.AddRange(schoolClass, maths, czech, current);
        db.SaveChanges();
        db.TimetableEntries.Add(Lesson(current, maths));
        db.SaveChanges();
    }

    private TimetableEntry Lesson(TimetableVersion version, Subject subject) => new()
    {
        SchoolId = SchoolId, TimetableVersion = version, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
        DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1,
    };

    private TimetableVersionsController Versions(UserRole role = UserRole.Admin) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 100 + (int)role, role)),
        };

    private TimetableController Timetable(UserRole role = UserRole.Admin) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 100 + (int)role, role)),
        };

    private async Task<TimetableVersionDto> Draft(string name, DateOnly validFrom, int? copyFrom = null) =>
        Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(
            (await Versions().CreateVersion(new(name, validFrom, copyFrom))).Result).Value);

    private async Task<List<string>> SubjectsOn(DateOnly? date = null, int? versionId = null, UserRole role = UserRole.Admin)
    {
        var result = await Timetable(role).GetTimetable(schoolClass.Id, versionId, date);
        return Assert.IsAssignableFrom<IEnumerable<TimetableEntryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value)
            .Select(e => e.SubjectName).ToList();
    }

    [Fact]
    public async Task ADraftIsACopyEditedAsideUntilPublishedForItsDate()
    {
        var draft = await Draft("2. pololetí", new DateOnly(2027, 2, 1), current.Id);
        Assert.Equal((TimetableVersionStatus.Draft, 1), (draft.Status, draft.EntryCount));
        var copied = await db.TimetableEntries.SingleAsync(e => e.TimetableVersionId == draft.Id);
        await Timetable().UpdateEntry(schoolClass.Id, copied.Id, new(czech.Id, DayOfWeek.Monday, 1, WeekParity.Every));

        Assert.Equal(["Matematika"], await SubjectsOn());
        Assert.Equal(["Matematika"], await SubjectsOn(new DateOnly(2027, 2, 1)));
        Assert.Equal(["Český jazyk"], await SubjectsOn(versionId: draft.Id));

        Assert.IsType<OkObjectResult>((await Versions().PublishVersion(draft.Id)).Result);

        Assert.Equal(new DateOnly(2027, 1, 31), current.ValidTo);
        Assert.Equal(["Matematika"], await SubjectsOn(new DateOnly(2027, 1, 31)));
        Assert.Equal(["Český jazyk"], await SubjectsOn(new DateOnly(2027, 2, 1)));
    }

    [Fact]
    public async Task OthersSeeOnlyPublishedVersions()
    {
        var draft = await Draft("Koncept", new DateOnly(2027, 2, 1), current.Id);

        var seen = Assert.IsAssignableFrom<IEnumerable<TimetableVersionDto>>(
            Assert.IsType<OkObjectResult>((await Versions(UserRole.Teacher).GetVersions()).Result).Value);
        Assert.Equal([current.Id], seen.Select(v => v.Id));
        Assert.IsType<NotFoundResult>((await Timetable(UserRole.Parent).GetTimetable(schoolClass.Id, draft.Id)).Result);
    }

    [Fact]
    public async Task ARoleMayBeGivenTheVersionsToLookAt()
    {
        var draft = await Draft("Koncept", new DateOnly(2027, 2, 1), current.Id);
        var teacherRole = db.Role(UserRole.Teacher);
        teacherRole.Permissions.Single(p => p.Permission == Permission.TimetableVersionsRead).Scope = PermissionScope.School;
        await db.SaveChangesAsync();

        var seen = Assert.IsAssignableFrom<IEnumerable<TimetableVersionDto>>(
            Assert.IsType<OkObjectResult>((await Versions(UserRole.Teacher).GetVersions()).Result).Value);
        Assert.Equal([draft.Id, current.Id], seen.Select(v => v.Id));
        Assert.Equal(["Matematika"], await SubjectsOn(versionId: draft.Id, role: UserRole.Teacher));
    }

    [Fact]
    public async Task APublishedVersionStartsAfterTheOneThatAppliesAndNotInThePast()
    {
        var past = await Draft("Zpětně", Today.AddDays(-1));
        Assert.IsType<BadRequestObjectResult>((await Versions().PublishVersion(past.Id)).Result);

        var today = await Draft("Od dneška", Today);
        Assert.IsType<OkObjectResult>((await Versions().PublishVersion(today.Id)).Result);
        var sameDay = await Draft("Taky od dneška", Today);
        Assert.IsType<BadRequestObjectResult>((await Versions().PublishVersion(sameDay.Id)).Result);
    }

    [Fact]
    public async Task ArchivingAFutureVersionGivesItsTimeBackToThePreviousOne()
    {
        var spring = await Draft("2. pololetí", new DateOnly(2027, 2, 1), current.Id);
        await Versions().PublishVersion(spring.Id);

        Assert.IsType<BadRequestObjectResult>((await Versions().ArchiveVersion(current.Id)).Result);
        Assert.IsType<OkObjectResult>((await Versions().ArchiveVersion(spring.Id)).Result);
        Assert.Null(current.ValidTo);
        Assert.Equal(["Matematika"], await SubjectsOn(new DateOnly(2027, 3, 1)));

        var archived = await db.TimetableEntries.FirstAsync(e => e.TimetableVersionId == spring.Id);
        Assert.IsType<BadRequestObjectResult>(
            (await Timetable().UpdateEntry(schoolClass.Id, archived.Id, new(czech.Id, DayOfWeek.Monday, 1, WeekParity.Every))).Result);

        var restored = Assert.IsType<TimetableVersionDto>(Assert.IsType<OkObjectResult>(
            (await Versions().RestoreVersion(spring.Id)).Result).Value);
        Assert.Equal(TimetableVersionStatus.Draft, restored.Status);
    }

    [Fact]
    public async Task ConflictsAreFoundOnlyWithinTheVersion()
    {
        var teacher = TestRoles.Principal(db, 7, UserRole.Teacher).UserId();
        (await db.TimetableEntries.SingleAsync()).TeacherId = teacher;
        await db.SaveChangesAsync();
        var draft = await Draft("Koncept", new DateOnly(2027, 2, 1));

        var added = await Timetable().AddEntry(
            schoolClass.Id, new(czech.Id, DayOfWeek.Monday, 1, WeekParity.Every, teacher, TimetableVersionId: draft.Id));

        Assert.Empty(Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>(added.Result).Value).Conflicts);
    }

    [Fact]
    public async Task TheFirstLessonOfASchoolWithoutATimetableStartsOneForTheSchoolYear()
    {
        db.TimetableVersions.Remove(current);
        await db.SaveChangesAsync();

        var added = await Timetable().AddEntry(schoolClass.Id, new(maths.Id, DayOfWeek.Monday, 1, WeekParity.Every));

        Assert.IsType<CreatedAtActionResult>(added.Result);
        var version = await db.TimetableVersions.SingleAsync();
        Assert.Equal(("Rozvrh 2026/27", new DateOnly(2026, 9, 1), TimetableVersionStatus.Published),
            (version.Name, version.ValidFrom, version.Status));
    }
}
