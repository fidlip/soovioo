using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.ClassBook;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.ClassBook;

public class ClassBookTests
{
    private const int SchoolId = 1;

    // Noon in Prague, a Monday.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private readonly FakeClock clock = new(Now);
    private readonly AppDbContext db;

    private readonly SchoolClass classA, classB;
    private readonly Subject maths, czech, music;
    private readonly User novak, mala, kral;
    private readonly StudentGroup choir;

    /// <summary>Monday: 1st maths in 5.A by Novák, Czech in 5.B by Malá; 2nd
    /// Czech in 5.A by Malá; 3rd music of a choir of both classes by Novák.
    /// Malá is třídní of 5.A, Král teaches nothing.</summary>
    public ClassBookTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = Monday };
        db.WithSchoolYears(SchoolId);
        User Teacher(string name) => new()
        {
            Username = name, PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)],
            ActiveRole = db.Role(UserRole.Teacher), Person = new Person { FirstName = name, LastName = "Učitel", SchoolId = SchoolId },
        };
        novak = Teacher("Novák");
        mala = Teacher("Malá");
        kral = Teacher("Král");
        db.Users.AddRange(novak, mala, kral);
        db.SaveChanges();
        classA = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId, ClassTeacherId = mala.Id };
        classB = new SchoolClass { Name = "5.B", Rocnik = 5, SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        czech = new Subject { Name = "Český jazyk", Abbreviation = "Čj", SchoolId = SchoolId };
        music = new Subject { Name = "Hudební výchova", Abbreviation = "Hv", SchoolId = SchoolId };
        var current = db.CurrentTimetableVersion(SchoolId);
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola" });
        db.AddRange(classA, classB, maths, czech, music);
        db.SaveChanges();
        db.SchoolPeriods.AddRange(Enumerable.Range(1, 6).Select(p => new SchoolPeriod
        {
            SchoolId = SchoolId, PeriodNumber = p, StartTime = new TimeOnly(7 + p, 0), EndTime = new TimeOnly(7 + p, 45),
        }));
        choir = new StudentGroup { SchoolId = SchoolId, Name = "Sbor", SubjectId = music.Id, TimetableVersionId = current.Id };
        db.StudentGroups.Add(choir);
        db.SaveChanges();
        db.StudentGroupClasses.AddRange(
            new StudentGroupClass { SchoolId = SchoolId, StudentGroupId = choir.Id, SchoolClassId = classA.Id },
            new StudentGroupClass { SchoolId = SchoolId, StudentGroupId = choir.Id, SchoolClassId = classB.Id });
        TimetableEntry Lesson(SchoolClass schoolClass, Subject subject, int period, User teacher, StudentGroup? group = null) => new()
        {
            SchoolId = SchoolId, TimetableVersionId = current.Id, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
            DayOfWeek = DayOfWeek.Monday, PeriodNumber = period, TeacherId = teacher.Id, StudentGroup = group,
        };
        db.TimetableEntries.AddRange(
            Lesson(classA, maths, 1, novak),
            Lesson(classB, czech, 1, mala),
            Lesson(classA, czech, 2, mala),
            Lesson(classA, music, 3, novak, choir),
            Lesson(classB, music, 3, novak, choir));
        db.SaveChanges();
    }

    private ClassBookController Controller(User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock) { ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(user)) };

    private ClassBookController AdminController() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<ClassBookDayDto> DayAsync(User user, int? classId, DateOnly? date = null) =>
        Ok(await Controller(user).GetDay(date ?? Monday, classId));

    private SaveLessonRequest Maths(string? topic, DateOnly? date = null) => new(date ?? Monday, 1, classA.Id, null, topic);

    [Fact]
    public async Task A_classs_day_has_its_lessons_and_its_groups_with_times()
    {
        var day = await DayAsync(mala, classA.Id);

        Assert.Equal([1, 2, 3], day.Lessons.Select(l => l.PeriodNumber));
        Assert.Equal(["5.A", "5.A", "Sbor (5.A, 5.B)"], day.Lessons.Select(l => l.Who));
        Assert.Equal(["Matematika", "Český jazyk", "Hudební výchova"], day.Lessons.Select(l => l.SubjectName));
        Assert.Equal(new TimeOnly(8, 0), day.Lessons[0].StartTime);
        Assert.Equal(choir.Id, day.Lessons[2].StudentGroupId);
        Assert.Null(day.Lessons[2].SchoolClassId);
        Assert.All(day.Lessons, l => Assert.Null(l.Topic));
    }

    [Fact]
    public async Task Without_a_class_the_teacher_sees_the_lessons_they_teach_that_day()
    {
        var day = await DayAsync(novak, null);

        Assert.Equal(["5.A", "Sbor (5.A, 5.B)"], day.Lessons.Select(l => l.Who));
        Assert.All(day.Lessons, l => Assert.True(l.CanWrite));
    }

    [Fact]
    public async Task A_teacher_sees_the_classes_they_teach_as_a_whole_or_are_trida_of()
    {
        // Novák teaches 5.B only in the choir.
        Assert.Equal(["5.A"], Ok(await Controller(novak).GetClasses()).Select(c => c.Name));
        Assert.Equal(["5.A", "5.B"], Ok(await Controller(mala).GetClasses()).Select(c => c.Name));
        Assert.Empty(Ok(await Controller(kral).GetClasses()));
        Assert.IsType<ForbidResult>((await Controller(kral).GetDay(Monday, classA.Id)).Result);
    }

    [Fact]
    public async Task The_teacher_writes_the_topic_of_their_lesson_and_an_empty_one_takes_it_back()
    {
        var saved = Ok(await Controller(novak).SaveLesson(Maths("  Zlomky ")));

        Assert.Equal("Zlomky", saved.Topic);
        Assert.Equal("Novák Učitel", saved.RecordedByName);
        Assert.Equal(Now, saved.RecordedAt);
        var record = await db.LessonRecords.SingleAsync();
        Assert.Equal((maths.Id, novak.Id), (record.SubjectId, record.TeacherId));
        Assert.Equal("Zlomky", (await DayAsync(mala, classA.Id)).Lessons[0].Topic);

        Ok(await Controller(novak).SaveLesson(Maths("Desetinná čísla")));
        Assert.Equal("Desetinná čísla", (await db.LessonRecords.SingleAsync()).Topic);

        Assert.Null(Ok(await Controller(novak).SaveLesson(Maths(" "))).Topic);
        Assert.Empty(db.LessonRecords);
    }

    [Fact]
    public async Task A_group_lesson_is_written_once_for_all_its_classes()
    {
        Ok(await Controller(novak).SaveLesson(new(Monday, 3, null, choir.Id, "Koledy")));

        Assert.Equal("Koledy", (await DayAsync(mala, classA.Id)).Lessons[2].Topic);
        Assert.Equal("Koledy", (await DayAsync(mala, classB.Id)).Lessons.Single(l => l.PeriodNumber == 3).Topic);
    }

    [Fact]
    public async Task Another_teachers_lesson_only_the_trida_writes()
    {
        Assert.IsType<ForbidResult>((await Controller(kral).SaveLesson(Maths("Zlomky"))).Result);
        Assert.False((await DayAsync(novak, classA.Id)).Lessons[1].CanWrite);

        Ok(await Controller(mala).SaveLesson(Maths("Zlomky")));
        Assert.True((await DayAsync(mala, classA.Id)).Lessons[0].CanWrite);
        // The choir is of 5.A too.
        Ok(await Controller(mala).SaveLesson(new(Monday, 3, null, choir.Id, "Koledy")));
    }

    [Fact]
    public async Task The_teacher_writes_on_the_lessons_day_and_the_next_the_trida_and_the_school_any_time()
    {
        db.Today = Monday.AddDays(1);
        Ok(await Controller(novak).SaveLesson(Maths("Zlomky")));

        db.Today = Monday.AddDays(2);
        Assert.IsType<BadRequestObjectResult>((await Controller(novak).SaveLesson(Maths("Zlomky 2"))).Result);
        Assert.False((await DayAsync(novak, classA.Id)).Lessons[0].CanWrite);
        Ok(await Controller(mala).SaveLesson(Maths("Zlomky 2")));
        Ok(await AdminController().SaveLesson(new(Monday, 1, classB.Id, null, "Vyjmenovaná slova")));
    }

    [Fact]
    public async Task No_lesson_is_written_before_its_day()
    {
        var nextMonday = Monday.AddDays(7);

        Assert.IsType<BadRequestObjectResult>((await AdminController().SaveLesson(Maths("Zlomky", nextMonday))).Result);
        Assert.All((await DayAsync(novak, classA.Id, nextMonday)).Lessons, l => Assert.False(l.CanWrite));
    }

    [Fact]
    public async Task The_substitute_writes_the_lesson_and_a_cancelled_one_is_not_written()
    {
        db.SubstitutionDays.Add(new SubstitutionDay
        {
            SchoolId = SchoolId, Date = Monday, PublishedAt = Now,
            Changes =
            [
                new(Monday, 1, classA.Id, null, false, false, kral.Id, null, null, null, null, null, null),
                new(Monday, 3, null, choir.Id, false, true, null, null, null, null, null, null, null),
            ],
        });
        await db.SaveChangesAsync();

        var day = await DayAsync(kral, null);
        Assert.Equal("Supl", Assert.Single(day.Lessons).Label);
        Ok(await Controller(kral).SaveLesson(Maths("Zlomky")));
        Assert.Equal(kral.Id, (await db.LessonRecords.SingleAsync()).TeacherId);
        Assert.IsType<ForbidResult>((await Controller(novak).SaveLesson(Maths("Zlomky"))).Result);

        var cancelled = (await DayAsync(mala, classA.Id)).Lessons[2];
        Assert.True(cancelled.Gone);
        Assert.False(cancelled.CanWrite);
        Assert.IsType<NotFoundObjectResult>((await AdminController().SaveLesson(new(Monday, 3, null, choir.Id, "x"))).Result);
    }

    [Fact]
    public async Task A_topic_is_at_most_500_characters()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller(novak).SaveLesson(Maths(new string('a', 501)))).Result);
        Assert.Empty(db.LessonRecords);
    }
}
