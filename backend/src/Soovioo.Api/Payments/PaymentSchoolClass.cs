using Soovioo.Api.Evidence;

namespace Soovioo.Api.Payments;

/// <summary>One class targeted as a recipient of a
/// <see cref="PaymentAudience.Selected"/> payment — every student currently
/// in that class owes it (resolved fresh on each installment, see
/// <see cref="PaymentRecipientGenerator"/>).</summary>
public class PaymentSchoolClass
{
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
