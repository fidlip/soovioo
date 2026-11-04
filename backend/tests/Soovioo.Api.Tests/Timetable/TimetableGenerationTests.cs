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
    public async Task AClassStartingLateBecauseItsTeacherCannotTeachTheFirstPeriodIsExplained()
    {
        var profile = db.TeacherProfiles.Single();
        profile.Availability = [.. TeacherProfile.Weekdays.Select(d => new AvailabilitySlot(d, 1, AvailabilityKind.Unavailable))];
        db.SaveChanges();

        var generation = await Generate(TimetableGenerationMode.All);

        Assert.StartsWith("5.A: první hodinu nemůže mít ani jeden den v týdnu, Jan Novák (6 h z 6 h) ji učit nemůže; začíná proto později ",
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
}
