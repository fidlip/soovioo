namespace Soovioo.Api.SchoolYears;

/// <summary>The okresy and Prague's obvody that the jarní prázdniny go by
/// (příloha vyhlášky 16/2005 Sb.): six groups, one week each between
/// February and March, moving a week later every year.</summary>
public static class Districts
{
    /// <summary>The groups in the order of the school year 2022/2023.</summary>
    private static readonly string[][] SpringGroups =
    [
        ["Praha 6–10", "Cheb", "Karlovy Vary", "Sokolov", "Nymburk", "Jindřichův Hradec", "Litoměřice", "Děčín", "Přerov",
            "Frýdek-Místek"],
        ["Kroměříž", "Uherské Hradiště", "Vsetín", "Zlín", "Praha-východ", "Praha-západ", "Mělník", "Rakovník", "Plzeň-město",
            "Plzeň-sever", "Plzeň-jih", "Hradec Králové", "Teplice", "Nový Jičín"],
        ["Česká Lípa", "Jablonec nad Nisou", "Liberec", "Semily", "Havlíčkův Brod", "Jihlava", "Pelhřimov", "Třebíč",
            "Žďár nad Sázavou", "Kladno", "Kolín", "Kutná Hora", "Písek", "Náchod", "Bruntál"],
        ["Mladá Boleslav", "Příbram", "Tábor", "Prachatice", "Strakonice", "Ústí nad Labem", "Chomutov", "Most", "Jičín",
            "Rychnov nad Kněžnou", "Olomouc", "Šumperk", "Opava", "Jeseník"],
        ["Benešov", "Beroun", "Rokycany", "České Budějovice", "Český Krumlov", "Klatovy", "Trutnov", "Pardubice", "Chrudim",
            "Svitavy", "Ústí nad Orlicí", "Ostrava-město", "Prostějov"],
        ["Praha 1–5", "Blansko", "Brno-město", "Brno-venkov", "Břeclav", "Hodonín", "Vyškov", "Znojmo", "Domažlice", "Tachov",
            "Louny", "Karviná"],
    ];

    /// <summary>Alphabetically, as people look for them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. SpringGroups.SelectMany(g => g).Order(StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), false))];

    public static bool IsKnown(string district) => SpringGroups.Any(g => g.Contains(district));

    /// <summary>The week of the jarní prázdniny (Monday) of the district in
    /// the school year starting in <paramref name="startYear"/>: the first
    /// Monday of February is the first week.</summary>
    public static DateOnly SpringHolidays(int startYear, string district)
    {
        var group = Array.FindIndex(SpringGroups, g => g.Contains(district));
        var week = ((group + startYear - 2022) % 6 + 6) % 6;
        var monday = new DateOnly(startYear + 1, 2, 1);
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(1);
        }
        return monday.AddDays(7 * week);
    }
}
