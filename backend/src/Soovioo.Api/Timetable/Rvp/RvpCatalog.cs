using System.Text.Json;

namespace Soovioo.Api.Timetable.Rvp;

/// <summary>Rámcové učební plány of the RVPs a school can teach by, from the
/// ministry's texts (see docs/timetable-generation.md). Grades are the
/// school's own (<see cref="Evidence.SchoolClass.Rocnik"/>), so a lower stage
/// of a víceleté gymnázium starts with 1.</summary>
public static class RvpCatalog
{
    private static readonly Lazy<Dictionary<string, RvpProgram>> ByCode = new(Load);

    public static IReadOnlyCollection<RvpProgram> Programs => ByCode.Value.Values;

    public static RvpProgram? Find(string? code) =>
        code is not null && ByCode.Value.TryGetValue(code, out var program) ? program : null;

    private static Dictionary<string, RvpProgram> Load()
    {
        using var stream = typeof(RvpCatalog).Assembly.GetManifestResourceStream("rvp-programs.json")!;
        var catalog = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return catalog.Programs.ToDictionary(p => p.Code);
    }

    private record Catalog(List<RvpProgram> Programs);
}

/// <param name="Family">ZV, G or SOV.</param>
/// <param name="Template">ZV = <see cref="RvpTemplates"/>'s typical plan by
/// grades, GEN = spread from the minima.</param>
/// <param name="TemplateOffset">Grade of the typical plan = the class's + this.</param>
public record RvpProgram(
    string Code, string Name, string Family, IReadOnlyList<RvpStage> Stages, IReadOnlyList<RvpWeekly> Weekly,
    RvpRules Rules, string Template, int TemplateOffset)
{
    public IEnumerable<int> Grades => Stages.SelectMany(s => Enumerable.Range(s.FromGrade, s.ToGrade - s.FromGrade + 1));

    public RvpStage? StageOf(int grade) => Stages.FirstOrDefault(s => grade >= s.FromGrade && grade <= s.ToGrade);

    public RvpWeekly? WeeklyOf(int grade) => Weekly.FirstOrDefault(w => grade >= w.FromGrade && grade <= w.ToGrade);

    /// <summary>Areas of all stages, each once.</summary>
    public IEnumerable<RvpArea> Areas => Stages.SelectMany(s => s.Areas).DistinctBy(a => a.Key);
}

/// <param name="Total">Hours a week over the stage's grades.</param>
/// <param name="TotalMax">Most the school may have; null = no limit but the weekly one.</param>
/// <param name="Disponible">Null = the RVP has no minima, so all but the total is up to the school.</param>
public record RvpStage(
    int FromGrade, int ToGrade, string Label, decimal Total, decimal? Disponible, IReadOnlyList<RvpArea> Areas, decimal? TotalMax = null);

/// <param name="Kind">What's taught there (<see cref="RvpSubjects"/>); null =
/// a vocational area known just by its name.</param>
/// <param name="Alt">Areas with the same value are alternatives, one is enough.</param>
public record RvpArea(string Key, string Name, decimal Hours, string? Kind = null, string? Alt = null);

public record RvpWeekly(int FromGrade, int ToGrade, decimal Min, decimal? Max = null);

/// <param name="EnglishFrom">English from this grade at the latest (a grade
/// later when another language starts in the first).</param>
/// <param name="SecondFrom">Another foreign language from this grade at the latest.</param>
/// <param name="PeMin">Hours a week of physical education in each grade.</param>
public record RvpRules(int? EnglishFrom, int? SecondFrom, decimal? PeMin);
