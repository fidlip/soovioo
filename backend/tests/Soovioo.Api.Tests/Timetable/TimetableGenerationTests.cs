using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableGenerationTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly TimetableVersion draft;
    private readonly Subject maths, pe;
    private readonly SchoolClass fifth;
    private readonly User teacher;
    private readonly Classroom gym;
    private readonly FakeJobs jobs = new();

    public TimetableGenerationTests()
    {
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
}
