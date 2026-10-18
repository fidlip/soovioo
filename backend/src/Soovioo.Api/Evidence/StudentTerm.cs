using System.Text.RegularExpressions;

namespace Soovioo.Api.Evidence;

/// <summary>Rewrites every declined form of "žák"/"student" in a Czech text
/// to the one the school uses (<see cref="SchoolType"/>). Both words are
/// masculine animate, so the surrounding words never need to change.
/// Mirror of <c>localizeStudentTerm</c> in the frontend shared lib — keep
/// the two form tables in sync.</summary>
public static partial class StudentTerm
{
    private static readonly (string Zak, string Student)[] Forms =
    [
        ("žák", "student"),
        ("žáka", "studenta"),
        ("žákovi", "studentovi"),
        ("žákem", "studentem"),
        ("žáku", "studente"),
        ("žáci", "studenti"),
        ("žáků", "studentů"),
        ("žákům", "studentům"),
        ("žáky", "studenty"),
        ("žácích", "studentech"),
    ];

    private static readonly Dictionary<string, string> ToStudent =
        Forms.ToDictionary(f => f.Zak, f => f.Student);

    private static readonly Dictionary<string, string> ToZak =
        Forms.ToDictionary(f => f.Student, f => f.Zak);

    [GeneratedRegex(@"(?<!\p{L})(žák\p{L}*|žác\p{L}*|student\p{L}*)(?!\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex WordPattern();

    public static string Localize(string text, SchoolType type)
    {
        var table = type == SchoolType.Secondary ? ToStudent : ToZak;
        return WordPattern().Replace(text, match =>
        {
            var word = match.Value;
            if (!table.TryGetValue(word.ToLowerInvariant(), out var replacement))
            {
                return word;
            }
            return char.IsUpper(word[0])
                ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
                : replacement;
        });
    }
}
