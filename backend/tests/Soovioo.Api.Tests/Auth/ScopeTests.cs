using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Homework;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Payments;
using Soovioo.Api.People;
using HomeworkEntity = Soovioo.Api.Homework.Homework;

namespace Soovioo.Api.Tests.Auth;

/// <summary>Every check inside an endpoint that narrows a permission to
/// classes or pupils, both ways, with the default teacher (třídní of 5.A,
/// teaches maths in 6.B, nothing in 7.C), a parent of Jan (5.A), Jan himself
/// and the parents' representative. The gates on the endpoints themselves
/// are <see cref="AuthorizationMapTests"/>.</summary>
public class ScopeTests
{
    private const int SchoolId = 1;
    private const int TeacherId = 7;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly SchoolClass a = new() { Name = "5.A", SchoolId = SchoolId, ClassTeacherId = TeacherId };
    private readonly SchoolClass b = new() { Name = "6.B", SchoolId = SchoolId };
    private readonly SchoolClass c = new() { Name = "7.C", SchoolId = SchoolId };
    private readonly Subject maths = new() { Name = "Matematika", SchoolId = SchoolId };
    private readonly Subject czech = new() { Name = "Čeština", SchoolId = SchoolId };
    private StudentProfile jan = null!, eva = null!, oto = null!;
    private User parent = null!, pupil = null!;

