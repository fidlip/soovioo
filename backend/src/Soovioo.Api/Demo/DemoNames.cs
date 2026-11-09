namespace Soovioo.Api.Demo;

/// <summary>Czech names for the generated demo people — surnames in both
/// the male and the female form, so a family shares one surname
/// (Novák / Nováková).</summary>
public static class DemoNames
{
    /// <summary>What pupils go by, for the first names that have a usual one.</summary>
    public static readonly IReadOnlyDictionary<string, string> Nicknames = new Dictionary<string, string>
    {
        ["Jan"] = "Honza", ["Jakub"] = "Kuba", ["Tomáš"] = "Tom", ["Matyáš"] = "Máťa", ["Filip"] = "Fíla",
        ["Vojtěch"] = "Vojta", ["Ondřej"] = "Ondra", ["Lukáš"] = "Luky", ["Matěj"] = "Matty", ["Daniel"] = "Dan",
        ["Martin"] = "Marťa", ["Šimon"] = "Šíma", ["Petr"] = "Péťa", ["Marek"] = "Mára", ["Josef"] = "Pepa",
        ["Michal"] = "Míša", ["Kryštof"] = "Kryštůfek", ["Jiří"] = "Jirka", ["František"] = "Franta",
        ["Václav"] = "Vašek", ["Karel"] = "Kája",
        ["Eliška"] = "Eli", ["Anna"] = "Anička", ["Adéla"] = "Áďa", ["Tereza"] = "Terka", ["Natálie"] = "Nati",
        ["Karolína"] = "Kája", ["Kristýna"] = "Týna", ["Barbora"] = "Bára", ["Klára"] = "Klárka", ["Lucie"] = "Lucka",
        ["Veronika"] = "Verča", ["Zuzana"] = "Zuzka", ["Kateřina"] = "Kačka", ["Markéta"] = "Markét",
        ["Michaela"] = "Míša", ["Alžběta"] = "Bětka", ["Johana"] = "Johy",
    };

    public static readonly string[] MaleFirstNames =
    [
        "Jan", "Jakub", "Tomáš", "Adam", "Matyáš", "Filip", "Vojtěch", "Ondřej", "David", "Lukáš",
        "Matěj", "Daniel", "Martin", "Šimon", "Petr", "Antonín", "Dominik", "Marek", "Josef", "Michal",
        "Tobiáš", "Kryštof", "Vít", "Štěpán", "Jiří", "František", "Václav", "Pavel", "Karel", "Oliver",
    ];

    public static readonly string[] FemaleFirstNames =
    [
        "Eliška", "Anna", "Adéla", "Tereza", "Sofie", "Viktorie", "Natálie", "Karolína", "Ema", "Kristýna",
        "Barbora", "Amálie", "Klára", "Julie", "Rozálie", "Laura", "Lucie", "Marie", "Veronika", "Zuzana",
        "Kateřina", "Markéta", "Jana", "Petra", "Hana", "Lenka", "Michaela", "Alžběta", "Nela", "Johana",
    ];

    /// <summary>Parents' generation — more of the names common in the 80s.</summary>
    public static readonly string[] AdultMaleFirstNames =
    [
        "Petr", "Martin", "Tomáš", "Jan", "Michal", "Lukáš", "Jakub", "David", "Jiří", "Pavel",
        "Ondřej", "Marek", "Radek", "Roman", "Zdeněk", "Miroslav", "Aleš", "Libor", "Vladimír", "Karel",
    ];

    public static readonly string[] AdultFemaleFirstNames =
    [
        "Lucie", "Petra", "Kateřina", "Jana", "Martina", "Veronika", "Lenka", "Michaela", "Hana", "Tereza",
        "Eva", "Markéta", "Zuzana", "Monika", "Pavla", "Ivana", "Simona", "Andrea", "Barbora", "Kristýna",
    ];

    public static readonly (string Male, string Female)[] Surnames =
    [
        ("Novák", "Nováková"), ("Svoboda", "Svobodová"), ("Novotný", "Novotná"), ("Dvořák", "Dvořáková"),
        ("Černý", "Černá"), ("Procházka", "Procházková"), ("Kučera", "Kučerová"), ("Veselý", "Veselá"),
        ("Horák", "Horáková"), ("Němec", "Němcová"), ("Marek", "Marková"), ("Pospíšil", "Pospíšilová"),
        ("Pokorný", "Pokorná"), ("Hájek", "Hájková"), ("Král", "Králová"), ("Jelínek", "Jelínková"),
        ("Růžička", "Růžičková"), ("Beneš", "Benešová"), ("Fiala", "Fialová"), ("Sedláček", "Sedláčková"),
        ("Doležal", "Doležalová"), ("Zeman", "Zemanová"), ("Kolář", "Kolářová"), ("Navrátil", "Navrátilová"),
        ("Čermák", "Čermáková"), ("Vaněk", "Vaňková"), ("Urban", "Urbanová"), ("Blažek", "Blažková"),
        ("Kříž", "Křížová"), ("Kovář", "Kovářová"), ("Kratochvíl", "Kratochvílová"), ("Bartoš", "Bartošová"),
        ("Vlček", "Vlčková"), ("Polák", "Poláková"), ("Musil", "Musilová"), ("Kopecký", "Kopecká"),
        ("Šimek", "Šimková"), ("Konečný", "Konečná"), ("Malý", "Malá"), ("Holub", "Holubová"),
        ("Štěpánek", "Štěpánková"), ("Kadlec", "Kadlecová"), ("Dostál", "Dostálová"), ("Soukup", "Soukupová"),
        ("Šťastný", "Šťastná"), ("Mareš", "Marešová"), ("Moravec", "Moravcová"), ("Sýkora", "Sýkorová"),
        ("Tichý", "Tichá"), ("Valenta", "Valentová"), ("Vávra", "Vávrová"), ("Matoušek", "Matoušková"),
        ("Bláha", "Bláhová"), ("Říha", "Říhová"), ("Ševčík", "Ševčíková"), ("Bureš", "Burešová"),
        ("Hruška", "Hrušková"), ("Mašek", "Mašková"), ("Pavlík", "Pavlíková"), ("Janda", "Jandová"),
        ("Hrubý", "Hrubá"), ("Šindelář", "Šindelářová"), ("Havlíček", "Havlíčková"), ("Kašpar", "Kašparová"),
        ("Vacek", "Vacková"), ("Jirásek", "Jirásková"), ("Kubíček", "Kubíčková"), ("Šebesta", "Šebestová"),
        ("Toman", "Tomanová"), ("Lukáš", "Lukášová"), ("Machala", "Machalová"), ("Zelenka", "Zelenková"),
    ];

    public static readonly string[] Streets =
    [
        "Školní", "Lipová", "Zahradní", "Nádražní", "Polní", "Krátká", "Luční", "Sadová", "Husova",
        "Komenského", "Palackého", "Masarykova", "Jiráskova", "Nerudova", "Smetanova", "Družstevní",
    ];

    /// <summary>"Eliška Nováková" → "eliska.novakova" (usernames, e-mails).</summary>
    public static string Ascii(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .Select(c => c == ' ' ? '.' : c);
        return new string(chars.ToArray());
    }
}
