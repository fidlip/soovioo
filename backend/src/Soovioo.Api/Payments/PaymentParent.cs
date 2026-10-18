using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Payments;

/// <summary>One Parent-role user explicitly targeted as a recipient of a
/// <see cref="PaymentAudience.Selected"/> payment — every student linked to
/// them via <see cref="ParentStudent"/> owes it.</summary>
public class PaymentParent
{
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
