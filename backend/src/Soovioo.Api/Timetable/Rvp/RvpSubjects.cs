using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable.Rvp;

/// <summary>Recognises a school's subjects by name or abbreviation: which
/// area of a program they count towards, and which subject of a proposed
/// plan they are.</summary>
public static class RvpSubjects
{
    public record Known(string Name, string Abbreviation, string Kind, params string[] Aliases);

    public const string Czech = "czech", English = "english", Second = "second", Math = "math", Informatics = "informatics",
        World = "world", Society = "society", Nature = "nature", Arts = "arts", Pe = "pe", HealthEducation = "healthEducation",
        Work = "work", Optional = "optional", Economic = "economic";

    public static readonly IReadOnlyList<Known> All =
    [
        new("Český jazyk a literatura", "Čj", Czech, "Český jazyk", "Čeština", "Čjl"),
        new("Anglický jazyk", "Aj", English, "Angličtina"),
        new("Německý jazyk", "Nj", Second, "Němčina"),
        new("Francouzský jazyk", "Fj", Second, "Francouzština"),
        new("Španělský jazyk", "Šj", Second, "Španělština"),
        new("Ruský jazyk", "Rj", Second, "Ruština"),
        new("Italský jazyk", "Ij", Second, "Italština"),
        new("Další cizí jazyk", "Dcj", Second, "Druhý cizí jazyk"),
        new("Matematika", "M", Math, "Ma"),
        new("Informatika", "Inf", Informatics, "In", "IVT", "ICT"),
        new("Prvouka", "Prv", World),
        new("Přírodověda", "Pří", World),
        new("Vlastivěda", "Vl", World),
        new("Dějepis", "D", Society, "De"),
        new("Občanská výchova", "Ov", Society, "Výchova k občanství"),
        new("Občanská nauka", "On", Society),
        new("Základy společenských věd", "ZSV", Society),
        new("Fyzika", "F", Nature, "Fy"),
        new("Chemie", "Ch", Nature),
        new("Přírodopis", "Př", Nature),
        new("Biologie", "Bi", Nature, "Bio"),
        new("Zeměpis", "Z", Nature, "Ze", "Geografie", "Ge"),
        new("Základy přírodních věd", "ZPV", Nature),
        new("Hudební výchova", "Hv", Arts, "Hudební obor"),
        new("Výtvarná výchova", "Vv", Arts, "Výtvarný obor"),
        new("Estetická výchova", "Est", Arts),
        new("Tělesná výchova", "Tv", Pe),
        new("Výchova ke zdraví", "Vz", HealthEducation),
        new("Pracovní činnosti", "Pč", Work, "Praktické činnosti", "Pv", "Pracovní výchova"),
        new("Volitelný seminář", "Sem", Optional, "Seminář"),
        new("Ekonomika", "Ek", Economic, "Eko"),
    ];

    private static readonly Dictionary<string, Known> ByNormalized = All
        .SelectMany(k => new[] { k.Name, k.Abbreviation }.Concat(k.Aliases).Select(n => (Key: Normalize(n), k)))
        .ToDictionary(x => x.Key, x => x.k);

    public static string Normalize(string text) => TextSearch.Fold(text.Trim());

    public static Known? Recognize(string name, string? abbreviation) =>
        ByNormalized.GetValueOrDefault(Normalize(name))
        ?? (abbreviation is null ? null : ByNormalized.GetValueOrDefault(Normalize(abbreviation)));

    public static Known? ByName(string name) => ByNormalized.GetValueOrDefault(Normalize(name));

    /// <summary>The area of the program the subject counts towards; null =
    /// only towards the disponibilní hours.</summary>
    public static RvpArea? AreaOf(RvpProgram program, string name, string? abbreviation)
    {
        var areas = program.Areas.ToList();
        var normalized = Normalize(name);
        var byName = areas.FirstOrDefault(a => Normalize(a.Name) == normalized);
        if (byName is not null)
        {
            return byName;
        }
        var kind = Recognize(name, abbreviation)?.Kind;
        if (kind is not null)
        {
            return areas.FirstOrDefault(a => a.Kind is { } areaKind && Primary(areaKind).Contains(kind))
                ?? areas.FirstOrDefault(a => a.Kind is { } areaKind && Fallback(areaKind).Contains(kind));
        }
        return normalized.Length < 5
            ? null
            : areas.FirstOrDefault(a => a.Kind is null && (Normalize(a.Name).Contains(normalized) || normalized.Contains(Normalize(a.Name))));
    }

    private static string[] Primary(string areaKind) => areaKind switch
    {
        "foreign" => [English],
        "foreignTwo" => [English, Second],
        "natureSociety" => [Nature, Society],
        "health" => [Pe, HealthEducation],
        "sport" => [Pe],
        _ => [areaKind],
    };

    /// <summary>Where there's no area of its own: German where only one
    /// foreign language counts.</summary>
    private static string[] Fallback(string areaKind) => areaKind == "foreign" ? [Second] : [];
}