    public ScopeTests()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        db.AddRange(a, b, c, maths, czech);
        db.SaveChanges();
        jan = Pupil("Jan", a);
        eva = Pupil("Eva", b);
        oto = Pupil("Oto", c);
        TestRoles.Principal(db, TeacherId, UserRole.Teacher);
        db.Teaches(TeacherId, b.Id, maths.Id);
        parent = new User { Username = "rodic", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = db.Role(UserRole.Parent) };
        pupil = new User { Username = "zak", PasswordHash = "x", SchoolId = SchoolId, ActiveRole = db.Role(UserRole.Student) };
        db.Users.AddRange(parent, pupil);
        db.SaveChanges();
        jan.Person!.UserId = pupil.Id;
        db.LinkChildAsync(parent.Id, jan.Id, SchoolId).GetAwaiter().GetResult();
        db.SaveChanges();
    }

    private StudentProfile Pupil(string name, SchoolClass schoolClass)
    {
        var student = new StudentProfile
        {
            Person = new Person { FirstName = name, LastName = "Žák", SchoolId = SchoolId },
            SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    private ClaimsPrincipal Teacher => TestRoles.Principal(db, TeacherId, UserRole.Teacher);
    private ClaimsPrincipal Admin => TestRoles.Principal(db, 99, UserRole.Admin);
    private ClaimsPrincipal Parent => TestRoles.PrincipalOf(parent);
    private ClaimsPrincipal PupilJan => TestRoles.PrincipalOf(pupil);

    private T As<T>(T controller, ClaimsPrincipal user) where T : ControllerBase
    {
        controller.ControllerContext = TestRoles.Context(user);
        return controller;
    }

    private GradesController Grades(ClaimsPrincipal user) =>
        As(new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)), user);

    private AbsencesController Absences(ClaimsPrincipal user) =>
        As(new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System), TimeProvider.System,
            new MessageAttachmentStorageService(new FakeWebHostEnvironment())), user);

    private BehaviorController Behavior(ClaimsPrincipal user) =>
        As(new BehaviorController(db, new FakeCurrentSchoolContext(SchoolId)), user);

    private HomeworkController Homework(ClaimsPrincipal user) =>
        As(new HomeworkController(db, new FakeCurrentSchoolContext(SchoolId),
            new MessageAttachmentStorageService(new FakeWebHostEnvironment()), new Notifier(db, TimeProvider.System)), user);

    private EventsController Events(ClaimsPrincipal user) =>
        As(new EventsController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)), user);

    private PaymentsController Payments(ClaimsPrincipal user) =>
        As(new PaymentsController(db, new FakeCurrentSchoolContext(SchoolId)), user);

    private static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    /// <summary>A role of the school's own, with only <paramref name="permission"/> in <paramref name="scope"/>.</summary>
    private ClaimsPrincipal OwnRole(int userId, RoleKind kind, Permission permission, PermissionScope scope)
    {
        var role = new Role { SchoolId = SchoolId, Name = $"Role {Guid.NewGuid()}", Kind = kind };
        role.Permissions.Add(new RolePermission { Permission = permission, Scope = scope });
        db.Roles.Add(role);
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            user = new User { Id = userId, Username = $"u{userId}", PasswordHash = "x", SchoolId = SchoolId };
            db.Users.Add(user);
        }
        user.ActiveRole = role;
        db.SaveChanges();
        return TestRoles.PrincipalOf(user);
    }

    /// <summary>Another parent of Jan, in a role of the school's own.</summary>
    private ClaimsPrincipal Representative(Permission permission)
    {
        var rep = OwnRole(50, RoleKind.Parent, permission, PermissionScope.Own);
        db.LinkChildAsync(50, jan.Id, SchoolId).GetAwaiter().GetResult();
        db.SaveChanges();
        return rep;
    }

    // ---- the scopes themselves ------------------------------------------

    [Theory]
    [InlineData(PermissionScope.None, new string[0])]
    [InlineData(PermissionScope.ClassTeacher, new[] { "Jan" })]
    [InlineData(PermissionScope.MyClasses, new[] { "Jan", "Eva" })]
    [InlineData(PermissionScope.School, new[] { "Jan", "Eva", "Oto" })]
    public async Task AStaffScopeReachesItsPupils(PermissionScope scope, string[] reached)
    {
        var user = OwnRole(TeacherId, RoleKind.Staff, Permission.GradesRead, scope);

        foreach (var student in new[] { jan, eva, oto })
        {
            Assert.Equal(reached.Contains(student.Person!.FirstName),
                await user.ReachesStudentAsync(db, Permission.GradesRead, student.Id));
        }
    }

    [Fact]
    public async Task OwnIsTheChildrenOfAParentAndThePupilThemselves()
    {
        Assert.Equal([jan.Id], await Parent.StudentIdsAsync(db, Permission.GradesRead));
        Assert.Equal([jan.Id], await PupilJan.StudentIdsAsync(db, Permission.GradesRead));
        Assert.Equal([a.Id], await Parent.ClassIdsAsync(db, Permission.GradesRead));
    }

    // ---- grades ---------------------------------------------------------

    [Fact]
    public async Task GradesListShowsWhatTheReadScopeReaches()
    {
        foreach (var student in new[] { jan, eva, oto })
        {
            db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = maths.Id, Value = 1, SchoolId = SchoolId });
        }
        await db.SaveChangesAsync();

        Assert.Equal([jan.Id, eva.Id], Ok(await Grades(Teacher).GetGrades()).Items.Select(g => g.StudentId).Order());
        Assert.Equal([jan.Id], Ok(await Grades(Parent).GetGrades()).Items.Select(g => g.StudentId));
        Assert.Equal(3, Ok(await Grades(Admin).GetGrades()).TotalCount);
        // A filter doesn't reach past the scope.
        Assert.Empty(Ok(await Grades(Parent).GetGrades(studentId: eva.Id)).Items);
    }

    [Fact]
    public async Task ATeacherChangesOnlyGradesOfWhatTheyTeach()
    {
        var mine = new Grade { StudentId = eva.Id, SubjectId = maths.Id, Value = 2, SchoolId = SchoolId };
        var notMine = new Grade { StudentId = jan.Id, SubjectId = czech.Id, Value = 2, SchoolId = SchoolId };
        db.Grades.AddRange(mine, notMine);
        await db.SaveChangesAsync();

        Assert.IsType<ForbidResult>((await Grades(Teacher).AddGrade(new(jan.Id, maths.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(Teacher).UpdateGrade(notMine.Id, new(jan.Id, czech.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>((await Grades(Teacher).UpdateGrade(mine.Id, new(oto.Id, maths.Id, 1, 1, null))).Result);
        Assert.IsType<ForbidResult>(await Grades(Teacher).DeleteGrade(notMine.Id));
        Assert.IsType<OkObjectResult>((await Grades(Teacher).UpdateGrade(mine.Id, new(eva.Id, maths.Id, 1, 1, null))).Result);
        Assert.IsType<NoContentResult>(await Grades(Teacher).DeleteGrade(mine.Id));
    }

    [Fact]
    public async Task AReportCardOnlyForPupilsWithinReach()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var reportCard = (ClaimsPrincipal user) =>
            As(new ReportCardController(db, new FakeCurrentSchoolContext(SchoolId)), user);

        Assert.IsType<ForbidResult>(await reportCard(Teacher).GetReportCard(oto.Id));
        Assert.IsType<ForbidResult>(await reportCard(Parent).GetReportCard(eva.Id));
        Assert.IsNotType<ForbidResult>(await reportCard(Parent).GetReportCard(jan.Id));
    }

    // ---- absences -------------------------------------------------------

    private async Task<Absence> AbsenceOf(StudentProfile student, int day = 1)
    {
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 10, day), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();
        return absence;
    }

    [Fact]
    public async Task AbsencesAreReadAndWrittenWithinReach()
    {
        var janAbsence = await AbsenceOf(jan);
        var otoAbsence = await AbsenceOf(oto);

        Assert.Equal([janAbsence.Id], Ok(await Absences(Parent).GetAbsences()).Select(x => x.Id));
        Assert.Equal([janAbsence.Id], Ok(await Absences(Teacher).GetAbsences()).Select(x => x.Id));
        Assert.IsType<ForbidResult>((await Absences(Teacher).AddAbsence(new(oto.Id, new DateOnly(2026, 10, 2)))).Result);
        Assert.IsType<ForbidResult>((await Absences(Teacher).UpdateAbsence(otoAbsence.Id, new(oto.Id, new DateOnly(2026, 10, 2)))).Result);
        Assert.IsType<ForbidResult>((await Absences(Teacher).UpdateAbsence(janAbsence.Id, new(oto.Id, new DateOnly(2026, 10, 2)))).Result);
        Assert.IsType<ForbidResult>(await Absences(Teacher).ReviewAbsence(otoAbsence.Id, new(true)));
        Assert.IsType<ForbidResult>(await Absences(Teacher).DeleteAbsence(otoAbsence.Id));
        Assert.IsType<CreatedAtActionResult>((await Absences(Teacher).AddAbsence(new(eva.Id, new DateOnly(2026, 10, 2)))).Result);
        Assert.IsType<NoContentResult>(await Absences(Parent).SubmitExcuse(janAbsence.Id, new("nemoc")));
        Assert.IsType<NoContentResult>(await Absences(Teacher).ReviewAbsence(janAbsence.Id, new(true)));
    }

    [Fact]
    public async Task AnExcuseComesFromTheParentOrTheClassTeacher()
    {
        var janAbsence = await AbsenceOf(jan);
        var evaAbsence = await AbsenceOf(eva);

        var janOther = await AbsenceOf(jan, 2);

        Assert.IsType<NoContentResult>(await Absences(Parent).SubmitExcuse(janAbsence.Id, new("nemoc")));
        Assert.IsType<NoContentResult>(await Absences(Teacher).SubmitExcuse(janOther.Id, new("nemoc")));
        // Teaches 6.B, but isn't its třídní.
        Assert.IsType<ForbidResult>(await Absences(Teacher).SubmitExcuse(evaAbsence.Id, new("nemoc")));
        Assert.IsType<ForbidResult>(await Absences(Parent).SubmitExcuse(evaAbsence.Id, new("nemoc")));
        Assert.IsType<ForbidResult>(await Absences(PupilJan).SubmitExcuse(janAbsence.Id, new("nemoc")));
    }

    // ---- behavior -------------------------------------------------------

    [Fact]
    public async Task EachKindOfBehaviorRecordHasItsReach()
    {
        async Task<bool> Records(ClaimsPrincipal user, StudentProfile student, BehaviorRecordType type) =>
            (await Behavior(user).CreateBehaviorRecord(new(student.Id, type, "Text", new DateOnly(2026, 10, 1)))).Result
                is CreatedAtActionResult;

        Assert.True(await Records(Teacher, eva, BehaviorRecordType.Pochvala));
        Assert.False(await Records(Teacher, oto, BehaviorRecordType.Poznamka));
        Assert.True(await Records(Teacher, jan, BehaviorRecordType.DutkaTridnihoUcitele));
        Assert.False(await Records(Teacher, eva, BehaviorRecordType.NapomenutiTridnihoUcitele));
        Assert.False(await Records(Teacher, jan, BehaviorRecordType.DutkaReditele));
        Assert.True(await Records(Admin, oto, BehaviorRecordType.DutkaReditele));
    }

    [Fact]
    public async Task BehaviorRecordsAreReadDeletedAndConfirmedWithinReach()
    {
        BehaviorRecord Record(StudentProfile student, BehaviorRecordType type) => new()
        {
            StudentId = student.Id, Type = type, Text = "Text", Date = new DateOnly(2026, 10, 1),
            RecordedByName = "x", SchoolId = SchoolId,
        };
        var otoPraise = Record(oto, BehaviorRecordType.Pochvala);
        var janDirector = Record(jan, BehaviorRecordType.DutkaReditele);
        var evaPraise = Record(eva, BehaviorRecordType.Pochvala);
        db.BehaviorRecords.AddRange(otoPraise, janDirector, evaPraise);
        await db.SaveChangesAsync();

        Assert.Equal([janDirector.Id, evaPraise.Id], Ok(await Behavior(Teacher).GetBehaviorRecords()).Select(r => r.Id).Order());
        Assert.Equal([janDirector.Id], Ok(await Behavior(Parent).GetBehaviorRecords()).Select(r => r.Id));
        Assert.IsType<ForbidResult>(await Behavior(Teacher).MarkBehaviorRecordRead(otoPraise.Id));
        Assert.IsType<ForbidResult>(await Behavior(Parent).MarkBehaviorRecordRead(evaPraise.Id));
        Assert.IsType<ForbidResult>(await Behavior(Teacher).DeleteBehaviorRecord(otoPraise.Id));
        Assert.IsType<ForbidResult>(await Behavior(Teacher).DeleteBehaviorRecord(janDirector.Id));
        Assert.IsType<NoContentResult>(await Behavior(Teacher).DeleteBehaviorRecord(evaPraise.Id));
    }

    [Fact]
    public async Task ClassificationGoesWithTheMeasures()
    {
        SetBehaviorClassificationRequest Of(StudentProfile s, BehaviorClassificationValue v) => new(s.Id, 2026, SchoolHalfYear.First, v);

        Assert.IsType<OkObjectResult>((await Behavior(Teacher).SetClassification(Of(jan, BehaviorClassificationValue.VelmiDobre))).Result);
        Assert.IsType<ForbidResult>((await Behavior(Teacher).SetClassification(Of(eva, BehaviorClassificationValue.VelmiDobre))).Result);
        Assert.IsType<ForbidResult>((await Behavior(Teacher).SetClassification(Of(jan, BehaviorClassificationValue.Uspokojive))).Result);
        Assert.IsType<OkObjectResult>((await Behavior(Admin).SetClassification(Of(oto, BehaviorClassificationValue.Neuspokojive))).Result);

        Assert.Equal([jan.Id], Ok(await Behavior(Teacher).GetBehaviorClassifications()).Select(x => x.StudentId));
        Assert.Equal([jan.Id], Ok(await Behavior(Parent).GetBehaviorClassifications()).Select(x => x.StudentId));
    }

    // ---- homework -------------------------------------------------------

    private async Task<HomeworkEntity> HomeworkOf(SchoolClass schoolClass, Subject subject)
    {
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 9),
            CreatedByName = "x", SchoolId = SchoolId, RequiresCompletion = true,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        return homework;
    }

    [Fact]
    public async Task HomeworkIsSetOnlyInSubjectsTheTeacherTeachesThere()
    {
        var otherSubject = await HomeworkOf(b, czech);
        var mine = await HomeworkOf(b, maths);

        Assert.IsType<ForbidResult>((await Homework(Teacher).CreateHomework(new(b.Id, czech.Id, "x", new DateOnly(2026, 10, 9), false))).Result);
        Assert.IsType<ForbidResult>((await Homework(Teacher).CreateHomework(new(a.Id, maths.Id, "x", new DateOnly(2026, 10, 9), false))).Result);
        Assert.IsType<ForbidResult>((await Homework(Teacher).UpdateHomework(otherSubject.Id, new(b.Id, maths.Id, "x", new DateOnly(2026, 10, 9), false))).Result);
        Assert.IsType<ForbidResult>((await Homework(Teacher).UpdateHomework(mine.Id, new(c.Id, maths.Id, "x", new DateOnly(2026, 10, 9), false))).Result);
        Assert.IsType<ForbidResult>((await Homework(Teacher).UploadAttachment(otherSubject.Id, null!)).Result);
        Assert.IsType<ForbidResult>(await Homework(Teacher).DeleteHomework(otherSubject.Id));
        Assert.IsType<CreatedAtActionResult>((await Homework(Teacher).CreateHomework(new(b.Id, maths.Id, "x", new DateOnly(2026, 10, 9), false))).Result);
    }

    [Fact]
    public async Task HomeworkIsSeenDownloadedAndCompletedWithinReach()
    {
        var inA = await HomeworkOf(a, czech);
        var inC = await HomeworkOf(c, czech);

        Assert.Equal([inA.Id], Ok(await Homework(Teacher).GetHomework()).Select(h => h.Id));
        Assert.Equal([inA.Id], Ok(await Homework(Parent).GetHomework()).Select(h => h.Id));
        Assert.IsType<ForbidResult>(await Homework(Teacher).DownloadAttachment(inC.Id, 1));
        Assert.IsType<ForbidResult>(await Homework(Parent).DownloadAttachment(inC.Id, 1));
        Assert.IsType<ForbidResult>(await Homework(Teacher).MarkComplete(inC.Id, new(oto.Id)));
        Assert.IsType<ForbidResult>(await Homework(Parent).MarkComplete(inC.Id, new(oto.Id)));
        Assert.IsType<NoContentResult>(await Homework(PupilJan).MarkComplete(inA.Id, new(jan.Id)));
    }

    // ---- events ---------------------------------------------------------

    private static CreateEventRequest EventFor(EventAudience audience, IReadOnlyList<int>? classIds = null) => new(
        "Výlet", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, audience, SchoolClassIds: classIds);

    [Fact]
    public async Task ATeacherAddressesOnlyTheirClassesAndChangesOnlyTheirEvents()
    {
        Assert.IsType<BadRequestObjectResult>((await Events(Teacher).CreateEvent(EventFor(EventAudience.Everyone))).Result);
        Assert.IsType<BadRequestObjectResult>((await Events(Teacher).CreateEvent(EventFor(EventAudience.Selected, [c.Id]))).Result);
        var created = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(
            (await Events(Teacher).CreateEvent(EventFor(EventAudience.Selected, [b.Id]))).Result).Value);
        var admins = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(
            (await Events(Admin).CreateEvent(EventFor(EventAudience.Everyone))).Result).Value);

        Assert.IsType<ForbidResult>((await Events(Teacher).UpdateEvent(admins.Id, EventFor(EventAudience.OnlyMe))).Result);
        Assert.IsType<ForbidResult>(await Events(Teacher).DeleteEvent(admins.Id));
        Assert.IsType<OkObjectResult>((await Events(Teacher).UpdateEvent(created.Id, EventFor(EventAudience.OnlyMe))).Result);
        Assert.IsType<NoContentResult>(await Events(Admin).DeleteEvent(created.Id));

        var options = Ok(await Events(Teacher).GetRecipientOptions());
        Assert.Equal([a.Id, b.Id], options.Classes.Select(x => x.Id).Order());
        Assert.Empty(options.Teachers);
    }

    [Fact]
    public async Task AParentsRepresentativeAddressesTheirChildrensClassesAndSeesTheirEvents()
    {
        var rep = Representative(Permission.EventsWrite);

        Assert.IsType<BadRequestObjectResult>((await Events(rep).CreateEvent(EventFor(EventAudience.Selected, [b.Id]))).Result);
        Assert.IsType<CreatedAtActionResult>((await Events(rep).CreateEvent(EventFor(EventAudience.Selected, [a.Id]))).Result);
        var mine = Assert.IsType<EventDto>(Assert.IsType<CreatedAtActionResult>(
            (await Events(rep).CreateEvent(EventFor(EventAudience.OnlyMe))).Result).Value);

        Assert.Equal([a.Id], Ok(await Events(rep).GetRecipientOptions()).Classes.Select(x => x.Id));
        Assert.Contains(Ok(await Events(rep).GetEvents(null, null)), e => e.Id == mine.Id);
        Assert.DoesNotContain(Ok(await Events(Parent).GetEvents(null, null)), e => e.Id == mine.Id);
    }

    [Fact]
    public async Task TheCalendarFeedSeesWhatTheAppSees()
    {
        var hidden = new Event
        {
            Title = "Jen pro 7.C", SchoolId = SchoolId, CreatedByUserId = 99, Audience = EventAudience.Selected,
            StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        hidden.SchoolClasses.Add(new EventSchoolClass { SchoolClassId = c.Id, SchoolId = SchoolId });
        db.Events.Add(hidden);
        TestRoles.Principal(db, 99, UserRole.Admin);
        var teacher = await db.Users.SingleAsync(u => u.Id == TeacherId);
        teacher.CalendarToken = "tok-teacher";
        var admin = await db.Users.SingleAsync(u => u.Id == 99);
        admin.CalendarToken = "tok-admin";
        await db.SaveChangesAsync();
        var feed = new CalendarFeedController(db);

        async Task<string> Ics(string token) =>
            Assert.IsType<ContentResult>(await feed.GetFeed(token)).Content!;

        Assert.Contains("Jen pro 7.C", await Ics("tok-admin"));
        Assert.DoesNotContain("Jen pro 7.C", await Ics("tok-teacher"));
    }

    // ---- payments -------------------------------------------------------

    private async Task<(Payment Payment, PaymentRecipient Eva, PaymentRecipient Oto)> PaymentByAdminAsync()
    {
        var payment = new Payment { Title = "Výlet", SchoolId = SchoolId, CreatedByUserId = 99, Audience = PaymentAudience.Selected, Amount = 100 };
        var forEva = new PaymentRecipient { StudentId = eva.Id, SchoolId = SchoolId, Amount = 100 };
        var forOto = new PaymentRecipient { StudentId = oto.Id, SchoolId = SchoolId, Amount = 100 };
        payment.Recipients.Add(forEva);
        payment.Recipients.Add(forOto);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (payment, forEva, forOto);
    }

    [Fact]
    public async Task ATeacherManagesRecipientsWithinReachAndDeletesOnlyTheirPayments()
    {
        var (payment, forEva, forOto) = await PaymentByAdminAsync();

        Assert.IsType<ForbidResult>((await Payments(Teacher).SetRecipientPaid(forOto.Id, new(true))).Result);
        Assert.IsType<ForbidResult>((await Payments(Teacher).SetRecipientVariableSymbol(forOto.Id, new("1"))).Result);
        Assert.IsType<OkObjectResult>((await Payments(Teacher).SetRecipientPaid(forEva.Id, new(true))).Result);
        Assert.IsType<OkObjectResult>((await Payments(Teacher).SetRecipientVariableSymbol(forEva.Id, new("1"))).Result);
        Assert.IsType<ForbidResult>(await Payments(Teacher).DeletePayment(payment.Id));
        Assert.Equal([forEva.Id], Ok(await Payments(Teacher).GetPayments()).Single().Recipients.Select(r => r.Id));
    }

    [Fact]
    public async Task AParentsRepresentativeBillsTheirChildsClassAndManagesWhatTheyCreated()
    {
        var rep = Representative(Permission.PaymentsWrite);
        CreatePaymentRequest For(int classId) => new(
            "Dárek", null, new DateOnly(2026, 11, 1), PaymentAmountType.FixedPerStudent, 50, PaymentRecurrence.None,
            PaymentAudience.Selected, SchoolClassIds: [classId]);

        Assert.IsType<BadRequestObjectResult>((await Payments(rep).CreatePayment(For(b.Id))).Result);
        var created = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(
            (await Payments(rep).CreatePayment(For(a.Id))).Result).Value);
        // A classmate of Jan's, to see the creator gets every recipient.
        var classmate = Pupil("Ema", a);
        var forClassmate = new PaymentRecipient { PaymentId = created.Id, StudentId = classmate.Id, SchoolId = SchoolId, Amount = 50 };
        db.PaymentRecipients.Add(forClassmate);
        await db.SaveChangesAsync();

        Assert.Contains(Ok(await Payments(rep).GetPayments()).Single(p => p.Id == created.Id).Recipients, r => r.Id == forClassmate.Id);
        Assert.IsType<OkObjectResult>((await Payments(rep).SetRecipientPaid(forClassmate.Id, new(true))).Result);
        Assert.DoesNotContain(Ok(await Payments(Parent).GetPayments()).SelectMany(p => p.Recipients), r => r.Id == forClassmate.Id);
        Assert.IsType<NoContentResult>(await Payments(rep).DeletePayment(created.Id));
    }
}
