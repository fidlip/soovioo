using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableBoardTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db;
    private readonly TimetableVersion draft;
    private readonly Subject maths, pe;
    private readonly SchoolClass fifth, sixth;
    private readonly User teacher, colleague;
    private readonly TeacherProfile profile;
    private readonly Classroom gym, smallGym;

    public TimetableBoardTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));
        gym = new Classroom { Name = "Tělocvična", Type = ClassroomType.Gym, SchoolId = SchoolId };
        smallGym = new Classroom { Name = "Malá", Type = ClassroomType.Gym, SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        pe = new Subject { Name = "Tělesná výchova", Abbreviation = "Tv", Classrooms = [new() { Type = ClassroomType.Gym }], SchoolId = SchoolId };
        fifth = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        sixth = new SchoolClass { Name = "6.A", Rocnik = 6, SchoolId = SchoolId };
        draft = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Od února", ValidFrom = new DateOnly(2027, 2, 1), Status = TimetableVersionStatus.Draft,
        };
        teacher = new User { Username = "novak", PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)] };
        colleague = new User { Username = "mala", PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)] };
        db.AddRange(gym, smallGym, maths, pe, fifth, sixth, draft, teacher, colleague);
        profile = new TeacherProfile { SchoolId = SchoolId };
        db.Persons.AddRange(
            new Person { SchoolId = SchoolId, FirstName = "Jan", LastName = "Novák", User = teacher, TeacherProfile = profile },
            new Person { SchoolId = SchoolId, FirstName = "Eva", LastName = "Malá", User = colleague, TeacherProfile = new TeacherProfile { SchoolId = SchoolId } });
        db.SaveChanges();
        db.TeachingAssignments.AddRange(
            new TeachingAssignment { SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = fifth.Id, SubjectId = maths.Id, TeacherId = teacher.Id },
            new TeachingAssignment { SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = fifth.Id, SubjectId = pe.Id, TeacherId = colleague.Id });
        db.CurriculumItems.AddRange(
            new CurriculumItem { SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 5, SubjectId = maths.Id, HoursPerWeek = 3 },
            new CurriculumItem { SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 5, SubjectId = pe.Id, HoursPerWeek = 1 });
        db.SaveChanges();
    }

    private TimetableBoardController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    private TimetableEntry Lesson(
        Subject subject, DayOfWeek day, int period, User? by = null, SchoolClass? of = null, bool locked = false, Classroom? room = null, StudentGroup? group = null)
    {
        var entry = new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = (of ?? fifth).Id, SubjectId = subject.Id,
            TeacherId = (by ?? teacher).Id, DayOfWeek = day, PeriodNumber = period, Locked = locked, ClassroomId = room?.Id,
            StudentGroupId = group?.Id,
        };
        db.TimetableEntries.Add(entry);
        db.SaveChanges();
        return entry;
    }

    private StudentGroup Group(string name, GroupDivision? division, User by, params SchoolClass[] classes)
    {
        var group = new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = name, SubjectId = pe.Id, HoursPerWeek = 1, TeacherId = by.Id,
            GroupDivision = division,
            Classes = [.. classes.Select(c => new StudentGroupClass { SchoolId = SchoolId, SchoolClassId = c.Id })],
        };
        db.StudentGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    private async Task<DropSlot> Slot(TimetableEntry entry, DayOfWeek day, int period)
    {
        var result = await Controller().GetSlots(draft.Id, entry.Id);
        var slots = Assert.IsAssignableFrom<IEnumerable<DropSlot>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return slots.Single(s => s.Day == day && s.Period == period);
    }

    [Fact]
    public async Task AFreeSlotIsFree()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1);

        var slot = await Slot(lesson, DayOfWeek.Tuesday, 1);

        Assert.Equal(DropSlotStatus.Free, slot.Status);
        Assert.Empty(slot.Reasons);
    }

    [Fact]
    public async Task ASlotWhereTheClassOrTheTeacherHasALessonOrTheTeacherCannotIsBlocked()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(pe, DayOfWeek.Tuesday, 1, colleague);
        Lesson(maths, DayOfWeek.Wednesday, 1, of: sixth);
        profile.Availability = [new AvailabilitySlot(DayOfWeek.Thursday, 1, AvailabilityKind.Unavailable)];
        db.SaveChanges();

        Assert.Equal(["5.A má v tu dobu jinou hodinu."], (await Slot(lesson, DayOfWeek.Tuesday, 1)).Reasons);
        Assert.Equal(DropSlotStatus.Blocked, (await Slot(lesson, DayOfWeek.Tuesday, 1)).Status);
        Assert.Equal(["Jan Novák v tu dobu učí."], (await Slot(lesson, DayOfWeek.Wednesday, 1)).Reasons);
        Assert.Equal(["Jan Novák v tu dobu nemůže."], (await Slot(lesson, DayOfWeek.Thursday, 1)).Reasons);
    }

    [Fact]
    public async Task ALessonOfAnotherWeekIsNoClash()
    {
        var odd = Lesson(maths, DayOfWeek.Monday, 1);
        odd.WeekParity = WeekParity.Odd;
        var even = Lesson(pe, DayOfWeek.Tuesday, 1, colleague);
        even.WeekParity = WeekParity.Even;
        db.SaveChanges();

        Assert.Equal(DropSlotStatus.Free, (await Slot(odd, DayOfWeek.Tuesday, 1)).Status);
    }

    [Fact]
    public async Task AWishAZerothPeriodAndAGapMakeItUnsuitable()
    {
        db.SchoolPeriods.AddRange(Enumerable.Range(0, 5).Select(p => new SchoolPeriod
        {
            SchoolId = SchoolId, PeriodNumber = p, StartTime = new TimeOnly(7 + p, 0), EndTime = new TimeOnly(7 + p, 45),
        }));
        var lesson = Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(pe, DayOfWeek.Tuesday, 1, colleague);
        Lesson(maths, DayOfWeek.Wednesday, 1, of: sixth);
        Lesson(maths, DayOfWeek.Wednesday, 2, of: sixth);
        profile.Availability = [new AvailabilitySlot(DayOfWeek.Friday, 1, AvailabilityKind.Avoid)];
        profile.MaxLessonsPerDay = 2;
        db.SaveChanges();

        Assert.Equal(["Jan Novák v tu dobu raději ne."], (await Slot(lesson, DayOfWeek.Friday, 1)).Reasons);
        Assert.Equal(["Nultá hodina."], (await Slot(lesson, DayOfWeek.Thursday, 0)).Reasons);
        Assert.Equal(["5.A by měla okno."], (await Slot(lesson, DayOfWeek.Tuesday, 3)).Reasons);
        var overDaily = await Slot(lesson, DayOfWeek.Wednesday, 3);
        Assert.Equal(DropSlotStatus.Unsuitable, overDaily.Status);
        Assert.Equal(["Jan Novák by měl víc než 2 h denně."], overDaily.Reasons);
        Assert.Equal(DropSlotStatus.Free, (await Slot(lesson, DayOfWeek.Tuesday, 2)).Status);
    }

    [Fact]
    public async Task GroupsWithoutAPupilInCommonCanShareASlot()
    {
        var a = Group("A", null, teacher, fifth);
        var b = Group("B", null, colleague, fifth);
        var pupil = new StudentProfile { SchoolId = SchoolId, SchoolClassId = fifth.Id, Person = new Person { SchoolId = SchoolId, FirstName = "Eva", LastName = "Malá" } };
        db.Students.Add(pupil);
        db.SaveChanges();
        db.StudentGroupMembers.Add(new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = a.Id, StudentId = pupil.Id });
        db.SaveChanges();
        var lesson = Lesson(pe, DayOfWeek.Monday, 1, group: a);
        Lesson(pe, DayOfWeek.Tuesday, 1, colleague, group: b);

        Assert.Equal(DropSlotStatus.Free, (await Slot(lesson, DayOfWeek.Tuesday, 1)).Status);

        db.StudentGroupMembers.Add(new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = b.Id, StudentId = pupil.Id });
        db.SaveChanges();

        Assert.Equal(DropSlotStatus.Blocked, (await Slot(lesson, DayOfWeek.Tuesday, 1)).Status);
    }

    [Fact]
    public async Task MovingKeepsTheLessonAndItsClassroomWhenFree()
    {
        var lesson = Lesson(pe, DayOfWeek.Monday, 1, colleague, room: smallGym);

        Assert.IsType<NoContentResult>(await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Friday, 3)));

        var moved = db.TimetableEntries.Single();
        Assert.Equal((lesson.Id, DayOfWeek.Friday, 3, (int?)smallGym.Id, false),
            (moved.Id, moved.DayOfWeek, moved.PeriodNumber, moved.ClassroomId, moved.Locked));
    }

    [Fact]
    public async Task MovingToWhereTheClassroomIsTakenFindsAnotherOrNone()
    {
        var lesson = Lesson(pe, DayOfWeek.Monday, 1, colleague, room: smallGym);
        Lesson(pe, DayOfWeek.Friday, 3, teacher, sixth, room: smallGym);

        await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Friday, 3));

        Assert.Equal(gym.Id, lesson.ClassroomId);

        Lesson(pe, DayOfWeek.Thursday, 2, teacher, sixth, room: smallGym);
        Lesson(maths, DayOfWeek.Thursday, 2, of: sixth, room: gym).TeacherId = null;
        db.SaveChanges();

        await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Thursday, 2));

        Assert.Null(lesson.ClassroomId);
    }

    [Fact]
    public async Task MovingToABlockedSlotOrOutOfTheWeekIsRefused()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(pe, DayOfWeek.Tuesday, 1, colleague);

        var refused = Assert.IsType<BadRequestObjectResult>(await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Tuesday, 1)));

        Assert.Equal("5.A má v tu dobu jinou hodinu.", refused.Value);
        Assert.IsType<BadRequestObjectResult>(await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Saturday, 1)));
        Assert.Equal(DayOfWeek.Monday, lesson.DayOfWeek);
    }

    [Fact]
    public async Task ALockedLessonNeitherMovesNorGoesBack()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1, locked: true);

        Assert.IsType<BadRequestObjectResult>(await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Friday, 3)));
        Assert.IsType<BadRequestObjectResult>(await Controller().Unplace(draft.Id, lesson.Id));

        Assert.Equal(DayOfWeek.Monday, db.TimetableEntries.Single().DayOfWeek);
    }

    [Fact]
    public async Task APublishedVersionDoesNotChange()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1);
        draft.Status = TimetableVersionStatus.Published;
        db.SaveChanges();

        Assert.IsType<BadRequestObjectResult>(await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Friday, 3)));
        Assert.IsType<BadRequestObjectResult>(await Controller().Unplace(draft.Id, lesson.Id));
        Assert.IsType<BadRequestObjectResult>(await Controller().Place(draft.Id, new(fifth.Id, pe.Id, DayOfWeek.Friday, 3)));
    }

    [Fact]
    public async Task AGroupsLessonMovesInAllItsClassesAndADivisionsGroupsTogether()
    {
        var division = new GroupDivision { SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Tv", HoursPerWeek = 1, ClassIds = [fifth.Id, sixth.Id] };
        db.GroupDivisions.Add(division);
        db.SaveChanges();
        var girls = Group("Dívky", division, teacher, fifth, sixth);
        var boys = Group("Chlapci", division, colleague, fifth, sixth);
        var lesson = Lesson(pe, DayOfWeek.Monday, 1, group: girls);
        Lesson(pe, DayOfWeek.Monday, 1, of: sixth, group: girls);
        Lesson(pe, DayOfWeek.Monday, 1, colleague, group: boys);
        Lesson(pe, DayOfWeek.Monday, 1, colleague, sixth, group: boys);

        await Controller().Move(draft.Id, lesson.Id, new(DayOfWeek.Wednesday, 2));

        Assert.All(db.TimetableEntries, e => Assert.Equal((DayOfWeek.Wednesday, 2), (e.DayOfWeek, e.PeriodNumber)));

        await Controller().Unplace(draft.Id, lesson.Id);

        Assert.Empty(db.TimetableEntries);
    }

    [Fact]
    public async Task UnplacedAreWhatThePlanMisses()
    {
        Lesson(maths, DayOfWeek.Monday, 1);

        var unplaced = await Unplaced();

        Assert.Equal(["M", "M", "Tv"], unplaced.Select(u => u.SubjectShort).Order());
        Assert.Equal("Jan Novák", unplaced.First(u => u.SubjectShort == "M").TeacherName);
    }

    private async Task<List<UnplacedLessonDto>> Unplaced() =>
        [.. Assert.IsAssignableFrom<IEnumerable<UnplacedLessonDto>>(
            Assert.IsType<OkObjectResult>((await Controller().GetUnplaced(draft.Id, fifth.Id)).Result).Value)];

    [Fact]
    public async Task HalfAnHourIsAnOddWeeksLesson()
    {
        db.CurriculumItems.Single(c => c.SubjectId == maths.Id).HoursPerWeek = 1.5m;
        db.CurriculumItems.Single(c => c.SubjectId == pe.Id).HoursPerWeek = 0;
        db.SaveChanges();

        var unplaced = await Unplaced();

        Assert.Equal([WeekParity.Every, WeekParity.Odd], unplaced.Select(u => u.WeekParity));
    }

    [Fact]
    public async Task PlacingFromTheUnplacedHasTheAssignedTeacherAndAFreeClassroomAndGoesBack()
    {
        Assert.IsType<NoContentResult>(await Controller().Place(draft.Id, new(fifth.Id, pe.Id, DayOfWeek.Monday, 2)));

        var placed = db.TimetableEntries.Single();
        Assert.Equal((colleague.Id, gym.Id, DayOfWeek.Monday, 2), (placed.TeacherId!.Value, placed.ClassroomId!.Value, placed.DayOfWeek, placed.PeriodNumber));
        Assert.DoesNotContain(await Unplaced(), u => u.SubjectId == pe.Id);

        await Controller().Unplace(draft.Id, placed.Id);

        Assert.Contains(await Unplaced(), u => u.SubjectId == pe.Id);
    }

    [Fact]
    public async Task ALessonThePlanDoesNotWantDoesNotGoAmongTheUnplaced()
    {
        var lesson = Lesson(maths, DayOfWeek.Monday, 1, of: sixth);

        var refused = Assert.IsType<BadRequestObjectResult>(await Controller().Unplace(draft.Id, lesson.Id));

        Assert.Contains("Smažte ji křížkem", (string)refused.Value!);
        Assert.Single(db.TimetableEntries);
    }

    [Fact]
    public async Task PlacingWhatThePlanDoesNotMissOrToABlockedSlotIsRefused()
    {
        Lesson(pe, DayOfWeek.Monday, 1, colleague);

        Assert.IsType<BadRequestObjectResult>(await Controller().Place(draft.Id, new(fifth.Id, pe.Id, DayOfWeek.Friday, 2)));
        Assert.IsType<BadRequestObjectResult>(await Controller().Place(draft.Id, new(fifth.Id, maths.Id, DayOfWeek.Monday, 1)));
        Assert.Single(db.TimetableEntries);
    }

    [Fact]
    public async Task PlacingADivisionPlacesAllItsGroupsInTheirClasses()
    {
        var division = new GroupDivision { SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Tv", HoursPerWeek = 1, ClassIds = [fifth.Id] };
        db.GroupDivisions.Add(division);
        db.SaveChanges();
        Group("Dívky", division, teacher, fifth);
        Group("Chlapci", division, colleague, fifth);

        var tile = Assert.Single(await Unplaced(), u => u.GroupId is not null);
        Assert.Equal("Tv", tile.GroupName);
        Assert.Equal("Eva Malá, Jan Novák", tile.TeacherName);

        await Controller().Place(draft.Id, new(fifth.Id, pe.Id, DayOfWeek.Monday, 1, tile.GroupId));

        Assert.Equal(2, db.TimetableEntries.Count());
        Assert.Equal([gym.Id, smallGym.Id], db.TimetableEntries.Select(e => e.ClassroomId!.Value).AsEnumerable().Order());
    }

    [Fact]
    public async Task TheAssessmentHasHowItLooksAndWhatIsWrong()
    {
        Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(maths, DayOfWeek.Monday, 3);

        var assessment = Assert.IsType<TimetableAssessmentDto>((await Controller().GetAssessment(draft.Id)).Value);

        Assert.Contains("Okna tříd: 1 (5.A po).", assessment.Notes);
        Assert.NotEmpty(assessment.Problems);
    }

    [Fact]
    public async Task AnEmptyVersionIsAssessedWithoutNotes()
    {
        var assessment = Assert.IsType<TimetableAssessmentDto>((await Controller().GetAssessment(draft.Id)).Value);

        Assert.Empty(assessment.Notes);
        Assert.Equal(["Verze nemá žádné hodiny."], assessment.Problems);
    }
}
