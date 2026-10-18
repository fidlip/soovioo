using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Payments;

/// <summary>Resolves a Payment's targeting criteria into actual student ids
/// and appends a fresh <see cref="PaymentRecipient"/> batch — shared between
/// the synchronous first-installment generation in
/// <see cref="PaymentsController.CreatePayment"/> and the recurring
/// <see cref="PaymentRecurringInstallmentsJob"/>, which runs with no
/// authenticated caller/HTTP context. Every query here therefore uses
/// <c>IgnoreQueryFilters()</c> plus an explicit SchoolId check, exactly like
/// <see cref="Events.CalendarFeedController"/>'s documented pattern for
/// running outside a request-scoped <see cref="ICurrentSchoolContext"/>.</summary>
public static class PaymentRecipientGenerator
{
    /// <summary>Appends new <see cref="PaymentRecipient"/> rows to
    /// <paramref name="payment"/>.Recipients (the navigation collection, not
    /// <c>db.PaymentRecipients.Add</c> with a manual FK) — this lets a single
    /// <c>SaveChangesAsync</c> work whether the Payment is brand new or
    /// already persisted. Does NOT save — that's the caller's job.</summary>
    public static async Task GenerateBatchAsync(AppDbContext db, Payment payment, DateOnly dueDate)
    {
        var studentIds = await ResolveTargetStudentIdsAsync(db, payment);
        if (studentIds.Count == 0)
        {
            return;
        }

        // TotalSplitEvenly: remainder cents from the division aren't
        // redistributed — a deliberate simplification, not a bug. A real
        // bank reconciliation only cares that the TOTAL roughly matches;
        // exact-to-the-cent splitting isn't in scope.
        var amount = payment.AmountType == PaymentAmountType.TotalSplitEvenly
            ? Math.Round(payment.Amount / studentIds.Count, 2, MidpointRounding.AwayFromZero)
            : payment.Amount;

        foreach (var studentId in studentIds)
        {
            payment.Recipients.Add(new PaymentRecipient
            {
                StudentId = studentId,
                SchoolId = payment.SchoolId,
                Amount = amount,
                InstallmentDueDate = dueDate,
            });
        }
    }

    private static async Task<List<int>> ResolveTargetStudentIdsAsync(AppDbContext db, Payment payment)
    {
        if (payment.Audience == PaymentAudience.Everyone)
        {
            return await db.Students.IgnoreQueryFilters()
                .Where(s => s.SchoolId == payment.SchoolId)
                .Select(s => s.Id)
                .ToListAsync();
        }

        var classIds = payment.SchoolClasses.Select(c => c.SchoolClassId).ToList();
        var directStudentIds = payment.Students.Select(s => s.StudentId).ToList();
        var parentUserIds = payment.Parents.Select(p => p.UserId).ToList();

        var fromClasses = await db.Students.IgnoreQueryFilters()
            .Where(s => s.SchoolId == payment.SchoolId && classIds.Contains(s.SchoolClassId))
            .Select(s => s.Id)
            .ToListAsync();
        var fromParents = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == payment.SchoolId
                && ps.ParentProfile!.Person!.UserId != null
                && parentUserIds.Contains(ps.ParentProfile.Person.UserId.Value))
            .Select(ps => ps.StudentId)
            .ToListAsync();

        return fromClasses.Concat(directStudentIds).Concat(fromParents).Distinct().ToList();
    }

    /// <summary>Shared by the controller (computing the second installment's
    /// due date at creation) and the job (advancing past each installment it
    /// generates). Never called for <see cref="PaymentRecurrence.None"/> —
    /// that payment only ever has the one, synchronously-generated batch.</summary>
    public static DateOnly Advance(DateOnly date, PaymentRecurrence recurrence) => recurrence switch
    {
        PaymentRecurrence.Monthly => date.AddMonths(1),
        PaymentRecurrence.Semiannually => date.AddMonths(6),
        _ => throw new InvalidOperationException("Advance must not be called for a non-recurring payment."),
    };

    /// <summary>Default end-of-recurrence date when the creator doesn't
    /// supply one: June 30 of the nearest currently-running Czech school
    /// year. The school doesn't have its own SchoolYear entity/setting yet
    /// (see ROADMAP), so this is derived purely from today's date — same
    /// trick as the frontend's defaultSchoolYearStartYear()/defaultHalfYear()
    /// helpers in behavior-page.ts.</summary>
    public static DateOnly DefaultSchoolYearEndDate(DateOnly today) =>
        today.Month >= 8 ? new DateOnly(today.Year + 1, 6, 30) : new DateOnly(today.Year, 6, 30);
}
