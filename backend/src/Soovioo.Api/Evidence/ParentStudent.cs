using Soovioo.Api.People;

namespace Soovioo.Api.Evidence;

/// <summary>One child of a <see cref="People.ParentProfile"/> — the pupils a
/// parent may see/act on. Queries starting from a login account go through
/// <c>ParentProfile.Person.UserId</c>; see <see cref="ParentLinkQueries"/>.</summary>
public class ParentStudent
{
    public int ParentProfileId { get; set; }
    public ParentProfile? ParentProfile { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }
}
