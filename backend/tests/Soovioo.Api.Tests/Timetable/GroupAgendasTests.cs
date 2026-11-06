using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Homework;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

/// <summary>A teacher who teaches a class only in a group teaches just the
/// group's pupils: grades, absences, homework, "my classes", directories.</summary>
public class GroupAgendasTests
{
    private const int SchoolId = 1;
    private const int Kral = 10, Novak = 11, Admin = 900;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly SchoolClass a, b;
    private readonly Subject maths, german;
    private readonly StudentProfile anna, petr, eva;
    private readonly StudentGroup germanGroup;

    public GroupAgendasTests()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        a = new SchoolClass { Name = "7.A", Rocnik = 7, SchoolId = SchoolId };
        b = new SchoolClass { Name = "7.B", Rocnik = 7, SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        german = new Subject { Name = "Německý jazyk", Abbreviation = "Nj", SchoolId = SchoolId };
        db.AddRange(a, b, maths, german);
        db.SaveChanges();
        TestRoles.Principal(db, Kral, UserRole.Teacher, name: "Král");
        TestRoles.Principal(db, Novak, UserRole.Teacher, name: "Novák");
        anna = Pupil("Anna", a);
        petr = Pupil("Petr", a);
        eva = Pupil("Eva", b);

        // Novák teaches 7.A maths; Král German to Anna (7.A) and Eva (7.B) only.
        db.Teaches(Novak, a.Id, maths.Id);
        germanGroup = new StudentGroup
        {
            Name = "Nj", SchoolId = SchoolId, TimetableVersion = db.CurrentTimetableVersion(SchoolId), SubjectId = german.Id, TeacherId = Kral,
            Classes = [new() { SchoolClassId = a.Id, SchoolId = SchoolId }, new() { SchoolClassId = b.Id, SchoolId = SchoolId }],
            Members = [new() { StudentId = anna.Id, SchoolId = SchoolId }, new() { StudentId = eva.Id, SchoolId = SchoolId }],
        };
        db.StudentGroups.Add(germanGroup);
        db.SaveChanges();
        foreach (var schoolClass in new[] { a, b })
        {
            db.TimetableEntries.Add(new TimetableEntry
            {
                TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id,
                SubjectId = german.Id, TeacherId = Kral, StudentGroupId = germanGroup.Id, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 3,
            });
        }
        db.SaveChanges();
    }

