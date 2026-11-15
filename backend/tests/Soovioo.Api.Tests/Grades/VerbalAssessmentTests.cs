using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.People;
using Soovioo.Api.SchoolYears;

namespace Soovioo.Api.Tests.Grades;

/// <summary>Jan (3.A, ročník 3, his own account 50) — the teacher (1) is his
/// třídní and teaches him maths, Czech is someone else's; 2 is the school's
/// management. 1. pololetí 2026/27.</summary>
public class VerbalAssessmentTests : IDisposable
{
    private const int SchoolId = 1;
    private const int Year = 2026;

    private readonly AppDbContext db;
    private readonly int period;
    private readonly StudentProfile jan;
    private readonly SchoolClass schoolClass;
    private readonly Subject maths = new() { Name = "Matematika", SchoolId = SchoolId };
    private readonly Subject czech = new() { Name = "Český jazyk", SchoolId = SchoolId };
    private readonly ClaimsPrincipal teacher;
    private readonly ClaimsPrincipal management;
    private readonly ClaimsPrincipal pupil;

    public VerbalAssessmentTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId)) { Today = new DateOnly(2026, 12, 1) };
        period = db.FirstHalf(Year).Id;
        teacher = TestRoles.Principal(db, 1, UserRole.Teacher, SchoolId);
        management = TestRoles.Principal(db, 2, UserRole.Admin, SchoolId);
        pupil = TestRoles.Principal(db, 50, UserRole.Student, SchoolId);
        schoolClass = new SchoolClass { Name = "3.A", Rocnik = 3, SchoolId = SchoolId, ClassTeacherId = 1 };
        jan = new StudentProfile
        {
            Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId, UserId = 50 },
            SchoolClass = schoolClass,
            SchoolId = SchoolId,
        };
        db.AddRange(schoolClass, jan, maths, czech);
        db.Schools.Add(new School { Id = SchoolId, Name = "ZŠ Lipová", Slug = "zs", City = "Brno", Izo = "102030405" });
        db.SaveChanges();
        db.Teaches(1, schoolClass.Id, maths.Id);
        db.Teaches(3, schoolClass.Id, czech.Id);
        Grade(maths, 2);
        Grade(czech, 1);
    }

    public void Dispose() => db.Dispose();

    private ReportCardsController As(ClaimsPrincipal user) =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(user) };

    private MySchoolController Settings() =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(management) };

    private void Grade(Subject subject, int value)
    {
        db.Grades.Add(new Grade
        {
            StudentId = jan.Id, SubjectId = subject.Id, Value = value, Weight = 1,
            GradedOn = new DateOnly(2026, 10, 1), SchoolId = SchoolId,
        });
        db.SaveChanges();
    }

    private async Task Rule(
        AssessmentMode mode, int? rocnik = null, int? studentId = null, Subject? subject = null, bool behavior = false,
        int? classId = null, ReportCardMarkFormat? format = null) =>
        Assert.IsType<NoContentResult>(await Settings().SetAssessmentRule(new(rocnik, classId, studentId, subject?.Id, behavior, mode, format)));

    private async Task<ReportCardDto> CardAs(ClaimsPrincipal user)
    {
        var result = await As(user).GetReportCards(period, studentId: jan.Id);
        return Assert.Single((List<ReportCardDto>)Assert.IsType<OkObjectResult>(result.Result).Value!);
    }

    private static ReportCardSubjectDto In(ReportCardDto card, Subject subject) =>
        card.Subjects.Single(s => s.SubjectId == subject.Id);

    private Task<IActionResult> Text(ClaimsPrincipal user, Subject subject, string? text) =>
        As(user).SetGradeText(new(jan.Id, subject.Id, period, text));

    private static int Pages(FileContentResult pdf) =>
        System.Text.RegularExpressions.Regex.Count(
            System.Text.Encoding.Latin1.GetString(pdf.FileContents), @"/Type\s*/Page\b");

    private async Task<ConfirmReportCardGradesResult> Confirm(ClaimsPrincipal user, params Subject[] subjects)
    {
        var result = await As(user).ConfirmGrades(new(period, [.. subjects.Select(s => new ReportCardGradeKey(jan.Id, s.Id))]));
        return (ConfirmReportCardGradesResult)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    [Fact]
    public async Task ThePupilsRuleBeatsTheRocniksAndASubjectBeatsAllSubjects()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3);
        await Rule(AssessmentMode.ClassificationWithVerbal, rocnik: 3, subject: maths);
        await Rule(AssessmentMode.Verbal, rocnik: 3, behavior: true);

        var byRocnik = await CardAs(management);
        Assert.Equal(AssessmentMode.ClassificationWithVerbal, In(byRocnik, maths).Mode);
        Assert.Equal(AssessmentMode.Verbal, In(byRocnik, czech).Mode);
        Assert.Equal(AssessmentMode.Verbal, byRocnik.BehaviorMode);
        Assert.True(byRocnik.HasVerbal);

        await Rule(AssessmentMode.Classification, studentId: jan.Id);
        await Rule(AssessmentMode.Verbal, studentId: jan.Id, subject: maths);

        var byPupil = await CardAs(management);
        Assert.Equal(AssessmentMode.Verbal, In(byPupil, maths).Mode);
        Assert.Equal(AssessmentMode.Classification, In(byPupil, czech).Mode);
        Assert.Equal(AssessmentMode.Verbal, byPupil.BehaviorMode);
    }

    [Fact]
    public async Task SettingARuleAgainChangesItsMode()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3, subject: maths);
        await Rule(AssessmentMode.ClassificationWithVerbal, rocnik: 3, subject: maths);

        var rules = (List<AssessmentModeRuleDto>)Assert.IsType<OkObjectResult>((await Settings().GetAssessmentRules()).Result).Value!;
        var rule = Assert.Single(rules);
        Assert.Equal(AssessmentMode.ClassificationWithVerbal, rule.Mode);
        Assert.Equal("Matematika", rule.SubjectName);

        Assert.IsType<BadRequestObjectResult>(await Settings().SetAssessmentRule(new(3, null, jan.Id, null, false, AssessmentMode.Verbal)));
        Assert.IsType<BadRequestObjectResult>(await Settings().SetAssessmentRule(new(3, null, null, maths.Id, true, AssessmentMode.Verbal)));
        Assert.IsType<BadRequestObjectResult>(await Settings().SetAssessmentRule(
            new(3, null, null, maths.Id, false, AssessmentMode.Verbal, ReportCardMarkFormat.Digits)));
        Assert.IsType<NoContentResult>(await Settings().DeleteAssessmentRule(rule.Id));
        Assert.Equal(AssessmentMode.Classification, In(await CardAs(management), maths).Mode);
    }

    [Fact]
    public async Task ASubjectAssessedSlovneIsConfirmedOnlyWithItsText()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3, subject: maths);

        Assert.Equal(new ConfirmReportCardGradesResult(0, 1), await Confirm(teacher, maths));
        Assert.IsType<NoContentResult>(await Text(teacher, maths, "  Počítá do sta.  "));

        var written = In(await CardAs(teacher), maths);
        Assert.Equal("Počítá do sta.", written.VerbalText);
        Assert.False(written.Changed);
        Assert.Equal(2, written.Value);
        Assert.True(written.Complete);

        Assert.Equal(new ConfirmReportCardGradesResult(1, 0), await Confirm(teacher, maths));
        var confirmed = In(await CardAs(teacher), maths);
        Assert.True(confirmed.Confirmed);
        Assert.Equal(2, confirmed.Value);
        Assert.IsType<ConflictObjectResult>(await Text(teacher, maths, "Jinak."));
    }

    [Fact]
    public async Task TheTextIsOnlyForASubjectAssessedSlovneAndOnlyItsTeachersWriteIt()
    {
        Assert.IsType<BadRequestObjectResult>(await Text(teacher, maths, "Počítá."));

        await Rule(AssessmentMode.ClassificationWithVerbal, rocnik: 3);

        Assert.IsType<ForbidResult>(await Text(teacher, czech, "Čte."));
        Assert.IsType<NoContentResult>(await Text(teacher, maths, "Počítá."));
    }

    [Fact]
    public async Task GoingBackToTheSuggestionKeepsTheText()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3, subject: maths);
        await Text(teacher, maths, "Počítá do sta.");
        await As(teacher).SetGrade(new(jan.Id, maths.Id, period, ReportCardGradeKind.Graded, 1));
        Assert.True(In(await CardAs(teacher), maths).Changed);

        Assert.IsType<NoContentResult>(await As(teacher).ResetGrade(jan.Id, maths.Id, period));

        var reset = In(await CardAs(teacher), maths);
        Assert.False(reset.Changed);
        Assert.Equal(2, reset.Value);
        Assert.Equal("Počítá do sta.", reset.VerbalText);

        await Text(teacher, maths, "");
        Assert.Empty(db.ReportCardGrades);
    }

    [Fact]
    public async Task TheFamilyDoesntSeeTheStupenOfASlovniHodnoceni()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3, subject: maths);
        await Rule(AssessmentMode.Verbal, rocnik: 3, behavior: true);
        await Text(teacher, maths, "Počítá do sta.");
        await Confirm(teacher, maths);
        await As(teacher).SetBehaviorText(new(jan.Id, period, "Je ohleduplný."));

        var before = await CardAs(pupil);
        Assert.Null(before.BehaviorText);
        Assert.Null(In(before, maths).VerbalText);

        db.Today = new DateOnly(2027, 1, 31);
        var after = await CardAs(pupil);
        var inMaths = In(after, maths);
        Assert.Equal("Počítá do sta.", inMaths.VerbalText);
        Assert.Equal((ReportCardGradeKind?)ReportCardGradeKind.Graded, inMaths.Kind);
        Assert.Null(inMaths.Value);
        Assert.Null(after.Behavior);
        Assert.Equal("Je ohleduplný.", after.BehaviorText);
    }

    [Fact]
    public async Task TheTridniWritesTheBehavioursText()
    {
        Assert.IsType<BadRequestObjectResult>(await As(teacher).SetBehaviorText(new(jan.Id, period, "Je ohleduplný.")));
        await Rule(AssessmentMode.Verbal, rocnik: 3, behavior: true);

        Assert.IsType<NoContentResult>(await As(teacher).SetBehaviorText(new(jan.Id, period, "Je ohleduplný.")));
        var card = await CardAs(teacher);
        Assert.Equal("Je ohleduplný.", card.BehaviorText);
        Assert.True(card.CanEditBehaviorText);

        schoolClass.ClassTeacherId = null;
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await As(teacher).SetBehaviorText(new(jan.Id, period, "Jinak.")));
    }

    [Fact]
    public async Task TheSummaryStandsInForTheSubjectsInTheFirstHalf()
    {
        var request = new SetReportCardSummaryRequest(jan.Id, period, "Celkově zvládá učivo.", ReportCardResult.Passed);
        Assert.IsType<BadRequestObjectResult>(await As(teacher).SetSummary(request));
        await Rule(AssessmentMode.Verbal, rocnik: 3);
        Assert.Null((await CardAs(management)).Result);

        Assert.IsType<NoContentResult>(await As(teacher).SetSummary(request));
        var written = await CardAs(teacher);
        Assert.Equal("Celkově zvládá učivo.", written.Summary!.Text);
        Assert.False(written.Summary.Confirmed);
        Assert.Null(written.Result);

        Assert.IsType<NoContentResult>(await As(teacher).ConfirmSummary(new(jan.Id, period)));
        var confirmed = await CardAs(teacher);
        Assert.True(confirmed.Summary!.Confirmed);
        Assert.False(confirmed.Summary.CanEdit);
        Assert.Equal(ReportCardResult.Passed, confirmed.Result);
        Assert.IsType<ConflictObjectResult>(await As(teacher).SetSummary(request with { Text = "Jinak." }));
        Assert.IsType<ForbidResult>(await As(teacher).ReopenSummary(new(jan.Id, period)));
        Assert.IsType<NoContentResult>(await As(management).ReopenSummary(new(jan.Id, period)));
        Assert.IsType<NoContentResult>(await As(teacher).SetSummary(request with { Text = null }));
        Assert.Null((await CardAs(teacher)).Summary!.Text);
    }

    [Fact]
    public async Task NoSummaryInTheSecondHalfOrWithAClassifiedSubject()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3);
        await Rule(AssessmentMode.ClassificationWithVerbal, rocnik: 3, subject: czech);

        Assert.IsType<BadRequestObjectResult>(await As(teacher).SetSummary(new(jan.Id, period, "Text", ReportCardResult.Passed)));
        Assert.Null((await CardAs(teacher)).Summary);
        Assert.IsType<BadRequestObjectResult>(await As(teacher).SetSummary(
            new(jan.Id, db.SecondHalf(Year).Id, "Text", ReportCardResult.Passed)));
    }

    [Fact]
    public async Task ListsAAndBAreForPupilsAssessedSlovneAListBGoesOnWithALongText()
    {
        Assert.IsType<BadRequestObjectResult>(await As(management).GetPdf(period, studentId: jan.Id, form: ReportCardPrint.Verbal));

        await Rule(AssessmentMode.Verbal, rocnik: 3);
        await Rule(AssessmentMode.Verbal, rocnik: 3, behavior: true);
        await Text(teacher, maths, string.Join(" ", Enumerable.Repeat("Počítá do sta, sčítá a odčítá s přechodem přes desítku.", 300)));
        await Confirm(teacher, maths);
        await As(teacher).SetBehaviorText(new(jan.Id, period, "Je ohleduplný ke spolužákům."));

        var file = Assert.IsType<FileContentResult>(
            await As(management).GetPdf(period, schoolClassId: schoolClass.Id, form: ReportCardPrint.Verbal));
        Assert.Equal("vysvedceni-list-a-b-3.A.pdf", file.FileDownloadName);
        Assert.Equal(4, Pages(file));
        Assert.IsType<FileContentResult>(await As(management).GetPdf(period, studentId: jan.Id));
    }

    [Fact]
    public async Task APupilAssessedSlovneOnlyInSomeSubjectsGetsAWholeListB()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3, subject: maths);
        await Text(teacher, maths, "Počítá do sta.");
        await Confirm(teacher, maths);

        var file = Assert.IsType<FileContentResult>(
            await As(management).GetPdf(period, studentId: jan.Id, form: ReportCardPrint.Verbal));

        Assert.Equal(2, Pages(file));
    }

    [Fact]
    public async Task TheClassRuleSitsBetweenThePupilsAndTheRocniks()
    {
        await Rule(AssessmentMode.Verbal, rocnik: 3);
        await Rule(AssessmentMode.Classification, classId: schoolClass.Id);
        await Rule(AssessmentMode.ClassificationWithVerbal, rocnik: 3, subject: maths);

        var card = await CardAs(management);
        Assert.Equal(AssessmentMode.Classification, In(card, maths).Mode);
        Assert.Equal(AssessmentMode.Classification, In(card, czech).Mode);

        await Rule(AssessmentMode.Verbal, studentId: jan.Id, subject: czech);
        Assert.Equal(AssessmentMode.Verbal, In(await CardAs(management), czech).Mode);

        var rules = (List<AssessmentModeRuleDto>)Assert.IsType<OkObjectResult>((await Settings().GetAssessmentRules()).Result).Value!;
        Assert.Equal("3.A", rules.Single(r => r.SchoolClassId is not null).SchoolClassName);
        Assert.IsType<BadRequestObjectResult>(await Settings().SetAssessmentRule(
            new(null, schoolClass.Id, jan.Id, null, false, AssessmentMode.Verbal)));
    }

    [Fact]
    public async Task TheMarkFormatComesFromTheMostSpecificRuleThatSetsOne()
    {
        async Task<ReportCard> Card() => Assert.Single(await ReportCards.BuildAsync(db, [jan.Id], (await db.GradingPeriodsAsync()).Report(period)!));
        Assert.IsType<OkObjectResult>((await Settings().SetReportCards(new(ReportCardMarkFormat.Words))).Result);

        Assert.Equal(ReportCardMarkFormat.Words, (await Card()).Subjects[0].MarkFormat);

        await Rule(AssessmentMode.Classification, rocnik: 3, format: ReportCardMarkFormat.Digits);
        await Rule(AssessmentMode.ClassificationWithVerbal, studentId: jan.Id, subject: maths);
        await Rule(AssessmentMode.Classification, classId: schoolClass.Id, behavior: true, format: ReportCardMarkFormat.Words);

        var card = await Card();
        var inMaths = card.Subjects.Single(s => s.SubjectId == maths.Id);
        Assert.Equal(AssessmentMode.ClassificationWithVerbal, inMaths.Mode);
        Assert.Equal(ReportCardMarkFormat.Digits, inMaths.MarkFormat);
        Assert.Equal(ReportCardMarkFormat.Digits, card.Subjects.Single(s => s.SubjectId == czech.Id).MarkFormat);
        Assert.Equal(ReportCardMarkFormat.Words, card.BehaviorMarkFormat);

        await Rule(AssessmentMode.ClassificationWithVerbal, studentId: jan.Id, subject: maths, format: ReportCardMarkFormat.Words);
        Assert.Equal(ReportCardMarkFormat.Words, (await Card()).Subjects.Single(s => s.SubjectId == maths.Id).MarkFormat);
    }
}
