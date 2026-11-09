using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Grades;

/// <summary>Jan (5.A, his own account 50) — the teacher (1) teaches him maths
/// and Czech is in his timetable too; 2 is the school's management.
/// 1. pololetí 2026/27: 1 Sep 2026 – 31 Jan 2027, issued 31 Jan 2027.</summary>
public class ReportCardsControllerTests : IDisposable
{
    private const int SchoolId = 1;
    private const int Year = 2026;
    private const SchoolHalfYear Half = SchoolHalfYear.First;

    private readonly AppDbContext db;
    private readonly StudentProfile jan;
    private readonly Subject maths = new() { Name = "Matematika", SchoolId = SchoolId };
    private readonly Subject czech = new() { Name = "Český jazyk", SchoolId = SchoolId };
    private readonly ClaimsPrincipal teacher;
    private readonly ClaimsPrincipal management;
    private readonly ClaimsPrincipal pupil;

    public ReportCardsControllerTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = new DateOnly(2026, 12, 1) };
        teacher = TestRoles.Principal(db, 1, UserRole.Teacher, SchoolId);
        management = TestRoles.Principal(db, 2, UserRole.Admin, SchoolId);
        pupil = TestRoles.Principal(db, 50, UserRole.Student, SchoolId);
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        jan = new StudentProfile
        {
            Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId, UserId = 50 },
            SchoolClass = schoolClass,
            SchoolId = SchoolId,
        };
        db.AddRange(schoolClass, jan, maths, czech);
        db.SaveChanges();
        db.Teaches(1, schoolClass.Id, maths.Id);
        db.Teaches(3, schoolClass.Id, czech.Id);
    }

    public void Dispose() => db.Dispose();

    private ReportCardsController As(ClaimsPrincipal user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(user) };

    private void Grade(Subject subject, int value, DateOnly on, int weight = 1)
    {
        db.Grades.Add(new Grade { StudentId = jan.Id, SubjectId = subject.Id, Value = value, Weight = weight, GradedOn = on, SchoolId = SchoolId });
        db.SaveChanges();
    }

    private async Task<ReportCardDto> CardAs(ClaimsPrincipal user)
    {
        var result = await As(user).GetReportCards(Year, Half, studentId: jan.Id);
        return Assert.Single((List<ReportCardDto>)Assert.IsType<OkObjectResult>(result.Result).Value!);
    }

    private static ReportCardSubjectDto In(ReportCardDto card, Subject subject) =>
        card.Subjects.Single(s => s.SubjectId == subject.Id);

    private Task<IActionResult> Set(ClaimsPrincipal user, Subject subject, int value) =>
        As(user).SetGrade(new(jan.Id, subject.Id, Year, Half, ReportCardGradeKind.Graded, value));

    private async Task<ConfirmReportCardGradesResult> Confirm(ClaimsPrincipal user, params Subject[] subjects)
    {
        var result = await As(user).ConfirmGrades(new(Year, Half, [.. subjects.Select(s => new ReportCardGradeKey(jan.Id, s.Id))]));
        return (ConfirmReportCardGradesResult)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    [Fact]
    public async Task SuggestsTheRoundedAverageOfTheHalfYearsGradesOnly()
    {
        Grade(maths, 1, new DateOnly(2026, 10, 1));
        Grade(maths, 2, new DateOnly(2026, 11, 1));
        Grade(maths, 5, new DateOnly(2026, 6, 1));

        var card = await CardAs(teacher);

        var inMaths = In(card, maths);
        Assert.Equal(1.5, inMaths.Average);
        Assert.Equal(2, inMaths.GradeCount);
        Assert.Equal(2, inMaths.Suggested);
        Assert.Equal((ReportCardGradeKind?)ReportCardGradeKind.Graded, inMaths.Kind);
        Assert.Equal(2, inMaths.Value);
        Assert.False(inMaths.Changed);
        Assert.True(inMaths.CanEdit);
        var inCzech = In(card, czech);
        Assert.Null(inCzech.Kind);
        Assert.False(inCzech.CanEdit);
        Assert.Equal(["Český jazyk", "Matematika"], card.Subjects.Select(s => s.SubjectName));
    }

    [Fact]
    public async Task AConfirmedGradeIsClosedUntilTheManagementReopensIt()
    {
        Grade(maths, 2, new DateOnly(2026, 10, 1));

        Assert.IsType<NoContentResult>(await Set(teacher, maths, 1));
        Assert.True(In(await CardAs(teacher), maths).Changed);
        Assert.Equal(new ConfirmReportCardGradesResult(1, 0), await Confirm(teacher, maths));

        var confirmed = In(await CardAs(teacher), maths);
        Assert.True(confirmed.Confirmed);
        Assert.Equal(1, confirmed.Value);
        Assert.False(confirmed.CanEdit);
        Assert.False(confirmed.CanReopen);
        Assert.IsType<ConflictObjectResult>(await Set(teacher, maths, 3));
        Assert.IsType<ConflictObjectResult>(await As(teacher).ResetGrade(jan.Id, maths.Id, Year, Half));
        Assert.IsType<ForbidResult>(await As(teacher).ReopenGrade(new(jan.Id, maths.Id, Year, Half)));

        Assert.True(In(await CardAs(management), maths).CanReopen);
        Assert.IsType<NoContentResult>(await As(management).ReopenGrade(new(jan.Id, maths.Id, Year, Half)));
        Assert.IsType<NoContentResult>(await As(teacher).ResetGrade(jan.Id, maths.Id, Year, Half));

        var reset = In(await CardAs(teacher), maths);
        Assert.False(reset.Confirmed);
        Assert.False(reset.Changed);
        Assert.Equal(2, reset.Value);
    }

    [Fact]
    public async Task ConfirmingSkipsSubjectsWithoutAnyGrade()
    {
        Grade(maths, 1, new DateOnly(2026, 10, 1));

        Assert.Equal(new ConfirmReportCardGradesResult(1, 1), await Confirm(management, maths, czech));
        Assert.False(In(await CardAs(management), czech).Confirmed);
    }

    [Fact]
    public async Task ATeacherConfirmsOnlyWhatTheyGrade()
    {
        var result = await As(teacher).ConfirmGrades(new(Year, Half, [new(jan.Id, czech.Id)]));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task TheFamilySeesConfirmedGradesOnceTheReportCardIsIssued()
    {
        Grade(maths, 1, new DateOnly(2026, 10, 1));
        Grade(czech, 3, new DateOnly(2026, 10, 1));
        await Confirm(management, maths);
        Assert.IsType<NoContentResult>(await Set(management, czech, 2));

        var before = await CardAs(pupil);
        Assert.False(before.Issued);
        Assert.Equal(1, In(before, maths).Average);
        Assert.Null(In(before, maths).Kind);
        Assert.Null(In(before, maths).Suggested);
        Assert.False(In(before, maths).CanEdit);
        Assert.IsType<BadRequestObjectResult>(await As(pupil).GetPdf(Year, Half, studentId: jan.Id));

        db.Today = new DateOnly(2027, 1, 31);
        var after = await CardAs(pupil);
        Assert.True(after.Issued);
        Assert.Equal(1, In(after, maths).Value);
        Assert.True(In(after, maths).Confirmed);
        Assert.Null(In(after, czech).Value);
        Assert.False(In(after, czech).Changed);
        Assert.Null(after.Result);
        Assert.IsType<FileContentResult>(await As(pupil).GetPdf(Year, Half, studentId: jan.Id));
    }

    [Fact]
    public async Task TheOverallResultComesOnceEverySubjectIsConfirmed()
    {
        Grade(maths, 1, new DateOnly(2026, 10, 1));
        Grade(czech, 3, new DateOnly(2026, 10, 1));
        await Confirm(management, maths);
        Assert.Null((await CardAs(management)).Result);

        await Confirm(management, czech);
        Assert.Equal(ReportCardResult.Passed, (await CardAs(management)).Result);
    }

    [Fact]
    public async Task CountsTheHalfYearsMissedLessons()
    {
        db.Absences.AddRange(
            new Absence { StudentId = jan.Id, Date = new DateOnly(2026, 10, 1), Periods = [1, 2, 3], Status = AbsenceStatus.Excused, SchoolId = SchoolId },
            new Absence { StudentId = jan.Id, Date = new DateOnly(2026, 10, 2), Periods = [4], Status = AbsenceStatus.Unexcused, SchoolId = SchoolId },
            new Absence { StudentId = jan.Id, Date = new DateOnly(2026, 6, 1), Periods = [1], Status = AbsenceStatus.Unexcused, SchoolId = SchoolId });
        db.BehaviorClassifications.Add(new BehaviorClassification
        {
            StudentId = jan.Id, SchoolYearStartYear = Year, HalfYear = Half, SchoolId = SchoolId,
            Value = BehaviorClassificationValue.Uspokojive, SetByName = "ředitel",
        });
        await db.SaveChangesAsync();

        var card = await CardAs(teacher);

        Assert.Equal(3, card.ExcusedHours);
        Assert.Equal(1, card.UnexcusedHours);
        Assert.Equal(BehaviorClassificationValue.Uspokojive, card.Behavior);
    }

    [Fact]
    public async Task TheClassPdfHasEveryPupil()
    {
        var result = await As(management).GetPdf(Year, Half, schoolClassId: jan.SchoolClassId);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("vysvedceni-5.A.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task ATeacherHasOnlyTheirClassesTheManagementTheWholeSchool()
    {
        var other = new SchoolClass { Name = "9.B", SchoolId = SchoolId };
        db.SchoolClasses.Add(other);
        jan.SchoolClass!.ClassTeacherId = 1;
        await db.SaveChangesAsync();

        var mine = (ReportCardClassesDto)Assert.IsType<OkObjectResult>((await As(teacher).GetClasses()).Result).Value!;
        var all = (ReportCardClassesDto)Assert.IsType<OkObjectResult>((await As(management).GetClasses()).Result).Value!;

        Assert.False(mine.All);
        Assert.Equal([new ReportCardClassDto(jan.SchoolClassId, "5.A", true)], mine.Classes);
        Assert.True(all.All);
        Assert.Equal(["5.A", "9.B"], all.Classes.Select(c => c.Name));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 1 }, BehaviorClassificationValue.VelmiDobre, ReportCardResult.WithHonours)]
    [InlineData(new[] { 1, 2, 2 }, BehaviorClassificationValue.VelmiDobre, ReportCardResult.Passed)]
    [InlineData(new[] { 1, 1, 3 }, BehaviorClassificationValue.VelmiDobre, ReportCardResult.Passed)]
    [InlineData(new[] { 1, 1, 1 }, BehaviorClassificationValue.Uspokojive, ReportCardResult.Passed)]
    [InlineData(new[] { 1, 1, 5 }, BehaviorClassificationValue.VelmiDobre, ReportCardResult.Failed)]
    public void TheOverallResult(int[] values, BehaviorClassificationValue behavior, ReportCardResult expected)
    {
        var marks = values.Select(v => (ReportCardGradeKind.Graded, (int?)v));

        Assert.Equal(expected, ReportCards.Result(marks, behavior, SchoolHalfYear.First));
    }

    [Fact]
    public void ExemptSubjectsDontCountAndNotGradedDependsOnTheHalf()
    {
        (ReportCardGradeKind, int?)[] exempt = [(ReportCardGradeKind.Graded, 1), (ReportCardGradeKind.Exempt, null)];
        (ReportCardGradeKind, int?)[] notGraded = [(ReportCardGradeKind.Graded, 1), (ReportCardGradeKind.NotGraded, null)];

        Assert.Equal(ReportCardResult.WithHonours, ReportCards.Result(exempt, BehaviorClassificationValue.VelmiDobre, SchoolHalfYear.First));
        Assert.Equal(ReportCardResult.NotAssessed, ReportCards.Result(notGraded, BehaviorClassificationValue.VelmiDobre, SchoolHalfYear.First));
        Assert.Equal(ReportCardResult.Failed, ReportCards.Result(notGraded, BehaviorClassificationValue.VelmiDobre, SchoolHalfYear.Second));
        (ReportCardGradeKind, int?)[] failedAndNotGraded = [(ReportCardGradeKind.Graded, 5), (ReportCardGradeKind.NotGraded, null)];
        Assert.Equal(ReportCardResult.Failed, ReportCards.Result(failedAndNotGraded, BehaviorClassificationValue.VelmiDobre, SchoolHalfYear.First));
    }
}
