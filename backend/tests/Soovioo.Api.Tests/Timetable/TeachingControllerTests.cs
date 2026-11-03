using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class TeachingControllerTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly TimetableVersion version;
    private readonly Subject history, maths;

    public TeachingControllerTests()
    {
        history = new Subject { Name = "Dějepis", Abbreviation = "D", SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        version = db.CurrentTimetableVersion();
        db.Subjects.AddRange(history, maths);
        db.SaveChanges();
    }

    private SchoolClass Class(string name, int? grade)
    {
        var schoolClass = new SchoolClass { Name = name, Rocnik = grade, SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.SaveChanges();
        return schoolClass;
    }

    private User Teacher(string lastName, decimal? contracted, params TeacherQualification[] qualifications)
    {
        var user = new User
        {
            Username = lastName.ToLowerInvariant(), PasswordHash = "x", SchoolId = SchoolId,
            Roles = [db.Role(UserRole.Teacher)],
        };
        db.Users.Add(user);
        db.Persons.Add(new Person
        {
            SchoolId = SchoolId, FirstName = "", LastName = lastName, User = user,
            TeacherProfile = new TeacherProfile { SchoolId = SchoolId, ContractedHours = contracted, Qualifications = [.. qualifications] },
        });
        db.SaveChanges();
        return user;
    }

    private static TeacherQualification Of(Subject subject, int? from = null, int? to = null) =>
        new() { SchoolId = SchoolId, Subject = subject, GradeFrom = from, GradeTo = to };

    private void Plan(int grade, Subject subject, decimal hours)
    {
        db.CurriculumItems.Add(new CurriculumItem
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, Grade = grade, SubjectId = subject.Id, HoursPerWeek = hours,
        });
        db.SaveChanges();
    }

    private void Lesson(SchoolClass schoolClass, Subject subject, User? teacher, DayOfWeek day = DayOfWeek.Monday,
        WeekParity parity = WeekParity.Every)
    {
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
            TeacherId = teacher?.Id, DayOfWeek = day, PeriodNumber = db.TimetableEntries.Local.Count + 1, WeekParity = parity,
        });
        db.SaveChanges();
    }

    private TeachingController Controller(UserRole role = UserRole.Admin, int userId = 900) =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, userId, role)),
        };

    private static TeachingOverviewDto Value(ActionResult<TeachingOverviewDto> result) =>
        Assert.IsType<TeachingOverviewDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<TeachingOverviewDto> Overview(UserRole role = UserRole.Admin, int userId = 900) =>
        Value(await Controller(role, userId).GetTeaching(version.Id));

    private static TeachingRowDto Row(TeachingOverviewDto overview, SchoolClass schoolClass, Subject subject) =>
        overview.Classes.Single(c => c.Id == schoolClass.Id).Rows.Single(r => r.SubjectId == subject.Id);

    [Fact]
    public async Task WhatTheSchoolDidNotAssignGoesToSomeoneQualifiedForTheGrade()
    {
        var fifth = Class("5.A", 5);
        Teacher("Starší", 22, Of(history, 6, 9));
        var anyGrade = Teacher("Nováková", 22, Of(history));
        Plan(5, history, 2);

        var row = Row(await Overview(), fifth, history);

        Assert.Equal((anyGrade.Id, true), (row.TeacherId, row.Automatic));
        Assert.Equal(["V rozvrhu chybí 2 h."], row.Problems);
    }

    [Fact]
    public async Task ThreeHistoryTeachersShareTheClassesByTheirContractedHours()
    {
        var classes = new[] { Class("6.A", 6), Class("6.B", 6), Class("6.C", 6) };
        var teachers = new[] { Teacher("Adámek", 2, Of(history)), Teacher("Bártová", 2, Of(history)), Teacher("Cibulka", 2, Of(history)) };
        Plan(6, history, 2);

        var overview = await Overview();

        Assert.Equal(teachers.Select(t => t.Id).Order(), classes.Select(c => Row(overview, c, history).TeacherId!.Value).Order());
        Assert.All(overview.Teachers, t => Assert.Equal(2m, t.AssignedHours));
        Assert.All(overview.Teachers, t => Assert.Empty(t.Problems));
    }

    [Fact]
    public async Task WhoTeachesItInTheTimetableKeepsIt()
    {
        var seventh = Class("7.A", 7);
        Teacher("Adámek", 22, Of(history));
        var inTimetable = Teacher("Bártová", 22, Of(history));
        Plan(7, history, 1);
        Lesson(seventh, history, inTimetable);

        var row = Row(await Overview(), seventh, history);

        Assert.Equal(inTimetable.Id, row.TeacherId);
        Assert.Empty(row.Problems);
    }

    [Fact]
    public async Task TheChecksCompareThePlanTimetableAndContracts()
    {
        var eighth = Class("8.A", 8);
        var assigned = Teacher("Adámek", 1, Of(maths));
        var other = Teacher("Bártová", null, Of(maths));
        other.Person!.TeacherProfile!.TeachingDays = [DayOfWeek.Tuesday];
        Plan(8, history, 1);
        Plan(8, maths, 2);
        db.TeachingAssignments.Add(new TeachingAssignment
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = eighth.Id, SubjectId = history.Id, TeacherId = assigned.Id,
        });
        db.TeachingAssignments.Add(new TeachingAssignment
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = eighth.Id, SubjectId = maths.Id, TeacherId = assigned.Id,
        });
        db.SaveChanges();
        Lesson(eighth, maths, assigned);
        Lesson(eighth, maths, other, DayOfWeek.Monday, WeekParity.Odd);

        var overview = await Overview();

        Assert.Equal(["V rozvrhu chybí 1 h.", "Adámek nemá aprobaci pro D v 8. ročníku."], Row(overview, eighth, history).Problems);
        var mathsRow = Row(overview, eighth, maths);
        Assert.Equal(1.5m, mathsRow.TimetableHours);
        Assert.Equal(["V rozvrhu chybí 0,5 h.", "V rozvrhu učí Bártová."], mathsRow.Problems);
        Assert.Equal(["Přiděleno o 2 h víc, než má nasmlouváno."], overview.Teachers.Single(t => t.Id == assigned.Id).Problems);
        Assert.Equal(["Nemá zadané nasmlouvané hodiny.", "V rozvrhu učí v pondělí, kdy učit nemůže."],
            overview.Teachers.Single(t => t.Id == other.Id).Problems);
    }

    [Fact]
    public async Task AClassWithoutAGradeHasOnlyItsOwnPlan()
    {
        var noGrade = Class("Přípravka", null);
        Plan(1, history, 1);
        Lesson(noGrade, maths, null);

        var schoolClass = (await Overview()).Classes.Single(c => c.Id == noGrade.Id);

        Assert.Equal(["Třída nemá zadaný ročník — učební plán ročníku se na ni nevztahuje, jen její vlastní."], schoolClass.Problems);
        Assert.Equal([maths.Id], schoolClass.Rows.Select(r => r.SubjectId));

        Assert.Contains("Předmět není v učebním plánu třídy.", schoolClass.Rows.Single().Problems);
        var own = Value(await Controller().SetClassCurriculum(version.Id, new(noGrade.Id, maths.Id, 1)));
        Assert.Equal((1m, true), (Row(own, noGrade, maths).PlanHours!.Value, Row(own, noGrade, maths).ClassPlan));
        Assert.DoesNotContain("Předmět není v učebním plánu třídy.", Row(own, noGrade, maths).Problems);
    }

    [Fact]
    public async Task AClassChangesItsGradesPlan()
    {
        var a = Class("7.A", 7);
        var b = Class("7.B", 7);
        var german = new Subject { Name = "Německý jazyk", Abbreviation = "Nj", SchoolId = SchoolId };
        db.Subjects.Add(german);
        db.SaveChanges();
        Plan(7, history, 2);
        Plan(7, german, 3);

        Value(await Controller().SetClassCurriculum(version.Id, new(a.Id, german.Id, 0)));
        var overview = Value(await Controller().SetClassCurriculum(version.Id, new(b.Id, history.Id, 1)));

        var aGerman = Row(overview, a, german);
        Assert.Equal((0m, 3m, true), (aGerman.PlanHours!.Value, aGerman.GradeHours!.Value, aGerman.ClassPlan));
        Assert.Empty(aGerman.Problems);
        Assert.Null(aGerman.TeacherId);
        Assert.Equal((1m, true), (Row(overview, b, history).PlanHours!.Value, Row(overview, b, history).ClassPlan));
        Assert.Equal(3m, Row(overview, b, german).PlanHours);

        // The grade's hours again: the class follows its grade.
        var back = Value(await Controller().SetClassCurriculum(version.Id, new(b.Id, history.Id, 2)));
        Assert.False(Row(back, b, history).ClassPlan);
        Assert.Equal(1, await db.ClassCurriculumItems.CountAsync());
    }

    [Fact]
    public async Task ClassesOfAGradeWithDifferentLanguagesGetTheirOwnPlanFromTheTimetable()
    {
        var a = Class("7.A", 7);
        var b = Class("7.B", 7);
        var c = Class("7.C", 7);
        var german = new Subject { Name = "Německý jazyk", Abbreviation = "Nj", SchoolId = SchoolId };
        db.Subjects.Add(german);
        db.SaveChanges();
        Lesson(a, maths, null);
        Lesson(b, maths, null);
        Lesson(c, german, null);

        var overview = Value(await Controller().ImportFromTimetable(version.Id));

        Assert.Equal([new CurriculumItemDto(7, maths.Id, 1)], overview.Curriculum);
        Assert.Equal((0m, true), (Row(overview, c, maths).PlanHours!.Value, Row(overview, c, maths).ClassPlan));
        Assert.Equal((1m, true), (Row(overview, c, german).PlanHours!.Value, Row(overview, c, german).ClassPlan));
        Assert.Throws<InvalidOperationException>(() => Row(overview, a, german));
        Assert.All(overview.Classes.SelectMany(x => x.Rows), r => Assert.DoesNotContain(r.Problems, p => p.StartsWith("V rozvrhu")));
    }

    [Fact]
    public async Task AnAssignmentIsSetConfirmedAndClearedBackToAutomatic()
    {
        var sixth = Class("6.A", 6);
        var first = Teacher("Adámek", 22, Of(history));
        var second = Teacher("Bártová", 22, Of(history));
        Plan(6, history, 2);

        var set = Value(await Controller().SetAssignment(version.Id, new(sixth.Id, history.Id, second.Id)));
        Assert.Equal((second.Id, false), (Row(set, sixth, history).TeacherId, Row(set, sixth, history).Automatic));

        var cleared = Value(await Controller().SetAssignment(version.Id, new(sixth.Id, history.Id, null)));
        Assert.Equal((first.Id, true), (Row(cleared, sixth, history).TeacherId, Row(cleared, sixth, history).Automatic));

        var confirmed = Value(await Controller().ConfirmAssignments(version.Id));
        Assert.False(Row(confirmed, sixth, history).Automatic);
        Assert.Equal(first.Id, (await db.TeachingAssignments.SingleAsync()).TeacherId);
    }

    [Fact]
    public async Task ThePlanAndAssignmentsAreReadFromTheTimetable()
    {
        var a = Class("6.A", 6);
        var b = Class("6.B", 6);
        var c = Class("6.C", 6);
        var teacher = Teacher("Adámek", 22);
        var substitute = Teacher("Bártová", 22);
        Lesson(a, history, teacher);
        Lesson(a, history, teacher, DayOfWeek.Tuesday);
        Lesson(a, history, substitute, DayOfWeek.Wednesday, WeekParity.Odd);
        Lesson(b, history, substitute);
        Lesson(b, history, substitute, DayOfWeek.Tuesday);
        Lesson(c, history, substitute);
        Lesson(c, history, substitute, DayOfWeek.Tuesday);

        var overview = Value(await Controller().ImportFromTimetable(version.Id));

        Assert.Equal([new CurriculumItemDto(6, history.Id, 2)], overview.Curriculum);
        Assert.Equal((teacher.Id, false), (Row(overview, a, history).TeacherId, Row(overview, a, history).Automatic));
        Assert.Equal(substitute.Id, Row(overview, b, history).TeacherId);
    }

    [Fact]
    public async Task TheCurriculumIsSavedWhole()
    {
        Plan(6, maths, 4);

        var saved = Value(await Controller().SaveCurriculum(version.Id, new([new(6, history.Id, 1.5m), new(7, history.Id, 0)])));

        Assert.Equal([new CurriculumItemDto(6, history.Id, 1.5m)], saved.Curriculum);
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveCurriculum(version.Id, new([new(6, history.Id, 1.2m)]))).Result);
        version.Status = TimetableVersionStatus.Archived;
        db.SaveChanges();
        Assert.IsType<BadRequestObjectResult>((await Controller().SaveCurriculum(version.Id, new([]))).Result);
    }

    [Fact]
    public async Task ATeacherSeesOnlyWhatTheyTeach()
    {
        var sixth = Class("6.A", 6);
        var me = Teacher("Adámek", 2, Of(history));
        Teacher("Bártová", 4, Of(maths));
        Plan(6, history, 2);
        Plan(6, maths, 4);

        var overview = await Overview(UserRole.Teacher, me.Id);

        Assert.False(overview.CanEdit);
        Assert.Equal([history.Id], overview.Classes.Single().Rows.Select(r => r.SubjectId));
        Assert.Equal([me.Id], overview.Teachers.Select(t => t.Id));
    }

    [Fact]
    public async Task AnotherSchoolsDraftIsNotFound()
    {
        var draft = new TimetableVersion { SchoolId = SchoolId, Name = "Koncept", Status = TimetableVersionStatus.Draft };
        db.TimetableVersions.Add(draft);
        db.SaveChanges();
        var teacher = Teacher("Adámek", 22);

        Assert.IsType<NotFoundResult>((await Controller(UserRole.Teacher, teacher.Id).GetTeaching(draft.Id)).Result);
        Assert.IsType<OkObjectResult>((await Controller().GetTeaching(draft.Id)).Result);
    }
}
