using Sova.Api.Evidence;

namespace Sova.Api.Payments;

/// <summary>One student's share of one installment of a <see cref="Payment"/>.
/// <see cref="Amount"/> is frozen at generation time (see
/// <see cref="PaymentRecipientGenerator"/>) — it never changes afterwards,
/// even if the Payment's targeting changes later. <see cref="VariableSymbol"/>
/// is deliberately NOT frozen the same way: null means "use this student's
/// current <see cref="Student.DefaultVariableSymbol"/>", resolved fresh every
/// time it's displayed/exported, so fixing a student's default VS later
/// also fixes it retroactively on already-generated recipients.</summary>
public class PaymentRecipient
{
    public int Id { get; set; }

    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>Frozen per-student amount, computed once at generation time —
    /// see <see cref="Payment.AmountType"/>/<see cref="Payment.Amount"/>.</summary>
    public decimal Amount { get; set; }

    /// <summary>Per-payment override. Null = use the student's
    /// <see cref="Student.DefaultVariableSymbol"/> (resolved on read, see
    /// <see cref="PaymentsController"/>'s DTO mapping).</summary>
    public string? VariableSymbol { get; set; }

    /// <summary>Which installment batch this belongs to — for a recurring
    /// Payment, each batch shares one due date (see
    /// <see cref="PaymentRecipientGenerator.GenerateBatchAsync"/>).</summary>
    public DateOnly InstallmentDueDate { get; set; }

    public bool Paid { get; set; }

    /// <summary>Set manually by Teacher/Admin — there's no bank API
    /// integration, see ROADMAP. Never settable by the payer themselves
    /// (Parent/Student) — a self-reported "I paid" would be a trust problem.</summary>
    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
