using System.Globalization;
using System.Text;

namespace Soovioo.Api.Data;

/// <summary>Searching regardless of case and diacritics — "Plzen" finds
/// "Plzeň", "novak" finds "Novák".</summary>
public static class TextSearch
{
    /// <summary>Lower-cased, without diacritics. In a database query this is
    /// the SQL function <c>fold_for_search</c> (<c>lower(unaccent(…))</c>),
    /// so fold the column and the searched text alike:
    /// <c>Fold(x.Name).Contains(Fold(term))</c>.</summary>
    public static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString().ToLowerInvariant();
    }
}
