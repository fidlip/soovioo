using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.ClassBook;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
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

    private FakeClock clock = new(Now);
    private readonly AppDbContext db;

    private readonly SchoolClass classA, classB;
    private readonly Subject maths, czech, music;
    private readonly User novak, mala, kral;
    private readonly StudentGroup choir;
    private readonly StudentProfile jan, eva, petr;
    private readonly User parentEva;

    /// <summary>Monday: 1st maths in 5.A by Novák, Czech in 5.B by Malá; 2nd
    /// Czech in 5.A by Malá; 3rd music of a choir of both classes by Novák.
    /// Malá is třídní of 5.A, Král teaches nothing. Jan and Eva are in 5.A,
    /// Jan in the choir; Petr in 5.B, in the choir too. Eva has a parent.</summary>
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
        jan = Pupil("Jan", "Adámek", classA);
        eva = Pupil("Eva", "Bílá", classA);
        petr = Pupil("Petr", "Cibulka", classB);
        parentEva = new User
        {
            Username = "rodicEva", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = db.Role(UserRole.Parent, SchoolId),
        };
        db.Users.Add(parentEva);
        db.SaveChanges();
        db.LinkChildAsync(parentEva.Id, eva.Id, SchoolId).GetAwaiter().GetResult();
        db.StudentGroupMembers.AddRange(
            new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = choir.Id, StudentId = jan.Id },
            new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = choir.Id, StudentId = petr.Id });
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

    private StudentProfile Pupil(string firstName, string lastName, SchoolClass schoolClass)
    {
        var pupil = new StudentProfile
        {
            Person = new Person { FirstName = firstName, LastName = lastName, SchoolId = SchoolId },
            SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(pupil);
        db.SaveChanges();
        return pupil;
    }

    private ClassBookController Controller(User user) => Controller(TestRoles.PrincipalOf(user));

    private ClassBookController AdminController() => Controller(TestRoles.Principal(db, 900, UserRole.Admin));

    private ClassBookController Controller(System.Security.Claims.ClaimsPrincipal principal) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock, new Notifier(db, clock),
            new MessageAttachmentStorageService(new FakeWebHostEnvironment()), new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)))
        {
            ControllerContext = TestRoles.Context(principal),
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

    private SaveLessonRequest Attendance(int period, params LessonMarkRequest[] marks) =>
        new(Monday, period, classA.Id, null, "", marks);

    private static LessonMarkRequest Absent(StudentProfile pupil) => new(pupil.Id, LessonAttendanceStatus.Absent);

    private async Task<LessonPupilsDto> PupilsAsync(int period, int? classId, int? groupId = null) =>
        Ok(await Controller(novak).GetPupils(Monday, period, classId, groupId));

    private async Task<List<int>?> AbsencePeriodsAsync(StudentProfile pupil) =>
        (await db.Absences.SingleOrDefaultAsync(a => a.StudentId == pupil.Id))?.Periods;

    [Fact]
    public async Task A_lessons_pupils_are_the_classs_or_the_groups()
    {
        Assert.Equal(["Adámek", "Bílá"], (await PupilsAsync(1, classA.Id)).Pupils.Select(p => p.LastName));
        Assert.Equal(["Adámek", "Cibulka"], (await PupilsAsync(3, null, choir.Id)).Pupils.Select(p => p.LastName));
        Assert.False((await PupilsAsync(1, classA.Id)).AttendanceTaken);
        Assert.IsType<ForbidResult>((await Controller(kral).GetPupils(Monday, 1, classA.Id)).Result);
    }

    [Fact]
    public async Task Taking_the_attendance_records_an_absence_only_for_the_absent_and_tells_the_family()
    {
        var saved = Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva), new(jan.Id, LessonAttendanceStatus.Late, 5))));

        Assert.True(saved.AttendanceTaken);
        Assert.Equal(["Jan Adámek", "Eva Bílá"], saved.Marks.Select(m => m.StudentName));
        Assert.Equal(5, saved.Marks[0].Minutes);
        Assert.Equal([1], await AbsencePeriodsAsync(eva));
        Assert.Null(await AbsencePeriodsAsync(jan));
        var notice = Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.Absence));
        Assert.Equal((parentEva.Id, "Zapsaná absence"), (notice.UserId, notice.Title));
        var pupils = await PupilsAsync(1, classA.Id);
        Assert.True(pupils.AttendanceTaken);
        Assert.Equal([LessonAttendanceStatus.Late, LessonAttendanceStatus.Absent], pupils.Pupils.Select(p => p.Status));
        // Without a topic the lesson isn't written yet, but its record stays.
        Assert.Null(saved.Topic);
        Ok(await Controller(novak).SaveLesson(Maths("")));
        Assert.Single(db.LessonRecords);
    }

    [Fact]
    public async Task Each_lesson_adds_its_period_to_the_days_absence_and_a_pupil_there_takes_it_away()
    {
        Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva))));
        Ok(await Controller(mala).SaveLesson(Attendance(2, Absent(eva))));
        Assert.Equal([1, 2], await AbsencePeriodsAsync(eva));

        Ok(await Controller(novak).SaveLesson(Attendance(1)));
        Assert.Equal([2], await AbsencePeriodsAsync(eva));
        var notice = Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.Absence));
        Assert.Equal("Změna absence", notice.Title);
        Assert.EndsWith("hodiny 2", notice.Body);

        Ok(await Controller(mala).SaveLesson(Attendance(2, new LessonMarkRequest(eva.Id, LessonAttendanceStatus.Away))));
        Assert.Empty(db.Absences);
    }

    [Fact]
    public async Task An_omluvenka_in_advance_suggests_the_pupil_away_and_being_there_narrows_its_day()
    {
        var excuse = new Excuse { SchoolId = SchoolId, StudentId = eva.Id, DateFrom = Monday, DateTo = Monday, Reason = "Lékař" };
        db.Excuses.Add(excuse);
        db.Absences.Add(new Absence
        {
            SchoolId = SchoolId, StudentId = eva.Id, Date = Monday, Excuse = excuse, Status = AbsenceStatus.PendingExcuse,
        });
        await db.SaveChangesAsync();

        var suggested = (await PupilsAsync(1, classA.Id)).Pupils.Single(p => p.StudentId == eva.Id);
        Assert.Equal("Omluvenka: Lékař", suggested.Suggestion);
        Assert.Null(suggested.Status);

        // She came for the first lesson after all: the omluvenka is left with her second.
        Ok(await Controller(novak).SaveLesson(Attendance(1)));
        var absence = await db.Absences.SingleAsync();
        Assert.Equal([2], absence.Periods);
        Assert.Equal(AbsenceStatus.PendingExcuse, absence.Status);

        Ok(await Controller(mala).SaveLesson(Attendance(2)));
        Assert.Empty(db.Absences);
        Assert.Empty(db.Excuses);
    }

    [Fact]
    public async Task Once_taken_only_an_omluvenka_still_tells_of_an_absent_pupil()
    {
        var excuse = new Excuse { SchoolId = SchoolId, StudentId = eva.Id, DateFrom = Monday, DateTo = Monday, Reason = "Lékař" };
        db.Excuses.Add(excuse);
        db.Absences.Add(new Absence
        {
            SchoolId = SchoolId, StudentId = eva.Id, Date = Monday, Excuse = excuse, Status = AbsenceStatus.PendingExcuse,
        });
        await db.SaveChangesAsync();
        Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva), Absent(jan))));

        var pupils = (await PupilsAsync(1, classA.Id)).Pupils;

        Assert.Equal("Omluvenka: Lékař", pupils.Single(p => p.StudentId == eva.Id).Suggestion);
        Assert.Null(pupils.Single(p => p.StudentId == jan.Id).Suggestion);
    }

    [Fact]
    public async Task A_pupil_who_left_or_missed_an_earlier_lesson_is_suggested_away()
    {
        Ok(await Controller(novak).SaveLesson(Attendance(1, new LessonMarkRequest(eva.Id, LessonAttendanceStatus.LeftEarly, 20), Absent(jan))));

        var pupils = (await PupilsAsync(2, classA.Id)).Pupils;
        Assert.Equal(["Chyběl(a) v 1. hodině", "Odešel(a) v 1. hodině"], pupils.Select(p => p.Suggestion));
    }

    [Fact]
    public async Task Only_the_lessons_pupils_and_minutes_only_of_coming_late_or_leaving()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller(novak).SaveLesson(Attendance(1, Absent(petr)))).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await Controller(novak).SaveLesson(Attendance(1, new LessonMarkRequest(eva.Id, LessonAttendanceStatus.Absent, 5)))).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await Controller(novak).SaveLesson(Attendance(1, new LessonMarkRequest(eva.Id, LessonAttendanceStatus.Late, 0)))).Result);
        Assert.Empty(db.LessonRecords);
    }

    [Fact]
    public async Task Now_is_the_lesson_going_on_else_the_next_one_today()
    {
        LessonAt(8, 10);
        Assert.Equal(1, Ok(await Controller(novak).GetNow()).PeriodNumber);
        LessonAt(8, 50);
        Assert.Equal(3, Ok(await Controller(novak).GetNow()).PeriodNumber);
        LessonAt(11, 0);
        Assert.IsType<NoContentResult>((await Controller(novak).GetNow()).Result);
        Assert.IsType<NoContentResult>((await Controller(kral).GetNow()).Result);
    }

    /// <summary>Monday, that time in Prague (UTC+1 in November).</summary>
    private void LessonAt(int hour, int minute) => clock = new FakeClock(new DateTimeOffset(2026, 11, 2, hour - 1, minute, 0, TimeSpan.Zero));

    private Excuse PendingExcuse(StudentProfile pupil)
    {
        var excuse = new Excuse { SchoolId = SchoolId, StudentId = pupil.Id, DateFrom = Monday, DateTo = Monday, Reason = "Lékař" };
        db.Excuses.Add(excuse);
        db.Absences.Add(new Absence
        {
            SchoolId = SchoolId, StudentId = pupil.Id, Date = Monday, Excuse = excuse, Status = AbsenceStatus.PendingExcuse,
        });
        db.SaveChanges();
        return excuse;
    }

    private ExcusesController Excuses(User user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, clock), clock,
            new MessageAttachmentStorageService(new FakeWebHostEnvironment()))
        {
            ControllerContext = TestRoles.Context(TestRoles.PrincipalOf(user)),
        };

    private async Task<ExcuseDayDto> ExcuseDayAsync(User user) =>
        Assert.Single(Assert.Single(Ok(await Excuses(user).GetExcuses())).Days);

    [Fact]
    public async Task An_omluvenkas_day_shows_the_pupils_lessons_as_the_trida_kniha_has_them()
    {
        PendingExcuse(eva);
        Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva))));

        var day = await ExcuseDayAsync(mala);

        Assert.Equal(
            [(1, "M", PupilLessonState.Absent), (2, "Čj", PupilLessonState.NotRecorded)],
            day.Lessons!.Select(l => (l.PeriodNumber, l.Subject, l.State)));
        Assert.False(day.ApprovalBlocked);
        Assert.Null((await ExcuseDayAsync(parentEva)).Lessons);
    }

    [Fact]
    public async Task The_school_may_refuse_approving_a_day_before_all_its_lessons_are_written()
    {
        var excuse = PendingExcuse(eva);
        (await db.Schools.SingleAsync()).UnverifiedExcuseApproval = UnverifiedExcuseApproval.Block;
        await db.SaveChangesAsync();
        Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva))));

        Assert.True((await ExcuseDayAsync(mala)).ApprovalBlocked);
        Assert.IsType<BadRequestObjectResult>(await Excuses(mala).ReviewExcuse(excuse.Id, new(true)));
        Assert.Equal(AbsenceStatus.PendingExcuse, (await db.Absences.SingleAsync()).Status);

        Ok(await Controller(mala).SaveLesson(Attendance(2, Absent(eva))));
        Assert.False((await ExcuseDayAsync(mala)).ApprovalBlocked);
        Assert.IsType<NoContentResult>(await Excuses(mala).ReviewExcuse(excuse.Id, new(true)));
        Assert.Equal(AbsenceStatus.Excused, (await db.Absences.SingleAsync()).Status);
    }

    [Fact]
    public async Task By_default_an_unwritten_day_is_approved_the_teacher_only_warned()
    {
        var excuse = PendingExcuse(eva);

        Assert.All((await ExcuseDayAsync(mala)).Lessons!, l => Assert.Equal(PupilLessonState.NotRecorded, l.State));
        Assert.IsType<NoContentResult>(await Excuses(mala).ReviewExcuse(excuse.Id, new(true)));
    }

    [Fact]
    public async Task An_absent_pupils_omluvenka_shows_in_the_trida_kniha()
    {
        PendingExcuse(eva);

        var mark = Assert.Single(Ok(await Controller(novak).SaveLesson(Attendance(1, Absent(eva)))).Marks);

        Assert.Equal(AbsenceStatus.PendingExcuse, mark.ExcuseStatus);
    }
}

