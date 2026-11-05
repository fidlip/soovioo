namespace Soovioo.Api.Timetable.Rvp;

/// <summary>A proposed učební plán of a program. The RVPs give no hours by
/// grade, so this isn't official: a typical plan of a základní škola, or the
/// minima spread evenly over the grades.</summary>
public static class RvpTemplates
{
    public record Proposal(IReadOnlyDictionary<int, IReadOnlyDictionary<string, decimal>> ByGrade, IReadOnlyList<string> Notes);

    /// <summary>Hours by subject in grades 1–9, with disponibilní hours.</summary>
    private static readonly Dictionary<string, int[]> Elementary = new()
    {
        ["Český jazyk a literatura"] = [9, 9, 8, 7, 7, 5, 4, 5, 5],
        ["Anglický jazyk"] = [0, 0, 3, 3, 3, 3, 3, 3, 3],
        ["Německý jazyk"] = [0, 0, 0, 0, 0, 0, 2, 2, 2],
        ["Matematika"] = [4, 5, 5, 5, 5, 5, 4, 5, 5],
        ["Informatika"] = [0, 0, 0, 1, 1, 1, 1, 1, 1],
        ["Prvouka"] = [2, 2, 3, 0, 0, 0, 0, 0, 0],
        ["Přírodověda"] = [0, 0, 0, 1, 2, 0, 0, 0, 0],
        ["Vlastivěda"] = [0, 0, 0, 2, 2, 0, 0, 0, 0],
        ["Dějepis"] = [0, 0, 0, 0, 0, 2, 2, 2, 2],
        ["Občanská výchova"] = [0, 0, 0, 0, 0, 1, 1, 1, 1],
        ["Fyzika"] = [0, 0, 0, 0, 0, 1, 2, 2, 2],
        ["Chemie"] = [0, 0, 0, 0, 0, 0, 0, 2, 2],
        ["Přírodopis"] = [0, 0, 0, 0, 0, 2, 2, 1, 2],
        ["Zeměpis"] = [0, 0, 0, 0, 0, 2, 2, 2, 2],
        ["Hudební výchova"] = [1, 1, 1, 1, 1, 1, 1, 1, 1],
        ["Výtvarná výchova"] = [1, 1, 2, 2, 2, 2, 2, 1, 1],
        ["Tělesná výchova"] = [2, 2, 2, 2, 2, 2, 2, 2, 2],
        ["Výchova ke zdraví"] = [0, 0, 0, 0, 0, 1, 1, 0, 0],
        ["Pracovní činnosti"] = [1, 1, 1, 1, 1, 1, 1, 1, 1],
    };

    public static Proposal Propose(RvpProgram program, bool spreadDisponible) =>
        program.Template == "ZV" ? FromTable(program) : FromMinima(program, spreadDisponible);

    private static Proposal FromTable(RvpProgram program)
    {
        var byGrade = program.Grades.ToDictionary(g => g, g =>
        {
            var hours = new Dictionary<string, decimal>();
            foreach (var (subject, grades) in Elementary)
            {
                if (grades[g + program.TemplateOffset - 1] > 0)
                {
                    hours[subject] = grades[g + program.TemplateOffset - 1];
                }
            }
            return hours;
        });
        // What the stage's total asks over the typical grades (the lower
        // stage of a six-year gymnázium).
        foreach (var stage in program.Stages)
        {
            var first = byGrade[stage.FromGrade];
            var have = Enumerable.Range(stage.FromGrade, stage.ToGrade - stage.FromGrade + 1).Sum(g => byGrade[g].Values.Sum());
            if (have < stage.Total)
            {
                first["Český jazyk a literatura"] += stage.Total - have;
            }
        }
        return new Proposal(byGrade.ToDictionary(x => x.Key, x => (IReadOnlyDictionary<string, decimal>)x.Value), []);
    }

    private static Proposal FromMinima(RvpProgram program, bool spreadDisponible)
    {
        var byGrade = new Dictionary<int, IReadOnlyDictionary<string, decimal>>();
        var notes = new List<string>();
        foreach (var stage in program.Stages)
        {
            var grades = Enumerable.Range(stage.FromGrade, stage.ToGrade - stage.FromGrade + 1).ToList();
            var subjects = new Dictionary<string, decimal>();
            foreach (var group in stage.Areas.GroupBy(a => a.Alt ?? a.Key))
            {
                var chosen = group.First();
                if (group.Count() > 1)
                {
                    notes.Add($"{program.Name}: navržený okruh {chosen.Name}; místo něj lze {string.Join(", ", group.Skip(1).Select(a => a.Name))}.");
                }
                var names = SubjectsOf(chosen);
                for (var i = 0; i < names.Length; i++)
                {
                    var share = Math.Floor(chosen.Hours / names.Length) + (i < chosen.Hours % names.Length ? 1 : 0);
                    subjects[names[i]] = subjects.GetValueOrDefault(names[i]) + share;
                }
            }
            if (spreadDisponible && stage.Disponible is { } disponible)
            {
                // To what the field is about, or to all where nothing is.
                var targets = stage.Areas.Where(a => a.Kind is null && a.Alt is null).Select(a => a.Name)
                    .Concat(stage.Areas.Where(a => a.Kind is null && a.Alt is not null).Take(1).Select(a => a.Name))
                    .ToList();
                if (targets.Count == 0)
                {
                    targets = [.. subjects.Keys];
                }
                for (var i = 0; i < (int)disponible; i++)
                {
                    subjects[targets[i % targets.Count]] += 1;
                }
                subjects[targets[0]] += disponible - Math.Floor(disponible);
            }

            var totals = grades.ToDictionary(g => g, _ => 0m);
            var hours = grades.ToDictionary(g => g, _ => new Dictionary<string, decimal>());
            foreach (var (subject, sum) in subjects.OrderByDescending(s => s.Value))
            {
                var each = Math.Floor(sum / grades.Count);
                foreach (var grade in grades)
                {
                    hours[grade][subject] = each;
                    totals[grade] += each;
                }
                var left = sum - each * grades.Count;
                while (left > 0)
                {
                    var lightest = grades.OrderBy(g => totals[g]).ThenBy(g => g).First(g => hours[g][subject] == each);
                    var step = Math.Min(left, 1);
                    hours[lightest][subject] += step;
                    totals[lightest] += step;
                    left -= step;
                }
            }
            foreach (var grade in grades)
            {
                byGrade[grade] = hours[grade].Where(h => h.Value > 0).ToDictionary(h => h.Key, h => h.Value);
            }
        }
        return new Proposal(byGrade, notes);
    }

    private static string[] SubjectsOf(RvpArea area) => area.Kind switch
    {
        "czech" => ["Český jazyk a literatura"],
        "foreign" => ["Anglický jazyk"],
        "foreignTwo" => ["Anglický jazyk", "Německý jazyk"],
        "second" => ["Německý jazyk"],
        "math" => ["Matematika"],
        "informatics" => ["Informatika"],
        "society" => ["Občanská nauka"],
        "nature" => ["Základy přírodních věd"],
        "natureSociety" => ["Fyzika", "Chemie", "Biologie", "Zeměpis", "Dějepis", "Základy společenských věd"],
        "arts" => ["Estetická výchova"],
        "health" or "sport" => ["Tělesná výchova"],
        "optional" => ["Volitelný seminář"],
        "economic" => ["Ekonomika"],
        _ => [area.Name],
    };
}
