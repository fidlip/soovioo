using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Dashboard;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class SubstitutionsTests
{
    private const int SchoolId = 1;

    // Noon in Prague, a Monday.
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private readonly FakeClock clock = new(Now);
    private readonly AppDbContext db;

    private readonly SchoolClass classA, classB;
    private readonly Subject maths, czech, music;
    private readonly User novak, mala, kral, horak, parentJan, parentEva;
    private readonly StudentProfile jan, eva;
    private readonly TimetableVersion current;
    private readonly Classroom room1, room2;

    /// <summary>Monday: 1st maths in 5.A by Novák in room 1, Czech in 5.B by
    /// Malá in room 2; 2nd Czech in 5.A by Malá; 3rd music of a choir of both
    /// classes by Novák. Král teaches 5.B on Tuesday only, Horák nothing. Jan
    /// of 5.A is in the choir, Eva of 5.B isn't; each has a parent.</summary>
    public SubstitutionsTests()
    {
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = Monday };
        User Teacher(string name) => new()
        {
            Username = name, PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)],
            ActiveRole = db.Role(UserRole.Teacher), Person = new Person { FirstName = name, LastName = "Učitel", SchoolId = SchoolId },
        };
        User Parent(string name) => new()
        {
            Username = name, PasswordHash = "x", SchoolId = SchoolId, ActiveRole = db.Role(UserRole.Parent, SchoolId),
        };
        novak = Teacher("Novák");
        mala = Teacher("Malá");
        kral = Teacher("Král");
        horak = Teacher("Horák");
        parentJan = Parent("rodicJan");
        parentEva = Parent("rodicEva");
        classA = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        classB = new SchoolClass { Name = "5.B", Rocnik = 5, SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        czech = new Subject { Name = "Český jazyk", Abbreviation = "Čj", SchoolId = SchoolId };
        music = new Subject { Name = "Hudební výchova", Abbreviation = "Hv", SchoolId = SchoolId };
        room1 = new Classroom { Name = "101", SchoolId = SchoolId };
        room2 = new Classroom { Name = "102", SchoolId = SchoolId };
        current = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Rozvrh", ValidFrom = new DateOnly(2026, 9, 1), Status = TimetableVersionStatus.Published,
        };
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "skola" });
        db.Users.AddRange(novak, mala, kral, horak, parentJan, parentEva);
        db.AddRange(classA, classB, maths, czech, music, room1, room2, current);
        db.SaveChanges();
        db.SchoolPeriods.AddRange(Enumerable.Range(1, 6).Select(p => new SchoolPeriod
        {
            SchoolId = SchoolId, PeriodNumber = p, StartTime = new TimeOnly(7 + p, 0), EndTime = new TimeOnly(7 + p, 45),
        }));
        jan = Pupil("Jan", classA);
        eva = Pupil("Eva", classB);
        db.LinkChildAsync(parentJan.Id, jan.Id, SchoolId).GetAwaiter().GetResult();
        db.LinkChildAsync(parentEva.Id, eva.Id, SchoolId).GetAwaiter().GetResult();
        var choir = new StudentGroup { SchoolId = SchoolId, Name = "Sbor", SubjectId = music.Id, TimetableVersionId = current.Id };
        db.StudentGroups.Add(choir);
        db.SaveChanges();
        db.StudentGroupClasses.AddRange(
            new StudentGroupClass { SchoolId = SchoolId, StudentGroupId = choir.Id, SchoolClassId = classA.Id },
            new StudentGroupClass { SchoolId = SchoolId, StudentGroupId = choir.Id, SchoolClassId = classB.Id });
        db.StudentGroupMembers.Add(new StudentGroupMember { SchoolId = SchoolId, StudentGroupId = choir.Id, StudentId = jan.Id });
        db.TimetableEntries.AddRange(
            Lesson(classA, maths, DayOfWeek.Monday, 1, novak, room1),
            Lesson(classB, czech, DayOfWeek.Monday, 1, mala, room2),
            Lesson(classA, czech, DayOfWeek.Monday, 2, mala),
            Lesson(classA, music, DayOfWeek.Monday, 3, novak, group: choir),
            Lesson(classB, music, DayOfWeek.Monday, 3, novak, group: choir),
            Lesson(classB, maths, DayOfWeek.Tuesday, 1, kral));
        db.SaveChanges();
        Choir = choir;
    }

    private StudentGroup Choir { get; }

    private StudentProfile Pupil(string name, SchoolClass schoolClass)
    {
        var pupil = new StudentProfile
        {
            Person = new Person { FirstName = name, LastName = "Žák", SchoolId = SchoolId },
            SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(pupil);
        db.SaveChanges();
        return pupil;
    }

    private TimetableEntry Lesson(
        SchoolClass schoolClass, Subject subject, DayOfWeek day, int period, User teacher, Classroom? room = null,
        StudentGroup? group = null) => new()
    {
        SchoolId = SchoolId, TimetableVersionId = current.Id, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
        DayOfWeek = day, PeriodNumber = period, TeacherId = teacher.Id, ClassroomId = room?.Id, StudentGroup = group,
    };

    private SubstitutionsController Controller() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock, new Notifier(db, clock))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<SubstitutionDayDto> AbsentAsync(User teacher, DateOnly? from = null, DateOnly? to = null, int? periodFrom = null) =>
        Ok(await Controller().AddAbsence(new(teacher.Id, from ?? Monday, to ?? from ?? Monday, periodFrom)));

    private async Task<SubstitutionDayDto> ChangeAsync(LessonChangeRequest request) =>
        Ok(await Controller().SaveChange(request));

    private async Task<List<LessonChangeDto>> WeekAsync(int? classId = null, int? teacherId = null, int? studentId = null) =>
        [.. Ok(await Controller().GetWeek(Monday, classId, teacherId, studentId))];

    private Task<List<Notification>> NotificationsAsync() =>
        db.Notifications.Where(n => n.Type == NotificationType.Substitution).ToListAsync();

    [Fact]
    public async Task An_absent_teachers_lessons_need_a_substitute_and_the_free_teachers_are_offered_in_order()
    {
        db.TeacherQualifications.Add(new TeacherQualification { SchoolId = SchoolId, SubjectId = maths.Id, TeacherProfileId = AddProfile(horak) });
        await db.SaveChangesAsync();

        var day = await AbsentAsync(novak, periodFrom: 1);

        Assert.Equal([1, 3], day.Lessons.Where(l => l.NeedsCover).Select(l => l.PeriodNumber));
        var offered = Ok(await Controller().GetCandidates(Monday, 1, schoolClassId: classA.Id)).Teachers;
        // Malá teaches then; Horák has the aprobace, Král doesn't teach on Monday.
        Assert.Equal(["Horák", "Král"], offered.Select(t => t.Name.Split(' ')[0]));
        Assert.True(offered[0].Qualified);
        offered = Ok(await Controller().GetCandidates(Monday, 2, schoolClassId: classA.Id, subjectId: czech.Id)).Teachers;
        Assert.DoesNotContain(offered, t => t.TeacherId == novak.Id || t.TeacherId == mala.Id);
    }

    private int AddProfile(User user)
    {
        var person = db.Persons.First(p => p.UserId == user.Id);
        person.TeacherProfile = new TeacherProfile { SchoolId = SchoolId };
        db.SaveChanges();
        return person.TeacherProfile.PersonId;
    }

    [Fact]
    public async Task Those_teaching_that_day_come_before_those_who_dont_then_the_fewest_substitutions_in_the_month()
    {
        await AbsentAsync(novak);
        // Horák already substitutes twice this month, Král not at all: both don't teach on Monday.
        db.LessonChanges.AddRange(
            new LessonChange { SchoolId = SchoolId, Date = Monday.AddDays(3), PeriodNumber = 5, SchoolClassId = classA.Id, Extra = true, SubjectId = maths.Id, TeacherId = horak.Id },
            new LessonChange { SchoolId = SchoolId, Date = Monday.AddDays(3), PeriodNumber = 6, SchoolClassId = classA.Id, Extra = true, SubjectId = maths.Id, TeacherId = horak.Id });
        await db.SaveChangesAsync();

        var offered = Ok(await Controller().GetCandidates(Monday, 3, studentGroupId: Choir.Id)).Teachers;

        Assert.Equal(["Malá", "Král", "Horák"], offered.Select(t => t.Name.Split(' ')[0]));
        Assert.True(offered[0].TeachesThatDay);
        Assert.Equal(2, offered[2].SubstitutionsThisMonth);
    }

    [Fact]
    public async Task A_teacher_who_cant_teach_then_or_is_away_too_isnt_offered()
    {
        await AbsentAsync(novak);
        await AbsentAsync(kral);
        var person = await db.Persons.FirstAsync(p => p.UserId == horak.Id);
        person.TeacherProfile = new TeacherProfile
        {
            SchoolId = SchoolId, Availability = [new AvailabilitySlot(DayOfWeek.Monday, 1, AvailabilityKind.Unavailable)],
        };
        await db.SaveChangesAsync();

        var offered = Ok(await Controller().GetCandidates(Monday, 1, schoolClassId: classA.Id)).Teachers;

        Assert.Empty(offered);
        Assert.Equal(["101"], Ok(await Controller().GetCandidates(Monday, 1, schoolClassId: classA.Id)).Classrooms.Select(r => r.Name));
    }

    [Fact]
    public async Task Filling_gives_each_lesson_the_first_one_offered_and_counts_those_left_without()
    {
        await AbsentAsync(novak);
        await AbsentAsync(horak);

        var result = Ok(await Controller().Fill(Monday));

        Assert.Equal(0, result.Unfilled);
        Assert.All(result.Day.Lessons.Where(l => l.OriginalTeacherId == novak.Id), l => Assert.False(l.NeedsCover));
        Assert.Equal(kral.Id, result.Day.Lessons.Single(l => l.PeriodNumber == 1 && l.SchoolClassId == classA.Id).TeacherId);
        Assert.Equal(mala.Id, result.Day.Lessons.Single(l => l.PeriodNumber == 3).TeacherId);

        await AbsentAsync(kral);
        await AbsentAsync(mala);
        await Controller().DeleteChange((await db.LessonChanges.FirstAsync(c => c.PeriodNumber == 1)).Id);
        // Everyone is away now, Malá's substitution too.
        Assert.Equal(4, Ok(await Controller().Fill(Monday)).Unfilled);
    }

    [Fact]
    public async Task Changes_show_in_the_timetables_only_once_the_day_is_published_and_later_edits_once_published_again()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        Assert.Empty(await WeekAsync(classA.Id));

        var day = Ok(await Controller().Publish(Monday));
        Assert.False(day.Unpublished);
        var view = Assert.Single(await WeekAsync(classA.Id));
        Assert.Equal(("Supl", true, false, horak.Id), (view.Label, view.Replaces, view.Gone, view.TeacherId));

        day = await ChangeAsync(new(Monday, 1, classA.Id, Cancelled: true));
        Assert.True(day.Unpublished);
        Assert.Equal("Supl", Assert.Single(await WeekAsync(classA.Id)).Label);
        Ok(await Controller().Publish(Monday));
        Assert.Equal("Odpadá", Assert.Single(await WeekAsync(classA.Id)).Label);
    }

    [Fact]
    public async Task The_teachers_see_whom_they_substitute_and_who_substitutes_them()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        Ok(await Controller().Publish(Monday));

        var original = Assert.Single(await WeekAsync(teacherId: novak.Id));
        Assert.Equal(("Supl", true, true), (original.Label, original.Replaces, original.Gone));
        Assert.Equal((maths.Id, "Horák Učitel", "Novák Učitel"), (original.SubjectId, original.TeacherName, original.WasTeacher));
        var substitute = Assert.Single(await WeekAsync(teacherId: horak.Id));
        Assert.Equal(("Supl", false, false, "5.A", "Novák Učitel"),
            (substitute.Label, substitute.Replaces, substitute.Gone, substitute.SchoolClassName, substitute.WasTeacher));
    }

    [Fact]
    public async Task Overriding_the_timetables_own_values_is_no_change_at_all()
    {
        var day = await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: novak.Id, ClassroomId: room1.Id));

        Assert.Empty(await db.LessonChanges.ToListAsync());
        Assert.All(day.Lessons, l => Assert.Null(l.Label));
    }

    [Fact]
    public async Task A_lesson_moves_within_its_week_only()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveChange(
            new(Monday, 2, classA.Id, MovedToDate: Monday.AddDays(7), MovedToPeriod: 2))).Result);

        await ChangeAsync(new(Monday, 2, classA.Id, MovedToDate: Tuesday, MovedToPeriod: 4));
        Ok(await Controller().Publish(Monday));

        var views = await WeekAsync(classA.Id);
        Assert.Equal(
            [(Monday, 2, true, true, "Přesun na út 3. 11., 4. h"), (Tuesday, 4, false, false, "Přesun z po 2. 11., 2. h")],
            views.Select(v => (v.Date, v.PeriodNumber, v.Replaces, v.Gone, v.Label)));
        Assert.Equal(mala.Id, views[1].TeacherId);
        Assert.Contains(Ok(await Controller().GetDay(Tuesday)).Lessons, l => l.MovedIn && l.PeriodNumber == 4);
    }

    [Fact]
    public async Task A_class_merged_with_another_has_its_lesson_and_the_other_class_hears_with_whom()
    {
        await AbsentAsync(novak);
        var day = await ChangeAsync(new(Monday, 1, classA.Id, MergedWithClassId: classB.Id));
        Assert.DoesNotContain(day.Lessons, l => l.NeedsCover && l.PeriodNumber == 1);
        Assert.Empty(day.Problems);
        Ok(await Controller().Publish(Monday));

        var merged = Assert.Single(await WeekAsync(classA.Id));
        Assert.Equal(("Spojeno s 5.B", czech.Id, mala.Id, room2.Id), (merged.Label, merged.SubjectId, merged.TeacherId, merged.ClassroomId));
        Assert.Equal("Spojeno s 5.A", Assert.Single(await WeekAsync(classB.Id)).Label);
        Assert.Equal("Spojeno s 5.B", Assert.Single(await WeekAsync(teacherId: novak.Id)).Label);
        Assert.Equal("Spojeno s 5.A", Assert.Single(await WeekAsync(teacherId: mala.Id)).Label);

        Assert.IsType<ConflictObjectResult>((await Controller().SaveChange(new(Monday, 1, classB.Id, Cancelled: true))).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveChange(new(Monday, 2, classA.Id, MergedWithClassId: classB.Id))).Result);
    }

    [Fact]
    public async Task A_groups_lesson_changes_for_its_pupils_only()
    {
        await ChangeAsync(new(Monday, 3, StudentGroupId: Choir.Id, Cancelled: true));
        Ok(await Controller().Publish(Monday));

        Assert.Equal("Odpadá", Assert.Single(await WeekAsync(studentId: jan.Id)).Label);
        Assert.Empty(await WeekAsync(studentId: eva.Id));
        Assert.Single(await WeekAsync(classB.Id));
    }

    [Fact]
    public async Task An_extra_lesson_is_added_where_the_class_has_none()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveChange(
            new(Monday, 1, classA.Id, Extra: true, SubjectId: maths.Id))).Result);

        var day = await ChangeAsync(new(Monday, 4, classA.Id, Extra: true, SubjectId: maths.Id, TeacherId: horak.Id));
        Assert.Contains(day.Lessons, l => l.PeriodNumber == 4 && l.Label == "Navíc");
        Ok(await Controller().Publish(Monday));

        var view = Assert.Single(await WeekAsync(classA.Id));
        Assert.Equal((4, false, "Navíc"), (view.PeriodNumber, view.Replaces, view.Label));
    }

    [Fact]
    public async Task The_day_reports_a_teacher_or_classroom_in_two_places_at_once()
    {
        var day = await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: mala.Id, ClassroomId: room2.Id));

        Assert.Equal(2, day.Problems.Count);
        Assert.Contains("Malá Učitel učí zároveň", day.Problems[0].Text);
        Assert.Contains("v učebně 102", day.Problems[1].Text);
    }

    [Fact]
    public async Task A_day_without_a_timetable_has_no_changes()
    {
        var before = new DateOnly(2026, 8, 31);

        Assert.False(Ok(await Controller().GetDay(before)).HasTimetable);
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveChange(new(before, 1, classA.Id, Cancelled: true))).Result);
    }

    [Fact]
    public async Task Publishing_tells_the_teachers_and_families_concerned_what_changed_for_them()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        await ChangeAsync(new(Monday, 3, StudentGroupId: Choir.Id, Cancelled: true));
        Ok(await Controller().Publish(Monday));

        var notifications = await NotificationsAsync();
        Assert.Equal(
            new[] { novak.Id, horak.Id, parentJan.Id }.Order(),
            notifications.Select(n => n.UserId).Order());
        Assert.All(notifications, n => Assert.Equal(20261102, n.TargetId));
        var family = notifications.Single(n => n.UserId == parentJan.Id);
        Assert.Equal("Změny v rozvrhu na po 2. 11. — Jan", family.Title);
        Assert.Equal("1. h M: Supl, Horák Učitel místo Novák Učitel; 3. h Sbor Hv: Odpadá.", family.Body);
        Assert.Equal("1. h 5.A M: Supl, Horák Učitel místo Novák Učitel; 3. h Sbor (5.A, 5.B) Hv: Odpadá.",
            notifications.Single(n => n.UserId == novak.Id).Body);
    }

    [Fact]
    public async Task Publishing_again_tells_only_those_whose_lessons_changed_since()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        Ok(await Controller().Publish(Monday));
        db.Notifications.RemoveRange(db.Notifications);
        await db.SaveChangesAsync();

        Ok(await Controller().Publish(Monday));
        Assert.Empty(await NotificationsAsync());

        await ChangeAsync(new(Monday, 1, classB.Id, Cancelled: true));
        Ok(await Controller().Publish(Monday));
        Assert.Equal(new[] { mala.Id, parentEva.Id }.Order(), (await NotificationsAsync()).Select(n => n.UserId).Order());

        db.Notifications.RemoveRange(db.Notifications);
        await db.SaveChangesAsync();
        await Controller().DeleteChange((await db.LessonChanges.FirstAsync(c => c.SchoolClassId == classA.Id)).Id);
        Ok(await Controller().Publish(Monday));
        var horaks = (await NotificationsAsync()).Single(n => n.UserId == horak.Id);
        Assert.Equal("Hodiny jsou zase podle rozvrhu.", horaks.Body);
    }

    [Fact]
    public async Task A_past_day_is_published_without_telling_anyone()
    {
        var lastMonday = Monday.AddDays(-7);
        await ChangeAsync(new(lastMonday, 1, classA.Id, Cancelled: true));

        Ok(await Controller().Publish(lastMonday));

        Assert.Empty(await NotificationsAsync());
    }

    [Fact]
    public async Task Home_shows_todays_published_changes()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        Ok(await Controller().Publish(Monday));

        var dashboard = new DashboardController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, horak.Id, UserRole.Teacher, SchoolId, null)),
        };
        var lessons = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>((await dashboard.GetDashboard()).Result).Value).TodayLessons;

        var lesson = Assert.Single(lessons);
        Assert.Equal((1, "Matematika", "Supl", "Novák Učitel"), (lesson.PeriodNumber, lesson.SubjectName, lesson.Change, lesson.WasTeacher));
    }

    [Fact]
    public async Task A_change_doesnt_apply_to_another_lesson_a_new_timetable_has_then_and_the_day_reports_it()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, Cancelled: true));
        Ok(await Controller().Publish(Monday));
        Assert.Equal([Monday], Ok(await Controller().GetChangedDays(Monday)));
        current.ValidTo = Monday.AddDays(-1);
        var next = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Nový", ValidFrom = Monday, Status = TimetableVersionStatus.Published,
        };
        db.TimetableVersions.Add(next);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = next.Id, SchoolClassId = classA.Id, SubjectId = czech.Id,
            DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, TeacherId = mala.Id,
        });
        await db.SaveChangesAsync();

        Assert.Empty(await WeekAsync(classA.Id));
        var problem = Assert.Single(Ok(await Controller().GetDay(Monday)).Problems);
        Assert.Contains("v rozvrhu je teď jiná hodina", problem.Text);
        Assert.Equal((await db.LessonChanges.SingleAsync()).Id, problem.ChangeId);

        await AbsentAsync(mala);
        Ok(await Controller().Fill(Monday));
        var filled = await db.LessonChanges.SingleAsync();
        Assert.Equal((false, czech.Id, mala.Id), (filled.Cancelled, filled.OfSubjectId, filled.OfTeacherId));
        Assert.NotNull(filled.TeacherId);
    }

    [Fact]
    public async Task The_substitute_has_the_class_or_the_groups_pupils_for_the_day_once_published()
    {
        await ChangeAsync(new(Monday, 1, classA.Id, TeacherId: horak.Id));
        await ChangeAsync(new(Monday, 3, StudentGroupId: Choir.Id, TeacherId: horak.Id));
        Assert.Empty(await db.GetTeacherClassIdsAsync(horak.Id));

        Ok(await Controller().Publish(Monday));

        Assert.Equal([classA.Id], await db.GetTeacherClassIdsAsync(horak.Id));
        Assert.True(await db.TeachesClassInSubjectAsync(horak.Id, classA.Id, maths.Id));
        Assert.False(await db.TeachesClassInSubjectAsync(horak.Id, classA.Id, czech.Id));
        Assert.True(await db.TeachesStudentInSubjectAsync(horak.Id, jan.Id, music.Id));
        Assert.Equal(new[] { jan.Id }, await db.GetTeacherStudentIdsAsync(horak.Id));
        Assert.Equal([classA.Id], await db.GetTeacherClassIdsAsync(novak.Id));

        db.Today = Tuesday;
        Assert.Empty(await db.GetTeacherClassIdsAsync(horak.Id));
    }

    [Fact]
    public async Task The_day_lists_the_school_periods_and_the_classes_by_name()
    {
        var day = Ok(await Controller().GetDay(Monday));

        Assert.Equal([1, 2, 3, 4, 5, 6], day.Periods);
        Assert.Equal(["5.A", "5.B"], day.Classes.Select(c => c.Name));
    }
}
