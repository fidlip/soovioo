using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Data a school may share with parents and pupils beyond what
/// they see anyway (own grades, absences…) — the school's "Viditelnost
/// údajů" settings. Flags: <see cref="School.SharedData"/> holds what the
/// school shares, <see cref="School.OptOutAllowed"/> where individuals may
/// hide their own part, <see cref="Person.HiddenData"/> (and
/// <see cref="Student.HiddenFromClassmates"/>) what they did hide.</summary>
[Flags]
public enum VisibilityOption
{
    None = 0,

    /// <summary>Employees' photos visible to parents and pupils.</summary>
    StaffPhotos = 1,

    /// <summary>Pupils' photos at all (each still needs a guardian's consent).</summary>
    StudentPhotos = 2,

    /// <summary>Parents' photos at all (voluntary, staff only).</summary>
    ParentPhotos = 4,

    /// <summary>Teachers' phone and e-mail visible to parents.</summary>
    TeacherContacts = 8,

    /// <summary>The class list visible to pupils and parents.</summary>
    Classmates = 16,

    /// <summary>Names and contacts of the other parents in the class
    /// visible to parents.</summary>
    ParentContacts = 32,
}

public static class Visibility
{
    public const VisibilityOption All =
        VisibilityOption.StaffPhotos | VisibilityOption.StudentPhotos | VisibilityOption.ParentPhotos
        | VisibilityOption.TeacherContacts | VisibilityOption.Classmates | VisibilityOption.ParentContacts;

    /// <summary>A pupil's photo is decided by the guardian's consent, not
    /// by an opt-out — every other option can be left to individuals.</summary>
    public const VisibilityOption OptOutCapable = All & ~VisibilityOption.StudentPhotos;

    /// <summary>Whether data under <paramref name="option"/> of someone who
    /// hid <paramref name="hidden"/> is shown. A hide only counts while the
    /// school allows it — turning opt-outs off makes everyone visible
    /// again.</summary>
    public static bool Shows(School school, VisibilityOption option, VisibilityOption hidden = VisibilityOption.None) =>
        school.SharedData.HasFlag(option)
        && !(school.OptOutAllowed.HasFlag(option) && hidden.HasFlag(option));

    /// <summary>The options concerning the own data of an account with
    /// <paramref name="roles"/> — what it may hide in its Settings. Pupils'
    /// classmate-list entries are per <see cref="Student"/>, see
    /// <see cref="Student.HiddenFromClassmates"/>.</summary>
    public static VisibilityOption OwnDataOptions(IEnumerable<UserRole> roles)
    {
        var options = VisibilityOption.None;
        foreach (var role in roles)
        {
            options |= role switch
            {
                UserRole.Teacher => VisibilityOption.StaffPhotos | VisibilityOption.TeacherContacts,
                UserRole.Admin => VisibilityOption.StaffPhotos,
                UserRole.Parent => VisibilityOption.ParentPhotos | VisibilityOption.ParentContacts,
                _ => VisibilityOption.None,
            };
        }
        return options;
    }

    /// <summary>Whether pupils (or their parents) may take a pupil off the
    /// class list.</summary>
    public static bool AllowsClassmatesOptOut(School school) =>
        school.SharedData.HasFlag(VisibilityOption.Classmates)
        && school.OptOutAllowed.HasFlag(VisibilityOption.Classmates);

    public static IReadOnlyList<string> ToNames(VisibilityOption options) =>
        Enum.GetValues<VisibilityOption>()
            .Where(o => o != VisibilityOption.None && options.HasFlag(o))
            .Select(o => o.ToString())
            .ToList();

    /// <summary>Null when a name isn't a single known option.</summary>
    public static VisibilityOption? FromNames(IEnumerable<string> names)
    {
        var options = VisibilityOption.None;
        foreach (var name in names)
        {
            if (!Enum.TryParse<VisibilityOption>(name, out var option)
                || option == VisibilityOption.None || !Enum.IsDefined(option) || int.TryParse(name, out _))
            {
                return null;
            }
            options |= option;
        }
        return options;
    }
}
