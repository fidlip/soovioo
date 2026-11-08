using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableGenerationTests
{
    private const int SchoolId = 1;

    private readonly DbContextOptions<AppDbContext> options =
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private readonly AppDbContext db;

    private readonly TimetableVersion draft;
    private readonly Subject maths, pe;
    private readonly SchoolClass fifth;
    private readonly User teacher;
    private readonly Classroom gym;
    private readonly FakeJobs jobs = new();

    public TimetableGenerationTests()
    {
        db = new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
        gym = new Classroom { Name = "Tělocvična", Type = ClassroomType.Gym, SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        pe = new Subject { Name = "Tělesná výchova", Abbreviation = "Tv", Classrooms = [new() { Type = ClassroomType.Gym }], SchoolId = SchoolId };
        fifth = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        draft = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Od února", ValidFrom = new DateOnly(2027, 2, 1), Status = TimetableVersionStatus.Draft,
        };
        teacher = new User { Username = "novak", PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)] };
        db.AddRange(gym, maths, pe, fifth, draft, teacher);
        db.Persons.Add(new Person
        {
            SchoolId = SchoolId, FirstName = "Jan", LastName = "Novák", User = teacher,
            TeacherProfile = new TeacherProfile
            {
                SchoolId = SchoolId,
                Qualifications = [new() { SchoolId = SchoolId, Subject = maths }, new() { SchoolId = SchoolId, Subject = pe }],
            },
        });
        db.SaveChanges();
        db.CurriculumItems.AddRange(
            new CurriculumItem { SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 5, SubjectId = maths.Id, HoursPerWeek = 4 },
            new CurriculumItem { SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 5, SubjectId = pe.Id, HoursPerWeek = 2 });
        // The few hours of these classes fill a main block of one period.
        db.GradeMainBlocks.AddRange(
            new GradeMainBlock { SchoolId = SchoolId, Grade = 5, From = 1, To = 1 },
            new GradeMainBlock { SchoolId = SchoolId, Grade = 6, From = 1, To = 1 });
        db.SaveChanges();
    }

    private TimetableEntry Lesson(Subject subject, DayOfWeek day, int period, bool locked = false)
    {
        var entry = new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = fifth.Id, SubjectId = subject.Id,
            TeacherId = teacher.Id, DayOfWeek = day, PeriodNumber = period, Locked = locked,
        };
        db.TimetableEntries.Add(entry);
        db.SaveChanges();
        return entry;
    }

    private async Task<TimetableGeneration> Generate(TimetableGenerationMode mode)
    {
        var generation = new TimetableGeneration { SchoolId = SchoolId, TimetableVersionId = draft.Id, Mode = mode };
        db.TimetableGenerations.Add(generation);
        await TimetableGenerationJob.GenerateAsync(db, generation, TimeSpan.FromSeconds(5));
        await db.SaveChangesAsync();
        return generation;
    }

    private TimetableGenerationController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), jobs, new FakeClock(new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero)))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    [Fact]
    public async Task FillingAddsWhatThePlanMissesWithTheAssignedTeacherAndAClassroomOfTheType()
    {
        var monday = Lesson(maths, DayOfWeek.Monday, 1);

        var generation = await Generate(TimetableGenerationMode.Fill);

        Assert.Equal(5, generation.Placed);
        Assert.Empty(generation.Messages);
        var lessons = db.TimetableEntries.Where(e => e.TimetableVersionId == draft.Id).ToList();
        Assert.Contains(monday, lessons);
        Assert.Equal(4, lessons.Count(e => e.SubjectId == maths.Id));
        Assert.All(lessons, e => Assert.Equal(teacher.Id, e.TeacherId));
        Assert.All(lessons.Where(e => e.SubjectId == pe.Id), e => Assert.Equal(gym.Id, e.ClassroomId));
    }

    [Fact]
    public async Task AClassBiggerThanTheClassroomGetsNone()
    {
        gym.Capacity = 1;
        db.Students.AddRange(
            new StudentProfile { SchoolId = SchoolId, SchoolClassId = fifth.Id, Person = new Person { SchoolId = SchoolId, FirstName = "Eva", LastName = "Malá" } },
            new StudentProfile { SchoolId = SchoolId, SchoolClassId = fifth.Id, Person = new Person { SchoolId = SchoolId, FirstName = "Ota", LastName = "Malý" } });
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Equal("5.A Tv: počet žáků 2 je větší než kapacita učeben předmětu (Tělocvična 1), hodiny jsou bez učebny.",
            Assert.Single(generation.Messages));
        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == pe.Id), e => Assert.Null(e.ClassroomId));
        Assert.Contains("Třídy nemají okna.", generation.Notes);
    }

    private Classroom Homeroom(string name, SchoolClass? of = null)
    {
        var room = new Classroom { Name = name, Type = ClassroomType.Homeroom, SchoolId = SchoolId };
        db.Classrooms.Add(room);
        if (of is not null)
        {
            of.Homeroom = room;
        }
        db.SaveChanges();
        return room;
    }

    [Fact]
    public async Task ASubjectWithoutAListHasTheClassesHomeroom()
    {
        Homeroom("100");
        var own = Homeroom("101", fifth);

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Empty(generation.Messages);
        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == maths.Id), e => Assert.Equal(own.Id, e.ClassroomId));
        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == pe.Id), e => Assert.Equal(gym.Id, e.ClassroomId));
    }

    [Fact]
    public async Task ATakenHomeroomLeavesTheLessonWithoutAClassroomAndSaysSo()
    {
        var own = Homeroom("101", fifth);
        var other = new SchoolClass { Name = "6.A", Rocnik = 6, SchoolId = SchoolId };
        db.SchoolClasses.Add(other);
        db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = SchoolId, PeriodNumber = 1, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 45) });
        db.CurriculumItems.RemoveRange(db.CurriculumItems.Where(c => c.SubjectId == pe.Id));
        db.SaveChanges();
        // Four lessons of maths and only three free days in the classroom.
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday })
        {
            db.TimetableEntries.Add(new TimetableEntry
            {
                SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = other.Id, SubjectId = pe.Id,
                DayOfWeek = day, PeriodNumber = 1, ClassroomId = own.Id, Locked = true,
            });
        }
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        var lessons = db.TimetableEntries.Where(e => e.SchoolClassId == fifth.Id).ToList();
        Assert.Equal(4, lessons.Count);
        Assert.Equal(1, lessons.Count(e => e.ClassroomId is null));
        Assert.Contains("M: nedostatek učeben, 1 h bez učebny (5.A) — doplňte ji ručně.", generation.Messages);
    }

    [Fact]
    public async Task AnyHomeroomOnASubjectMeansTheClassesOwn()
    {
        Homeroom("100");
        var own = Homeroom("101", fifth);
        maths.Classrooms = [new() { Type = ClassroomType.Homeroom }];
        db.SaveChanges();

        await Generate(TimetableGenerationMode.All);

        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == maths.Id), e => Assert.Equal(own.Id, e.ClassroomId));
    }

    [Fact]
    public async Task AnyHomeroomOnASubjectInAClassWithoutOneIsReported()
    {
        Homeroom("100");
        maths.Classrooms = [new() { Type = ClassroomType.Homeroom }];
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Equal("5.A M: třída nemá kmenovou učebnu, hodiny jsou bez učebny.", Assert.Single(generation.Messages));
        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == maths.Id), e => Assert.Null(e.ClassroomId));
    }

    [Fact]
    public async Task RegeneratingKeepsOnlyTheLockedLessons()
    {
        var locked = Lesson(maths, DayOfWeek.Friday, 3, locked: true);
        var other = Lesson(maths, DayOfWeek.Friday, 4);

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Equal(5, generation.Placed);
        var lessons = db.TimetableEntries.Where(e => e.TimetableVersionId == draft.Id).ToList();
        Assert.Equal(6, lessons.Count);
        Assert.Contains(locked, lessons);
        Assert.DoesNotContain(other, lessons);
    }

    [Fact]
    public async Task OnlyADraftAndOneRunAtATime()
    {
        var result = await Controller().Generate(draft.Id, new(TimetableGenerationMode.Fill));

        var started = Assert.IsType<TimetableGenerationDto>(Assert.IsType<AcceptedResult>(result.Result).Value);
        Assert.Equal(TimetableGenerationStatus.Queued, started.Status);
        Assert.Single(jobs.Created);
        Assert.IsType<ConflictObjectResult>((await Controller().Generate(draft.Id, new(TimetableGenerationMode.All))).Result);
        Assert.Equal(started.Id, Assert.IsType<TimetableGenerationDto>(
            Assert.IsType<OkObjectResult>((await Controller().GetGeneration(draft.Id)).Result).Value).Id);

        draft.Status = TimetableVersionStatus.Published;
        db.SaveChanges();
        Assert.IsType<BadRequestObjectResult>((await Controller().Generate(draft.Id, new(TimetableGenerationMode.Fill))).Result);
    }

    [Fact]
    public async Task ARunCutOffByARestartFailsAndANewOneCanStart()
    {
        db.TimetableGenerations.Add(new TimetableGeneration
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Mode = TimetableGenerationMode.All,
            Status = TimetableGenerationStatus.Running, CreatedAt = new DateTimeOffset(2026, 11, 3, 10, 40, 0, TimeSpan.Zero),
            StartedAt = new DateTimeOffset(2026, 11, 3, 10, 40, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        var latest = Assert.IsType<TimetableGenerationDto>(
            Assert.IsType<OkObjectResult>((await Controller().GetGeneration(draft.Id)).Result).Value);

        Assert.Equal(TimetableGenerationStatus.Failed, latest.Status);
        Assert.IsType<AcceptedResult>((await Controller().Generate(draft.Id, new(TimetableGenerationMode.All))).Result);
    }

    [Fact]
    public async Task WhoStartedItHearsHowItEnded()
    {
        var generation = new TimetableGeneration
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Mode = TimetableGenerationMode.All,
            Status = TimetableGenerationStatus.Queued, CreatedByUserId = teacher.Id,
        };
        db.TimetableGenerations.Add(generation);
        await db.SaveChangesAsync();

        await new TimetableGenerationJob(options, NullLogger<TimetableGenerationJob>.Instance, TimeProvider.System)
            .RunAsync(generation.Id, SchoolId);

        db.ChangeTracker.Clear();
        Assert.Equal(TimetableGenerationStatus.Done, db.TimetableGenerations.Single().Status);
        var notification = db.Notifications.Single();
        Assert.Equal((teacher.Id, NotificationType.Timetable, "timetable", draft.Id),
            (notification.UserId, notification.Type, notification.Page, notification.TargetId));
        Assert.Equal("Rozvrh „Od února“ je vygenerovaný", notification.Title);
    }

    private class FakeJobs : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add(job);
            return Created.Count.ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    [Fact]
    public async Task TooFewLessonsForTheMainBlockAreReportedAndTheDaysItIsNotFull()
    {
        var block = db.GradeMainBlocks.Single(g => g.Grade == 5);
        block.To = 2;
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Equal(
            "5.A: hodin pro celou třídu je 6 týdně, hlavní výukový blok (1.–2. hodina) jich potřebuje 10. Upravte blok třídy nebo ročníku.",
            Assert.Single(generation.Messages));
        // 6 lessons fill the block one day, the other four days have one of two.
        Assert.Contains(generation.Notes, n => n.StartsWith("Hlavní výukový blok není plný: ") && n.Split(',').Length == 4);
        Assert.Equal(6, db.TimetableEntries.Count(e => e.PeriodNumber <= 2));
    }

    [Fact]
    public async Task AFullMainBlockIsNoted()
    {
        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Contains("Všechny třídy mají plný hlavní výukový blok.", generation.Notes);
    }

    [Fact]
    public async Task AMainBlockStartingLateBecauseTheTeacherCannotTeachItsFirstPeriodIsExplained()
    {
        var profile = db.TeacherProfiles.Single();
        profile.Availability = [.. TeacherProfile.Weekdays.Select(d => new AvailabilitySlot(d, 1, AvailabilityKind.Unavailable))];
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Equal("5.A: 1. hodinu nemůže mít ani jeden den v týdnu, Jan Novák (6 h z 6 h) ji učit nemůže; hlavní výukový blok proto začíná později 5 dní.",
            Assert.Single(generation.Messages, m => m.StartsWith("5.A")));
        Assert.DoesNotContain(db.TimetableEntries, e => e.PeriodNumber == 1);
    }

    [Fact]
    public async Task TheSchoolsWishesApplyAndWhatIsNotMetIsNoted()
    {
        // 6 lessons of one teacher in a week of 5 days: at most 1 a day can't hold.
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola", TeacherMaxLessonsPerDay = 1 });
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.Contains("Nesplněná přání učitelů: Jan Novák (nejvýš 1 h denně).", generation.Notes);
    }

    [Fact]
    public async Task ATeachersOwnWishReplacesTheSchools()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola", TeacherMaxLessonsPerDay = 1 });
        db.TeacherProfiles.Single().MaxLessonsPerDay = 2;
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.DoesNotContain(generation.Notes, n => n.StartsWith("Nesplněná přání"));
        Assert.All(db.TimetableEntries.AsEnumerable().GroupBy(e => e.DayOfWeek).ToList(), d => Assert.InRange(d.Count(), 1, 2));
    }

    [Fact]
    public async Task ASubjectInBlocksGetsTwoLessonsInARow()
    {
        maths.DoubleLessons = true;
        db.SaveChanges();

        await Generate(TimetableGenerationMode.All);

        var days = db.TimetableEntries.Where(e => e.SubjectId == maths.Id).AsEnumerable().GroupBy(e => e.DayOfWeek).ToList();
        Assert.Equal(2, days.Count);
        Assert.All(days, d => Assert.Equal(1, Math.Abs(d.First().PeriodNumber - d.Last().PeriodNumber)));
    }

    private async Task<TimetableGeneration> Update(
        TimetableUpdateMoves moves = TimetableUpdateMoves.Fewest,
        TimetableUpdateMismatches mismatches = TimetableUpdateMismatches.FollowPlan)
    {
        var generation = new TimetableGeneration
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Mode = TimetableGenerationMode.Update,
            Moves = moves, Mismatches = mismatches,
        };
        db.TimetableGenerations.Add(generation);
        await TimetableGenerationJob.GenerateAsync(db, generation, TimeSpan.FromSeconds(5));
        await db.SaveChangesAsync();
        return generation;
    }

    private List<(int Id, DayOfWeek Day, int Period)> Places() => db.TimetableEntries
        .OrderBy(e => e.Id).Select(e => new { e.Id, e.DayOfWeek, e.PeriodNumber }).AsEnumerable()
        .Select(e => (e.Id, e.DayOfWeek, e.PeriodNumber)).ToList();

    private User SecondTeacher()
    {
        var other = new User { Username = "mala", PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)] };
        db.Users.Add(other);
        db.Persons.Add(new Person { SchoolId = SchoolId, FirstName = "Eva", LastName = "Malá", User = other, TeacherProfile = new TeacherProfile { SchoolId = SchoolId } });
        db.SaveChanges();
        return other;
    }

    private void Assign(Subject subject, User who)
    {
        db.TeachingAssignments.Add(new TeachingAssignment
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = fifth.Id, SubjectId = subject.Id, TeacherId = who.Id,
        });
        db.SaveChanges();
    }

    private void PlanHours(Subject subject, int hours)
    {
        db.CurriculumItems.Single(c => c.SubjectId == subject.Id).HoursPerWeek = hours;
        db.SaveChanges();
    }

    private void OnlyPeriods(params int[] periods)
    {
        foreach (var period in periods)
        {
            db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = SchoolId, PeriodNumber = period, StartTime = new TimeOnly(7 + period, 0), EndTime = new TimeOnly(7 + period, 45) });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task UpdatingATimetableInLineWithThePlanChangesNothing()
    {
        await Generate(TimetableGenerationMode.All);
        var before = Places();

        var generation = await Update();

        Assert.Equal(before, Places());
        Assert.Equal(0, generation.Placed);
        Assert.Contains("V rozvrhu se nic nezměnilo.", generation.Notes);
    }

    [Fact]
    public async Task AddedHoursArePlacedAndTheOthersStay()
    {
        await Generate(TimetableGenerationMode.All);
        var before = Places();
        PlanHours(maths, 5);

        var generation = await Update(TimetableUpdateMoves.AffectedOnly);

        Assert.Equal(7, Places().Count);
        Assert.All(before, place => Assert.Contains(place, Places()));
        Assert.Contains("Přidáno 1 h (5.A M).", generation.Notes);
    }

    [Theory]
    [InlineData(TimetableUpdateMoves.Fewest)]
    [InlineData(TimetableUpdateMoves.AffectedOnly)]
    public async Task ExtraHoursGoWhenThePlanDecides(TimetableUpdateMoves moves)
    {
        await Generate(TimetableGenerationMode.All);
        PlanHours(maths, 2);

        var generation = await Update(moves);

        Assert.Equal(2, db.TimetableEntries.Count(e => e.SubjectId == maths.Id));
        Assert.Equal(2, db.TimetableEntries.Count(e => e.SubjectId == pe.Id));
        Assert.Contains("Odebráno 2 h (5.A M).", generation.Notes);
    }

    [Fact]
    public async Task ExtraHoursStayAndAreReportedWhenLeft()
    {
        await Generate(TimetableGenerationMode.All);
        var before = Places();
        PlanHours(maths, 2);

        var generation = await Update(mismatches: TimetableUpdateMismatches.Leave);

        Assert.Equal(before, Places());
        Assert.Contains("5.A M: o 2 h víc, než je v plánu, ponecháno.", generation.Messages);
    }

    [Fact]
    public async Task ASubjectOutOfThePlanGoesOrIsReported()
    {
        await Generate(TimetableGenerationMode.All);
        db.CurriculumItems.RemoveRange(db.CurriculumItems.Where(c => c.SubjectId == pe.Id));
        db.SaveChanges();

        var left = await Update(mismatches: TimetableUpdateMismatches.Leave);

        Assert.Contains("5.A Tv: není v plánu (2 h), ponecháno.", left.Messages);
        Assert.Equal(2, db.TimetableEntries.Count(e => e.SubjectId == pe.Id));

        var gone = await Update();

        Assert.Equal(0, db.TimetableEntries.Count(e => e.SubjectId == pe.Id));
        Assert.Contains("Odebráno 2 h (5.A Tv).", gone.Notes);
    }

    [Theory]
    [InlineData(TimetableUpdateMoves.Fewest)]
    [InlineData(TimetableUpdateMoves.AffectedOnly)]
    public async Task AnotherAssignedTeacherTakesTheLessonsWhereTheyAre(TimetableUpdateMoves moves)
    {
        await Generate(TimetableGenerationMode.All);
        var before = Places();
        var other = SecondTeacher();
        Assign(maths, other);

        var generation = await Update(moves);

        Assert.Equal(before, Places());
        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == maths.Id), e => Assert.Equal(other.Id, e.TeacherId));
        Assert.Contains("Změněn učitel: 4 h (5.A M).", generation.Notes);
    }

    [Fact]
    public async Task AnotherTeacherStaysAndIsReportedWhenLeft()
    {
        await Generate(TimetableGenerationMode.All);
        var other = SecondTeacher();
        Assign(maths, other);

        var generation = await Update(mismatches: TimetableUpdateMismatches.Leave);

        Assert.All(db.TimetableEntries.Where(e => e.SubjectId == maths.Id), e => Assert.Equal(teacher.Id, e.TeacherId));
        Assert.Contains("5.A M: učí Jan Novák, přiděleno je Eva Malá (4 h), ponecháno.", generation.Messages);
    }

    [Theory]
    [InlineData(TimetableUpdateMoves.Fewest, TimetableUpdateMismatches.FollowPlan)]
    [InlineData(TimetableUpdateMoves.AffectedOnly, TimetableUpdateMismatches.Leave)]
    public async Task ALessonWhenItsTeacherCannotMovesWhateverIsChosen(TimetableUpdateMoves moves, TimetableUpdateMismatches mismatches)
    {
        var monday = Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(maths, DayOfWeek.Tuesday, 1);
        Lesson(maths, DayOfWeek.Wednesday, 1);
        Lesson(maths, DayOfWeek.Thursday, 1);
        Assign(maths, teacher);
        PlanHours(pe, 0);
        db.TeacherProfiles.Single().Availability = [new AvailabilitySlot(DayOfWeek.Monday, 1, AvailabilityKind.Unavailable)];
        db.SaveChanges();

        var generation = await Update(moves, mismatches);

        Assert.DoesNotContain(db.TimetableEntries, e => e.DayOfWeek == DayOfWeek.Monday && e.PeriodNumber == 1);
        Assert.Equal(4, db.TimetableEntries.Count(e => e.SubjectId == maths.Id));
        Assert.Contains("Přesunuto 1 h (5.A M).", generation.Notes);
    }

    [Fact]
    public async Task ALockedLessonNeverChanges()
    {
        var locked = Lesson(maths, DayOfWeek.Monday, 1, locked: true);
        var other = SecondTeacher();
        Assign(maths, other);
        PlanHours(pe, 0);
        PlanHours(maths, 1);
        db.TeacherProfiles.First().Availability = [new AvailabilitySlot(DayOfWeek.Monday, 1, AvailabilityKind.Unavailable)];
        db.SaveChanges();

        await Update();

        var only = Assert.Single(db.TimetableEntries);
        Assert.Equal((locked.Id, DayOfWeek.Monday, 1, teacher.Id), (only.Id, only.DayOfWeek, only.PeriodNumber, only.TeacherId!.Value));
    }

    [Fact]
    public async Task FewestMovesShiftsAnotherLessonInAFullWeekOnlyAffectedDoesNot()
    {
        // One period a day and all five taken: M has to swap with Tv.
        OnlyPeriods(1);
        var other = SecondTeacher();
        Assign(maths, teacher);
        Assign(pe, other);
        PlanHours(maths, 3);
        foreach (var (subject, day) in new[] { (maths, DayOfWeek.Monday), (maths, DayOfWeek.Tuesday), (maths, DayOfWeek.Wednesday), (pe, DayOfWeek.Thursday), (pe, DayOfWeek.Friday) })
        {
            var entry = Lesson(subject, day, 1);
            entry.TeacherId = subject == maths ? teacher.Id : other.Id;
            entry.ClassroomId = subject == pe ? gym.Id : null;
        }
        db.TeacherProfiles.Single(t => t.Person!.User == teacher).Availability = [new AvailabilitySlot(DayOfWeek.Monday, 1, AvailabilityKind.Unavailable)];
        db.SaveChanges();

        var only = await Update(TimetableUpdateMoves.AffectedOnly);

        Assert.Contains("5.A M: nevešlo se 1 h.", only.Messages);
        Assert.Equal(2, db.TimetableEntries.Count(e => e.SubjectId == maths.Id));
        Assert.Equal(new[] { DayOfWeek.Thursday, DayOfWeek.Friday },
            db.TimetableEntries.Where(e => e.SubjectId == pe.Id).Select(e => e.DayOfWeek).AsEnumerable().Order().ToArray());

        // What the first run took out is back with the swap.
        var fewest = await Update();

        Assert.DoesNotContain(fewest.Messages, m => m.Contains("nevešlo"));
        Assert.Equal(3, db.TimetableEntries.Count(e => e.SubjectId == maths.Id));
        Assert.Equal(2, db.TimetableEntries.Count(e => e.SubjectId == pe.Id));
        Assert.DoesNotContain(db.TimetableEntries, e => e.SubjectId == maths.Id && e.DayOfWeek == DayOfWeek.Monday);
    }

    [Fact]
    public async Task WhatIsMissingIsFilledInWithTheLessonInPlaceEvenWhenLeft()
    {
        PlanHours(maths, 1);
        PlanHours(pe, 1);
        var other = SecondTeacher();
        Assign(maths, other);
        Assign(pe, other);
        var withoutTeacher = Lesson(maths, DayOfWeek.Monday, 1);
        withoutTeacher.TeacherId = null;
        var withoutRoom = Lesson(pe, DayOfWeek.Tuesday, 1);
        withoutRoom.TeacherId = other.Id;
        db.SaveChanges();

        var generation = await Update(TimetableUpdateMoves.AffectedOnly, TimetableUpdateMismatches.Leave);

        var lessons = db.TimetableEntries.OrderBy(e => e.DayOfWeek).ToList();
        Assert.Equal((withoutTeacher.Id, other.Id), (lessons[0].Id, lessons[0].TeacherId!.Value));
        Assert.Equal((withoutRoom.Id, (int?)gym.Id), (lessons[1].Id, lessons[1].ClassroomId));
        Assert.Contains("Změněn učitel: 1 h (5.A M).", generation.Notes);
        Assert.Contains("Změněna učebna: 1 h (5.A Tv).", generation.Notes);
    }

    [Fact]
    public async Task AClassroomOutOfTheSubjectsListIsReplacedOrReported()
    {
        PlanHours(maths, 0);
        PlanHours(pe, 1);
        var elsewhere = new Classroom { Name = "Aula", Type = ClassroomType.Specialized, SchoolId = SchoolId };
        db.Classrooms.Add(elsewhere);
        db.SaveChanges();
        var lesson = Lesson(pe, DayOfWeek.Monday, 1);
        lesson.ClassroomId = elsewhere.Id;
        db.SaveChanges();

        var left = await Update(mismatches: TimetableUpdateMismatches.Leave);

        Assert.Equal(elsewhere.Id, lesson.ClassroomId);
        Assert.Contains("5.A Tv: učebna Aula není mezi učebnami předmětu (1 h), ponecháno.", left.Messages);

        await Update();

        Assert.Equal(gym.Id, db.TimetableEntries.Single().ClassroomId);
    }

    [Fact]
    public async Task ALessonStayingKeepsItsClassroomThoughAnotherComesFirst()
    {
        PlanHours(maths, 0);
        PlanHours(pe, 1);
        var second = new Classroom { Name = "Malá tělocvična", Type = ClassroomType.Gym, SchoolId = SchoolId };
        db.Classrooms.Add(second);
        db.SaveChanges();
        var lesson = Lesson(pe, DayOfWeek.Monday, 1);
        lesson.ClassroomId = second.Id;
        db.SaveChanges();

        await Update();

        Assert.Equal(second.Id, db.TimetableEntries.Single().ClassroomId);
    }

    [Fact]
    public async Task OnlyAffectedMovesALessonTheTeacherWouldRatherNotHaveAndOverTheirMostADay()
    {
        PlanHours(pe, 0);
        Lesson(maths, DayOfWeek.Monday, 1);
        Lesson(maths, DayOfWeek.Tuesday, 1);
        Lesson(maths, DayOfWeek.Tuesday, 2);
        Lesson(maths, DayOfWeek.Tuesday, 3);
        var profile = db.TeacherProfiles.Single();
        profile.Availability = [new AvailabilitySlot(DayOfWeek.Monday, 1, AvailabilityKind.Avoid)];
        profile.MaxLessonsPerDay = 2;
        db.SaveChanges();

        var generation = await Update(TimetableUpdateMoves.AffectedOnly);

        Assert.DoesNotContain(db.TimetableEntries, e => e.DayOfWeek == DayOfWeek.Monday && e.PeriodNumber == 1);
        Assert.DoesNotContain(db.TimetableEntries, e => e.DayOfWeek == DayOfWeek.Tuesday && e.PeriodNumber == 3);
        Assert.Contains("Přesunuto 2 h (5.A M).", generation.Notes);
        Assert.Equal(4, db.TimetableEntries.Count());
    }

    [Fact]
    public async Task ABlockStaysInPlaceAndASplitOneComesTogether()
    {
        maths.DoubleLessons = true;
        PlanHours(pe, 0);
        db.SaveChanges();
        await Generate(TimetableGenerationMode.All);
        var before = Places();

        await Update();

        Assert.Equal(before, Places());

        var entry = db.TimetableEntries.OrderBy(e => e.DayOfWeek).ThenBy(e => e.PeriodNumber).First();
        entry.DayOfWeek = DayOfWeek.Friday;
        entry.PeriodNumber = 6;
        db.SaveChanges();

        await Update();

        var days = db.TimetableEntries.AsEnumerable().GroupBy(e => e.DayOfWeek).ToList();
        Assert.Equal(2, days.Count);
        Assert.All(days, d => Assert.Equal(1, Math.Abs(d.First().PeriodNumber - d.Last().PeriodNumber)));
    }

    [Fact]
    public async Task LessonsLeftFixedOnAFullDayDoNotStopTheRest()
    {
        PlanHours(maths, 6);
        PlanHours(pe, 1);
        foreach (var period in Enumerable.Range(1, 6))
        {
            Lesson(maths, DayOfWeek.Monday, period);
        }

        var generation = await Update(TimetableUpdateMoves.AffectedOnly);

        Assert.Equal(1, generation.Placed);
        Assert.Equal(1, db.TimetableEntries.Count(e => e.SubjectId == pe.Id));
        Assert.Equal(6, db.TimetableEntries.Count(e => e.SubjectId == maths.Id));
    }

    [Fact]
    public async Task AGroupsLessonChangesTeacherInEachOfItsClassesInPlace()
    {
        var other = SecondTeacher();
        var sixth = new SchoolClass { Name = "6.A", Rocnik = 6, SchoolId = SchoolId };
        db.SchoolClasses.Add(sixth);
        db.CurriculumItems.RemoveRange(db.CurriculumItems);
        db.SaveChanges();
        var group = new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Němčina", SubjectId = maths.Id, HoursPerWeek = 1, TeacherId = other.Id,
            Classes = [new() { SchoolId = SchoolId, SchoolClassId = fifth.Id }, new() { SchoolId = SchoolId, SchoolClassId = sixth.Id }],
        };
        db.StudentGroups.Add(group);
        db.SaveChanges();
        foreach (var classId in new[] { fifth.Id, sixth.Id })
        {
            db.TimetableEntries.Add(new TimetableEntry
            {
                SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = classId, StudentGroupId = group.Id, SubjectId = maths.Id,
                TeacherId = teacher.Id, DayOfWeek = DayOfWeek.Wednesday, PeriodNumber = 1,
            });
        }
        db.SaveChanges();
        var before = Places();

        var generation = await Update();

        Assert.Equal(before, Places());
        Assert.All(db.TimetableEntries, e => Assert.Equal(other.Id, e.TeacherId));
        Assert.Contains("Změněn učitel: 1 h (Němčina).", generation.Notes);
    }

    [Fact]
    public async Task TheUpdatesChoicesAreKeptWithTheRun()
    {
        await Controller().Generate(draft.Id, new(TimetableGenerationMode.Update, TimetableUpdateMoves.AffectedOnly, TimetableUpdateMismatches.Leave));

        var run = db.TimetableGenerations.Single();
        Assert.Equal(
            (TimetableGenerationMode.Update, TimetableUpdateMoves.AffectedOnly, TimetableUpdateMismatches.Leave),
            (run.Mode, run.Moves, run.Mismatches));
    }

    private StudentGroup Group(SchoolClass of, int pupils, GroupDivision? division = null, string name = "Seminář")
    {
        var group = new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = name, SubjectId = maths.Id, HoursPerWeek = 1, TeacherId = teacher.Id,
            GroupDivision = division,
            Classes = [new() { SchoolId = SchoolId, SchoolClassId = of.Id }],
            Members = [.. db.Students.Where(s => s.SchoolClassId == of.Id).OrderBy(s => s.Id).Take(pupils).AsEnumerable()
                .Select(s => new StudentGroupMember { SchoolId = SchoolId, StudentId = s.Id })],
        };
        db.StudentGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    private void Pupils(SchoolClass of, int count)
    {
        foreach (var i in Enumerable.Range(0, count))
        {
            db.Students.Add(new StudentProfile { SchoolId = SchoolId, SchoolClassId = of.Id, Person = new Person { SchoolId = SchoolId, FirstName = $"Žák{i}", LastName = "Malý" } });
        }
        db.SaveChanges();
    }

    /// <summary>Locked: M in the 1st and 3rd period on Monday, the group's in the 2nd.</summary>
    private async Task<TimetableGeneration> GeneratedWithLockedGroupLessonInside(StudentGroup group)
    {
        PlanHours(pe, 0);
        PlanHours(maths, 2);
        Lesson(maths, DayOfWeek.Monday, 1, locked: true);
        Lesson(maths, DayOfWeek.Monday, 3, locked: true);
        var lesson = Lesson(maths, DayOfWeek.Monday, 2, locked: true);
        lesson.StudentGroupId = group.Id;
        db.SaveChanges();
        return await Generate(TimetableGenerationMode.Fill);
    }

    private const string InsideNote = "Volitelné předměty uprostřed dne: 5.A po.";

    [Fact]
    public async Task AGroupOfSomePupilsLeftInsideTheDayIsNoted()
    {
        Pupils(fifth, 2);

        var generation = await GeneratedWithLockedGroupLessonInside(Group(fifth, 1));

        Assert.Contains(InsideNote, generation.Notes);
    }

    [Fact]
    public async Task AGroupWithNoPupilsYetCounts()
    {
        Pupils(fifth, 2);

        var generation = await GeneratedWithLockedGroupLessonInside(Group(fifth, 0));

        Assert.Contains(InsideNote, generation.Notes);
    }

    [Fact]
    public async Task AGroupOfTheWholeClassIsNotElective()
    {
        Pupils(fifth, 2);

        var generation = await GeneratedWithLockedGroupLessonInside(Group(fifth, 2));

        Assert.DoesNotContain(generation.Notes, n => n.StartsWith("Volitelné"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    public async Task ADivisionIsElectiveWhenSomePupilsAreInNoneOfItsGroups(int pupils, bool elective)
    {
        Pupils(fifth, 2);
        var division = new GroupDivision { SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Jazyky", HoursPerWeek = 1, ClassIds = [fifth.Id] };
        db.GroupDivisions.Add(division);
        db.SaveChanges();

        var generation = await GeneratedWithLockedGroupLessonInside(Group(fifth, pupils, division));

        Assert.Equal(elective, generation.Notes.Contains(InsideNote));
    }

    [Fact]
    public async Task AnElectiveGroupGoesToTheEdgeOfTheDayEvenAgainstItsTeachersWish()
    {
        Pupils(fifth, 2);
        PlanHours(pe, 0);
        PlanHours(maths, 19);
        var other = SecondTeacher();
        Assign(maths, teacher);
        var seminar = Group(fifth, 1);
        seminar.TeacherId = other.Id;
        seminar.SubjectId = pe.Id;
        db.TeacherProfiles.Single(t => t.Person!.User == other).Availability =
            [.. TeacherProfile.Weekdays.SelectMany(d => new[] { new AvailabilitySlot(d, 1, AvailabilityKind.Avoid), new AvailabilitySlot(d, 4, AvailabilityKind.Avoid) })];
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        var lesson = db.TimetableEntries.Single(e => e.StudentGroupId == seminar.Id);
        var forAll = db.TimetableEntries.Where(e => e.StudentGroupId == null && e.DayOfWeek == lesson.DayOfWeek).Select(e => e.PeriodNumber).ToList();
        Assert.True(lesson.PeriodNumber < forAll.Min() || lesson.PeriodNumber > forAll.Max());
        Assert.DoesNotContain(generation.Notes, n => n.StartsWith("Volitelné"));
    }
}
