using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Whose data a visibility setting is about. A teacher who is also
/// a parent decides both as <see cref="Staff"/> and as <see cref="Parent"/>
/// — each is shown to different people for a different reason.</summary>
public enum VisibilityOwner
{
    Staff,
    Parent,
    Student,
}

public enum VisibilityField
{
    /// <summary>Being on the list at all — who can't see the name sees none
    /// of the other fields either.</summary>
    Name,
    Phone,
    Email,
    Photo,
}

/// <summary>Who looks. Staff aren't one of them: what teachers and the
/// school's management see is a matter of permissions, not of anyone's
/// privacy settings.</summary>
public enum VisibilityAudience
{
    Parents,
    Students,
}

/// <summary>How far data reaches within an audience, ordered — a wider
/// scope includes the narrower ones.</summary>
public enum VisibilityScope
{
    Nobody,

    /// <summary>A teacher's classes (taught or led), a parent's children's
    /// classes, a pupil's own class — on the viewer's side the same.</summary>
    MyClasses,

    School,
}

public readonly record struct VisibilityKey(VisibilityOwner Owner, VisibilityField Field, VisibilityAudience Audience);

/// <summary>The school's "Viditelnost údajů" for one <see cref="VisibilityKey"/>:
/// individuals choose between <see cref="Min"/> and <see cref="Max"/>,
/// <see cref="Default"/> applies until they do. A missing row means
/// <see cref="VisibilityScope.Nobody"/> throughout — a new school shares
/// nothing until an admin turns it on.</summary>
public class VisibilityRule
{
    public int Id { get; set; }
    public int SchoolId { get; set; }
    public School? School { get; set; }

    public VisibilityOwner Owner { get; set; }
    public VisibilityField Field { get; set; }
    public VisibilityAudience Audience { get; set; }

    public VisibilityScope Min { get; set; }
    public VisibilityScope Default { get; set; }
    public VisibilityScope Max { get; set; }

    public VisibilityKey Key => new(Owner, Field, Audience);
}

/// <summary>An individual's own choice, stored only where it differs from
/// the school's default, for their own <see cref="Person"/> — a parent
/// chooses for their child's.</summary>
public class VisibilityChoice
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public VisibilityOwner Owner { get; set; }
    public VisibilityField Field { get; set; }
    public VisibilityAudience Audience { get; set; }
    public VisibilityScope Scope { get; set; }

    public VisibilityKey Key => new(Owner, Field, Audience);
}

public static class Visibility
{
    /// <summary>Every setting there is. Staff names aren't one: the
    /// teachers of a class are always shown, as in Zprávy's contacts.
    /// Pupils have no contacts of their own on record (<see cref="Student.ContactPhone"/>
    /// is the guardians' emergency contact).</summary>
    public static readonly IReadOnlyList<VisibilityKey> Keys =
    [
        .. Combine(VisibilityOwner.Staff, [VisibilityField.Phone, VisibilityField.Email, VisibilityField.Photo],
            [VisibilityAudience.Parents, VisibilityAudience.Students]),
        .. Combine(VisibilityOwner.Parent, [VisibilityField.Name, VisibilityField.Phone, VisibilityField.Email, VisibilityField.Photo],
            [VisibilityAudience.Parents]),
        .. Combine(VisibilityOwner.Student, [VisibilityField.Name, VisibilityField.Photo],
            [VisibilityAudience.Students, VisibilityAudience.Parents]),
    ];

    /// <summary>The owners whose settings an account with
    /// <paramref name="roles"/> decides for itself — a pupil's are decided
    /// by the parents.</summary>
    public static IReadOnlyList<VisibilityOwner> OwnOwners(IEnumerable<UserRole> roles)
    {
        var owners = new List<VisibilityOwner>();
        if (roles.Any(r => r is UserRole.Teacher or UserRole.Admin))
        {
            owners.Add(VisibilityOwner.Staff);
        }
        if (roles.Contains(UserRole.Parent))
        {
            owners.Add(VisibilityOwner.Parent);
        }
        return owners;
    }

