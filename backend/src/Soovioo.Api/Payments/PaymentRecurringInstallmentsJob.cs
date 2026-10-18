using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Payments;

/// <summary>The first real Hangfire job in this codebase (registered as a
/// daily RecurringJob in Program.cs). Generates the next installment for
/// every recurring Payment whose <see cref="Payment.NextOccurrenceDueDate"/>
/// has arrived, then advances (or clears) that cursor. Runs with no
/// authenticated caller — see <see cref="PaymentRecipientGenerator"/>'s doc
/// comment for why every query here uses IgnoreQueryFilters() explicitly.</summary>
public class PaymentRecurringInstallmentsJob(AppDbContext db)
{
    public async Task RunAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var duePayments = await db.Payments.IgnoreQueryFilters()
            .Where(p => p.NextOccurrenceDueDate != null && p.NextOccurrenceDueDate <= today)
            .Include(p => p.SchoolClasses)
            .Include(p => p.Parents)
            .Include(p => p.Students)
            .ToListAsync();

        foreach (var payment in duePayments)
        {
            var dueDate = payment.NextOccurrenceDueDate!.Value;
            await PaymentRecipientGenerator.GenerateBatchAsync(db, payment, dueDate);

            var next = PaymentRecipientGenerator.Advance(dueDate, payment.Recurrence);
            payment.NextOccurrenceDueDate = payment.RecurrenceEndDate is { } end && next > end ? null : next;
        }

        await db.SaveChangesAsync();
    }
}
