using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Parent-specific data of a <see cref="Person"/> — above all which
/// pupils they're a parent of (<see cref="Children"/>). Created when the
/// account gets the Parent role (or the first child is linked).</summary>
public class ParentProfile
{
    /// <summary>Primary key = the person's id (one profile per person).</summary>
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public int SchoolId { get; set; }

    public List<ParentStudent> Children { get; set; } = [];
}