    private StudentProfile Pupil(string firstName, SchoolClass schoolClass)
    {
        var student = new StudentProfile
        {
            Person = new Person { FirstName = firstName, LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    /// <summary>The pupil's own account.</summary>
    private int AccountOf(StudentProfile student)
    {
        var userId = 100 + student.Id;
        TestRoles.Principal(db, userId, UserRole.Student);
        student.Person!.UserId = userId;
        db.SaveChanges();
        return userId;
    }

    private Notifier Notifier() => new(db, TimeProvider.System);

    private GradesController Grades(int userId) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), Notifier()) { ControllerContext = TestRoles.Context(db, userId, UserRole.Teacher) };

    private AbsencesController Absences(int userId) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), Notifier()) { ControllerContext = TestRoles.Context(db, userId, UserRole.Teacher) };

    private HomeworkController Homework(int userId, UserRole role = UserRole.Teacher) =>
        new(db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()), Notifier())
        {
            ControllerContext = TestRoles.Context(db, userId, role),
        };

    private StudentsController Students(int userId) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(db, userId, UserRole.Teacher) };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private CreateHomeworkRequest GroupHomework(bool requiresCompletion = false) =>
        new(null, german.Id, "Slovíčka", new DateOnly(2026, 11, 10), requiresCompletion, StudentGroupId: germanGroup.Id);

    [Fact]
    public async Task AGroupsTeacherGradesOnlyItsPupilsInItsSubject()
    {
        Assert.IsType<CreatedAtActionResult>((await Grades(Kral).AddGrade(new CreateGradeRequest(eva.Id, german.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(Kral).AddGrade(new CreateGradeRequest(petr.Id, german.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(Kral).AddGrade(new CreateGradeRequest(anna.Id, maths.Id, 1, 1, null))).Result);
        // The class's teacher of another subject doesn't grade the group's.
        Assert.IsType<ForbidResult>((await Grades(Novak).AddGrade(new CreateGradeRequest(anna.Id, german.Id, 1, 1, null))).Result);

        var seen = Ok(await Grades(Kral).GetGrades()).Items;
        Assert.Equal([eva.Id], seen.Select(g => g.StudentId));
    }

    [Fact]
    public async Task AGroupsTeacherTeachesItsPupilsButNotTheirClasses()
    {
        Assert.Empty(await db.GetTeacherClassIdsAsync(Kral));
        Assert.Equal([anna.Id, eva.Id], (await db.GetTeacherStudentIdsAsync(Kral)).Order());
        Assert.Equal([a.Id], await db.GetTeacherClassIdsAsync(Novak));

        var broadcast = new MessageBroadcastController(db, new FakeCurrentSchoolContext(SchoolId), Notifier())
        {
            ControllerContext = TestRoles.Context(db, Kral, UserRole.Teacher),
        };
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<SchoolClass>>(
            Assert.IsType<OkObjectResult>((await broadcast.GetBroadcastableClasses()).Result).Value));

        var day = new DateOnly(2026, 11, 3);
        Assert.IsType<CreatedAtActionResult>((await Absences(Kral).AddAbsence(new CreateAbsenceRequest(eva.Id, day, [3]))).Result);
        Assert.IsType<ForbidResult>((await Absences(Kral).AddAbsence(new CreateAbsenceRequest(petr.Id, day, [3]))).Result);
    }

    [Fact]
    public async Task TheReachSaysWhomAndInWhichSubjects()
    {
        var kral = Ok(await Students(Kral).GetReach(Permission.GradesWrite));
        Assert.False(kral.All);
        Assert.Equal([anna.Id, eva.Id], kral.Students.Select(s => s.StudentId));
        Assert.All(kral.Students, s =>
        {
            Assert.Equal([german.Id], s.SubjectIds!);
            Assert.Equal([germanGroup.Id], s.StudentGroupIds);
        });

        var novak = Ok(await Students(Novak).GetReach(Permission.GradesWrite));
        Assert.Equal([anna.Id, petr.Id], novak.Students.Select(s => s.StudentId));
        Assert.All(novak.Students, s => Assert.Equal([maths.Id], s.SubjectIds!));

        var admin = new StudentsController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(db, Admin, UserRole.Admin),
        };
        Assert.True(Ok(await admin.GetReach(Permission.GradesWrite)).All);
    }

    [Fact]
    public async Task HomeworkOfAGroupIsForItsPupilsAcrossItsClasses()
    {
        var created = Assert.IsType<HomeworkDto>(
            Assert.IsType<CreatedAtActionResult>((await Homework(Kral).CreateHomework(GroupHomework(requiresCompletion: true))).Result).Value);
        Assert.Equal(("Nj (7.A, 7.B)", null, germanGroup.Id), (created.SchoolClassName, created.SchoolClassId, created.StudentGroupId));
        Assert.Equal([a.Id, b.Id], created.SchoolClassIds);

        // Its pupils get it, a classmate outside the group doesn't.
        var annaUser = AccountOf(anna);
        var petrUser = AccountOf(petr);
        Assert.Single(Ok(await Homework(annaUser, UserRole.Student).GetHomework()));
        Assert.Empty(Ok(await Homework(petrUser, UserRole.Student).GetHomework()));
        Assert.IsType<NoContentResult>(await Homework(annaUser, UserRole.Student).MarkComplete(created.Id, new MarkHomeworkCompleteRequest(anna.Id)));
        Assert.IsType<BadRequestObjectResult>(await Homework(petrUser, UserRole.Student).MarkComplete(created.Id, new MarkHomeworkCompleteRequest(petr.Id)));

        var seen = Assert.Single(Ok(await Homework(Kral).GetHomework()));
        Assert.Equal([anna.Id, eva.Id], seen.GroupStudentIds!.Order());
    }

    [Fact]
    public async Task OnlyTheGroupsTeacherGivesItHomeworkAndNotToTheWholeClass()
    {
        Assert.IsType<ForbidResult>((await Homework(Novak).CreateHomework(GroupHomework())).Result);
        Assert.IsType<ForbidResult>((await Homework(Kral).CreateHomework(
            new CreateHomeworkRequest(a.Id, german.Id, "Slovíčka", new DateOnly(2026, 11, 10), false))).Result);
        Assert.Equal("Skupina má jiný předmět.", Assert.IsType<BadRequestObjectResult>((await Homework(Kral).CreateHomework(
            GroupHomework() with { SubjectId = maths.Id })).Result).Value);
        Assert.Equal("Všichni vybraní žáci musí patřit do skupiny.", Assert.IsType<BadRequestObjectResult>((await Homework(Kral).CreateHomework(
            GroupHomework() with { StudentIds = [petr.Id] })).Result).Value);
        Assert.IsType<CreatedAtActionResult>((await Homework(Kral).CreateHomework(GroupHomework() with { StudentIds = [eva.Id] })).Result);
    }

    [Fact]
    public async Task AGroupWithHomeworkIsNotDeleted()
    {
        Assert.IsType<CreatedAtActionResult>((await Homework(Kral).CreateHomework(GroupHomework())).Result);
        var groups = new StudentGroupsController(db, new FakeCurrentSchoolContext(SchoolId), TimeProvider.System)
        {
            ControllerContext = TestRoles.Context(db, Admin, UserRole.Admin),
        };

        (await db.TimetableVersions.FindAsync(germanGroup.TimetableVersionId))!.Status = TimetableVersionStatus.Draft;

        var refused = Assert.IsType<BadRequestObjectResult>(await groups.DeleteGroup(germanGroup.TimetableVersionId, germanGroup.Id));
        Assert.Equal("Skupina má domácí úkoly, smazat ji nejde.", refused.Value);
        Assert.True(await db.StudentGroups.AnyAsync(g => g.Id == germanGroup.Id));
    }

    [Fact]
    public async Task APupilsDirectoryHasTheTeachersOfTheirGroupsOnly()
    {
        var annaUser = AccountOf(anna);
        var petrUser = AccountOf(petr);
        ClassDirectoryController Directory(int userId) =>
            new(db, new FakeCurrentSchoolContext(SchoolId), new PhotoAccess(db, new FakeCurrentSchoolContext(SchoolId)))
            {
                ControllerContext = TestRoles.Context(db, userId, UserRole.Student),
            };

        Assert.Equal([Kral, Novak], Ok(await Directory(annaUser).Get(null)).Teachers.Select(t => t.UserId).Order());
        Assert.Equal([Novak], Ok(await Directory(petrUser).Get(null)).Teachers.Select(t => t.UserId));
    }
}
