using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Parent↔child links as seen from login accounts — the rest of the
/// app thinks in user ids, the links live on <see cref="ParentProfile"/>.</summary>
public static class ParentLinkQueries
{
    /// <summary>Pupils the account is a parent of.</summary>
    public static IQueryable<int> ChildIdsOf(this AppDbContext db, int userId) =>
        db.ParentStudents
            .Where(ps => ps.ParentProfile!.Person!.UserId == userId)
            .Select(ps => ps.StudentId);

    /// <summary>(StudentId, parent's UserId) for every link whose parent has
    /// an account.</summary>
    public static IQueryable<ParentLinkRow> ParentAccountLinks(this IQueryable<ParentStudent> links) =>
        links
            .Where(ps => ps.ParentProfile!.Person!.UserId != null)
            .Select(ps => new ParentLinkRow(ps.StudentId, ps.ParentProfile!.Person!.UserId!.Value));

    /// <summary>The account's Person, created on the fly for accounts that
    /// don't have one yet (name defaults to the username).</summary>
    public static async Task<Person> EnsurePersonAsync(this AppDbContext db, User user)
    {
        var person = db.Persons.Local.FirstOrDefault(p => p.UserId == user.Id)
            ?? await db.Persons.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (person is null)
        {
            person = new Person { UserId = user.Id, LastName = user.Username, SchoolId = user.SchoolId };
            db.Persons.Add(person);
        }
        return person;
    }

    public static async Task<ParentProfile> EnsureParentProfileAsync(this AppDbContext db, User user)
    {
        var person = await db.EnsurePersonAsync(user);
        var profile = db.ParentProfiles.Local.FirstOrDefault(p => p.Person == person)
            ?? (person.Id == 0 ? null : await db.ParentProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.PersonId == person.Id));
        if (profile is null)
        {
            profile = new ParentProfile { Person = person, SchoolId = person.SchoolId!.Value };
            db.ParentProfiles.Add(profile);
        }
        return profile;
    }

    public static async Task<TeacherProfile> EnsureTeacherProfileAsync(this AppDbContext db, User user)
    {
        var person = await db.EnsurePersonAsync(user);
        var profile = db.TeacherProfiles.Local.FirstOrDefault(p => p.Person == person)
            ?? (person.Id == 0 ? null : await db.TeacherProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.PersonId == person.Id));
        if (profile is null)
        {
            profile = new TeacherProfile { Person = person, SchoolId = person.SchoolId!.Value };
            db.TeacherProfiles.Add(profile);
        }
        return profile;
    }

    /// <summary>Links <paramref name="studentId"/> as a child of the account
    /// (creating its Person/ParentProfile if needed). Caller saves.</summary>
    public static async Task LinkChildAsync(this AppDbContext db, int parentUserId, int studentId, int schoolId)
    {
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == parentUserId);
        var profile = await db.EnsureParentProfileAsync(user);
        profile.Children.Add(new ParentStudent { StudentId = studentId, SchoolId = schoolId });
    }
}

public record ParentLinkRow(int StudentId, int UserId);
