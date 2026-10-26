using Soovioo.Api.Evidence;

namespace Soovioo.Api.Payments;

/// <summary>One student explicitly targeted as a recipient of a
/// <see cref="PaymentAudience.Selected"/> payment.</summary>
public class PaymentStudent
{
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