    /// <summary>What a demo school starts with — enough sharing to show what
    /// the system can do, the choice left to individuals.</summary>
    public static IEnumerable<VisibilityRule> DemoRules(int schoolId) =>
        Keys.Select(key => new VisibilityRule
        {
            SchoolId = schoolId,
            Owner = key.Owner,
            Field = key.Field,
            Audience = key.Audience,
            Min = VisibilityScope.Nobody,
            Default = key.Field == VisibilityField.Photo && key.Owner != VisibilityOwner.Staff
                ? VisibilityScope.Nobody
                : VisibilityScope.MyClasses,
            Max = VisibilityScope.School,
        });

    public static async Task<SchoolVisibility> LoadAsync(AppDbContext db, int schoolId)
    {
        var photos = await db.Schools.Where(s => s.Id == schoolId)
            .Select(s => new { s.StudentPhotos, s.ParentPhotos })
            .FirstAsync();
        return new(await db.VisibilityRules.Where(r => r.SchoolId == schoolId).ToListAsync(),
            photos.StudentPhotos, photos.ParentPhotos);
    }

    private static IEnumerable<VisibilityKey> Combine(
        VisibilityOwner owner, VisibilityField[] fields, VisibilityAudience[] audiences) =>
        fields.SelectMany(f => audiences.Select(a => new VisibilityKey(owner, f, a)));
}

/// <summary>A school's rules, resolving what someone's data is shown to.
/// Pupils' and parents' photo settings are out of use while the school has
/// those photos off (<see cref="School.StudentPhotos"/>,
/// <see cref="School.ParentPhotos"/>): not offered, nothing shown — the
/// rules and choices stay for when they're back on.</summary>
public class SchoolVisibility(IEnumerable<VisibilityRule> rules, bool studentPhotos, bool parentPhotos)
{
    private readonly Dictionary<VisibilityKey, VisibilityRule> rules = rules.ToDictionary(r => r.Key);

    public VisibilityRule Rule(VisibilityKey key) =>
        rules.GetValueOrDefault(key) ?? new VisibilityRule { Owner = key.Owner, Field = key.Field, Audience = key.Audience };

    /// <summary>The choice, or the default, kept within the school's range
    /// (it may have changed since) — and never wider than the name: data of
    /// someone who isn't on the list would be nameless.</summary>
    public bool InUse(VisibilityKey key) =>
        key.Field != VisibilityField.Photo || key.Owner switch
        {
            VisibilityOwner.Student => studentPhotos,
            VisibilityOwner.Parent => parentPhotos,
            _ => true,
        };

    public VisibilityScope Effective(VisibilityKey key, IEnumerable<VisibilityChoice> choices)
    {
        if (!InUse(key))
        {
            return VisibilityScope.Nobody;
        }
        var rule = Rule(key);
        var chosen = choices.FirstOrDefault(c => c.Key == key)?.Scope ?? rule.Default;
        var scope = Clamp(chosen, rule.Min, rule.Max);
        var name = key with { Field = VisibilityField.Name };
        if (key.Field != VisibilityField.Name && Visibility.Keys.Contains(name))
        {
            scope = Min(scope, Effective(name, choices));
        }
        return scope;
    }

    /// <summary>Whether a viewer at <paramref name="distance"/> (same class
    /// = <see cref="VisibilityScope.MyClasses"/>, same school only =
    /// <see cref="VisibilityScope.School"/>) sees the data.</summary>
    public bool Shows(VisibilityKey key, IEnumerable<VisibilityChoice> choices, VisibilityScope distance) =>
        Effective(key, choices) >= distance;

    /// <summary>Whether anybody's data under <paramref name="key"/> can be
    /// shown at <paramref name="distance"/> at all.</summary>
    public bool CanShow(VisibilityKey key, VisibilityScope distance) => InUse(key) && Rule(key).Max >= distance;

    public static VisibilityScope Clamp(VisibilityScope scope, VisibilityScope min, VisibilityScope max) =>
        scope < min ? min : scope > max ? max : scope;

    private static VisibilityScope Min(VisibilityScope a, VisibilityScope b) => a < b ? a : b;
}
