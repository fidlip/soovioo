using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Payments;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Payments;

public class PaymentsControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static PaymentsController NewController(AppDbContext db, int userId, string role) =>
        new(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = TestRoles.Principal(db, userId, Enum.Parse<UserRole>(role), SchoolId, "tester"),
                },
            },
        };

    /// <summary>Two classes, a teacher who owns only class A, a student in
    /// each class, and a parent of each — same shape as EventsControllerTests'
    /// AudienceFixture, reused here for the identical targeting-restriction
    /// logic Payment deliberately mirrors.</summary>
    private sealed class Fixture
    {
        public required SchoolClass ClassA { get; init; }
        public required SchoolClass ClassB { get; init; }
        public required User TeacherA { get; init; }
        public required StudentProfile StudentA { get; init; }
        public required StudentProfile StudentB { get; init; }
        public required User ParentA { get; init; }
        public required User ParentB { get; init; }
        public required User StudentUserA { get; init; }

        public static async Task<Fixture> SeedAsync(AppDbContext db)
        {
            var teacherA = new User { Username = "ucitelA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
            var studentUserA = new User { Username = "zakA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
            db.Users.AddRange(teacherA, studentUserA);
            await db.SaveChangesAsync();

            var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacherA.Id };
            var classB = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
            db.SchoolClasses.AddRange(classA, classB);
            await db.SaveChangesAsync();

            var studentA = new StudentProfile
            {
                Person = new Person { FirstName = "Žák", LastName = "A", UserId = studentUserA.Id, SchoolId = SchoolId },
                SchoolId = SchoolId,
                SchoolClassId = classA.Id,
                DefaultVariableSymbol = "1001",
            };
            var studentB = new StudentProfile { Person = new Person { FirstName = "Žák", LastName = "B", SchoolId = SchoolId }, SchoolId = SchoolId, SchoolClassId = classB.Id };
            db.Students.AddRange(studentA, studentB);
            await db.SaveChangesAsync();

            var parentA = new User { Username = "rodicA", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
            var parentB = new User { Username = "rodicB", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
            db.Users.AddRange(parentA, parentB);
            await db.SaveChangesAsync();

            await db.LinkChildAsync(parentA.Id, studentA.Id, SchoolId);
            await db.LinkChildAsync(parentB.Id, studentB.Id, SchoolId);
            await db.SaveChangesAsync();

            return new Fixture
            {
                ClassA = classA, ClassB = classB, TeacherA = teacherA,
                StudentA = studentA, StudentB = studentB, ParentA = parentA, ParentB = parentB,
                StudentUserA = studentUserA,
            };
        }
    }

    [Fact]
    public async Task EveryoneAudienceGeneratesRecipientsForAllStudents()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Školní výlet", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 250m, PaymentRecurrence.None, PaymentAudience.Everyone));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(2, dto.Recipients.Count);
        Assert.All(dto.Recipients, r => Assert.Equal(250m, r.Amount));
    }

    [Fact]
    public async Task SelectedAudienceWithExplicitStudentsOnlyTargetsThem()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Kroužek", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            StudentIds: [fx.StudentA.Id]));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Single(dto.Recipients);
        Assert.Equal(fx.StudentA.Id, dto.Recipients.Single().StudentId);
    }

    [Fact]
    public async Task TotalSplitEvenlyDividesAmongTargetedStudents()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Společný dárek", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.TotalSplitEvenly, 100m, PaymentRecurrence.None, PaymentAudience.Everyone));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(2, dto.Recipients.Count);
        Assert.All(dto.Recipients, r => Assert.Equal(50m, r.Amount));
    }

    [Fact]
    public async Task EffectiveVariableSymbolFallsBackToStudentDefault()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Platba", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            StudentIds: [fx.StudentA.Id]));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        var recipient = dto.Recipients.Single();
        Assert.Null(recipient.VariableSymbol);
        Assert.Equal("1001", recipient.EffectiveVariableSymbol);
    }

    [Fact]
    public async Task TeacherCannotTargetClassTheyDoNotOwn()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, fx.TeacherA.Id, "Teacher").CreatePayment(new CreatePaymentRequest(
            "Mimo dosah", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            SchoolClassIds: [fx.ClassB.Id]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TeacherCanTargetTheirOwnClass()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, fx.TeacherA.Id, "Teacher").CreatePayment(new CreatePaymentRequest(
            "Pro vlastní třídu", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            SchoolClassIds: [fx.ClassA.Id]));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Single(dto.Recipients);
        Assert.Equal(fx.StudentA.Id, dto.Recipients.Single().StudentId);
    }

    [Fact]
    public async Task ParentOnlySeesTheirOwnChildsRecipientRowWithinASharedPayment()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Pro celou školu", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Everyone));

        var parentAResult = await NewController(db, fx.ParentA.Id, "Parent").GetPayments();

        var payments = Assert.IsAssignableFrom<IEnumerable<PaymentDto>>(
            Assert.IsType<OkObjectResult>(parentAResult.Result).Value);
        var payment = payments.Single();
        Assert.Single(payment.Recipients);
        Assert.Equal(fx.StudentA.Id, payment.Recipients.Single().StudentId);
    }

    [Fact]
    public async Task ParentWithNoMatchingRecipientDoesNotSeeThePaymentAtAll()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);
        await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Jen pro třídu A", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            SchoolClassIds: [fx.ClassA.Id]));

        var parentBResult = await NewController(db, fx.ParentB.Id, "Parent").GetPayments();

        var payments = Assert.IsAssignableFrom<IEnumerable<PaymentDto>>(
            Assert.IsType<OkObjectResult>(parentBResult.Result).Value);
        Assert.Empty(payments);
    }

    [Fact]
    public async Task SetRecipientPaidPersistsPaidAtAndCanBeUnset()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);
        var createResult = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Platba", null, new DateOnly(2026, 11, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.None, PaymentAudience.Selected,
            StudentIds: [fx.StudentA.Id]));
        var created = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(createResult.Result).Value);
        var recipientId = created.Recipients.Single().Id;

        var paidResult = await NewController(db, 999, "Admin")
            .SetRecipientPaid(recipientId, new SetPaymentRecipientPaidRequest(true));
        var paidDto = Assert.IsType<PaymentRecipientDto>(Assert.IsType<OkObjectResult>(paidResult.Result).Value);
        Assert.True(paidDto.Paid);
        Assert.NotNull(paidDto.PaidAt);

        var unpaidResult = await NewController(db, 999, "Admin")
            .SetRecipientPaid(recipientId, new SetPaymentRecipientPaidRequest(false));
        var unpaidDto = Assert.IsType<PaymentRecipientDto>(Assert.IsType<OkObjectResult>(unpaidResult.Result).Value);
        Assert.False(unpaidDto.Paid);
        Assert.Null(unpaidDto.PaidAt);
    }

    [Fact]
    public async Task RecurringPaymentSetsNextOccurrenceDueDateWithinRecurrenceEndDate()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Kroužek měsíčně", null, new DateOnly(2026, 9, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.Monthly, PaymentAudience.Everyone,
            RecurrenceEndDate: new DateOnly(2027, 6, 30)));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        var saved = await db.Payments.FindAsync(dto.Id);
        Assert.Equal(new DateOnly(2026, 10, 1), saved!.NextOccurrenceDueDate);
    }

    [Fact]
    public async Task RecurringPaymentClearsNextOccurrenceDueDateWhenBeyondRecurrenceEndDate()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);

        var result = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Poslední splátka", null, new DateOnly(2027, 6, 15),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.Monthly, PaymentAudience.Everyone,
            RecurrenceEndDate: new DateOnly(2027, 6, 30)));

        var dto = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        var saved = await db.Payments.FindAsync(dto.Id);
        Assert.Null(saved!.NextOccurrenceDueDate);
    }

    [Fact]
    public async Task RecurringJobGeneratesSecondInstallmentAndAdvancesCursor()
    {
        using var db = NewDb();
        var fx = await Fixture.SeedAsync(db);
        var createResult = await NewController(db, 999, "Admin").CreatePayment(new CreatePaymentRequest(
            "Kroužek měsíčně", null, new DateOnly(2026, 9, 1),
            PaymentAmountType.FixedPerStudent, 100m, PaymentRecurrence.Monthly, PaymentAudience.Everyone,
            RecurrenceEndDate: new DateOnly(2027, 6, 30)));
        var created = Assert.IsType<PaymentDto>(Assert.IsType<CreatedAtActionResult>(createResult.Result).Value);

        // The job's date-based "is it due yet" check uses DateTime.UtcNow, so
        // force NextOccurrenceDueDate into the past to make it due right now
        // regardless of when this test actually runs.
        var payment = await db.Payments.FindAsync(created.Id);
        payment!.NextOccurrenceDueDate = new DateOnly(2000, 1, 1);
        await db.SaveChangesAsync();

        await new PaymentRecurringInstallmentsJob(db).RunAsync();

        var recipients = await db.PaymentRecipients.Where(r => r.PaymentId == created.Id).ToListAsync();
        Assert.Equal(4, recipients.Count); // 2 students x 2 installments (creation + job run)
        var refreshed = await db.Payments.FindAsync(created.Id);
        Assert.Equal(new DateOnly(2000, 2, 1), refreshed!.NextOccurrenceDueDate);
    }
}
