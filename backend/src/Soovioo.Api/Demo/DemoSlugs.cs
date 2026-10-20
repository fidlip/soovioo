using System.Security.Cryptography;

namespace Soovioo.Api.Demo;

/// <summary>Slugs of the "Vyzkoušet" demo schools: <c>demo</c> itself is the
/// entry page (demo.soovioo.cloud), every demo school lives at
/// <c>demo-&lt;id&gt;</c>. Both are off limits for real schools.</summary>
public static class DemoSlugs
{
    public const string EntrySlug = "demo";
    public const string Prefix = "demo-";

    /// <summary>Length of the random part — it's also the only "key" to a
    /// demo (36^11 ≈ 2^57 possibilities, not guessable).</summary>
    public const int IdLength = 11;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static bool IsReserved(string slug) =>
        slug.Equals(EntrySlug, StringComparison.OrdinalIgnoreCase)
        || slug.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string NewSlug() => Prefix + RandomNumberGenerator.GetString(Alphabet, IdLength);
}
