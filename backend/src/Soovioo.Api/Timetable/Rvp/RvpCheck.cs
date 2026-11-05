using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Timetable.Rvp;

/// <summary>A version's učební plán against the RVPs of its classes. A stage
/// counts the plan of its grades as they are now, each grade by the class of
/// the program having the least; a stage with a grade missing checks only the
/// weeks.</summary>
public static class RvpCheck
{
    public record ClassInput(int Id, string Name, int? Grade, string? ProgramCode, string? TeachingLanguage);

    public record Choice(int SubjectId, string ProgramCode, string? AreaKey);

    public static async Task<RvpCheckDto> LoadAsync(AppDbContext db, TeachingPlan plan)
    {
        var classes = await db.SchoolClasses
            .Select(c => new ClassInput(c.Id, c.Name, c.Rocnik, c.RvpProgramCode, c.TeachingLanguage))
            .ToListAsync();
        var choices = await db.SubjectRvpAreas.Select(c => new Choice(c.SubjectId, c.ProgramCode, c.AreaKey)).ToListAsync();
        return Check(plan.Classes, classes, choices);
    }

    public static RvpCheckDto Check(
        IReadOnlyList<TeachingClassDto> planned, IReadOnlyList<ClassInput> classes, IReadOnlyList<Choice> choices)
    {
        var rowsOf = planned.ToDictionary(c => c.Id, c => c.Rows.Where(r => r.PlanHours > 0).ToList());
        var programs = classes.Where(c => RvpCatalog.Find(c.ProgramCode) is not null)
            .GroupBy(c => c.ProgramCode!)
            .OrderBy(g => g.Key)
            .Select(g => CheckProgram(RvpCatalog.Find(g.Key)!, [.. g.OrderBy(c => c.Name)], rowsOf, choices))
            .ToList();
        var without = classes.Where(c => RvpCatalog.Find(c.ProgramCode) is null).OrderBy(c => c.Name).Select(c => c.Name).ToList();
        return new RvpCheckDto(programs, without);
    }

    private static RvpProgramCheckDto CheckProgram(
        RvpProgram program, List<ClassInput> classes, IReadOnlyDictionary<int, List<TeachingRowDto>> rowsOf, IReadOnlyList<Choice> choices)
    {
        var problems = new List<string>();
        var rows = classes.ToDictionary(c => c.Id, c => rowsOf.GetValueOrDefault(c.Id) ?? []);
        var subjects = rows.Values.SelectMany(r => r).DistinctBy(r => r.SubjectId).OrderBy(r => r.SubjectName)
            .Select(r =>
            {
                var choice = choices.FirstOrDefault(c => c.SubjectId == r.SubjectId && c.ProgramCode == program.Code);
                var area = choice is not null ? choice.AreaKey : RvpSubjects.AreaOf(program, r.SubjectName, r.SubjectAbbreviation)?.Key;
                return new RvpSubjectDto(r.SubjectId, r.SubjectName, area, choice is not null);
            })
            .ToList();
        var areaOf = subjects.ToDictionary(s => s.SubjectId, s => s.AreaKey);

        foreach (var schoolClass in classes.Where(c => c.Grade is not { } g || program.StageOf(g) is null))
        {
            problems.Add($"Třída {schoolClass.Name}: ročník {(schoolClass.Grade?.ToString() ?? "není zadaný")} v tomto programu není.");
        }
        foreach (var schoolClass in classes.Where(c => c.Grade is { } g && program.StageOf(g) is not null))
        {
            var hours = rows[schoolClass.Id].Sum(r => r.PlanHours ?? 0);
            if (hours == 0)
            {
                continue;
            }
            if (program.WeeklyOf(schoolClass.Grade!.Value) is { } week)
            {
                if (hours < week.Min)
                {
                    problems.Add($"Třída {schoolClass.Name}: {Hours(hours)} týdně, RVP žádá aspoň {Hours(week.Min)}.");
                }
                else if (hours > week.Max)
                {
                    problems.Add($"Třída {schoolClass.Name}: {Hours(hours)} týdně, RVP dovoluje nejvýš {Hours(week.Max.Value)}.");
                }
            }
            problems.AddRange(RuleProblems(program, schoolClass, classes, rows));
        }

        var stages = program.Stages.Select(stage =>
        {
            var grades = Enumerable.Range(stage.FromGrade, stage.ToGrade - stage.FromGrade + 1).ToList();
            var missing = grades.Where(g => !classes.Any(c => c.Grade == g)).ToList();
            decimal Least(Func<TeachingRowDto, bool> counted) => grades.Where(g => !missing.Contains(g))
                .Sum(g => classes.Where(c => c.Grade == g).Min(c => rows[c.Id].Where(counted).Sum(r => r.PlanHours ?? 0)));

            var total = Least(_ => true);
            var areas = stage.Areas.GroupBy(a => a.Alt ?? a.Key).Select(group =>
            {
                var keys = group.Select(a => a.Key).ToHashSet();
                var have = group.Max(a => Least(r => areaOf[r.SubjectId] == a.Key));
                return new RvpAreaCheckDto(
                    group.First().Key, string.Join(" / ", group.Select(a => a.Name)), group.First().Hours, have,
                    missing.Count > 0 || have >= group.First().Hours);
            }).ToList();
            var complete = missing.Count == 0;
            if (complete)
            {
                if (total < stage.Total)
                {
                    problems.Add($"{stage.Label}: {Hours(total)} celkem, RVP žádá {Hours(stage.Total)}.");
                }
                else if (total > (stage.TotalMax ?? (program.Family == "ZV" ? stage.Total : decimal.MaxValue)))
                {
                    problems.Add($"{stage.Label}: {Hours(total)} celkem, RVP dovoluje nejvýš {Hours(stage.TotalMax ?? stage.Total)}.");
                }
                problems.AddRange(areas.Where(a => !a.Ok).Select(a => $"{stage.Label}, {a.Name}: {Hours(a.Have)}, minimum je {Hours(a.Min)}."));
            }
            var used = complete && stage.Disponible is not null ? Math.Max(0, total - areas.Sum(a => a.Min)) : (decimal?)null;
            return new RvpStageCheckDto(stage.Label, stage.FromGrade, stage.ToGrade, missing, stage.Total, total, stage.Disponible, used, areas);
        }).ToList();

        return new RvpProgramCheckDto(
            program.Code, program.Name, classes.Select(c => c.Name).ToList(), stages, problems, subjects,
            program.Areas.Select(a => new RvpAreaOptionDto(a.Key, a.Name)).ToList());
    }

    private static IEnumerable<string> RuleProblems(
        RvpProgram program, ClassInput schoolClass, List<ClassInput> classes, IReadOnlyDictionary<int, List<TeachingRowDto>> rows)
    {
        var grade = schoolClass.Grade!.Value;
        var own = rows[schoolClass.Id];
        bool Is(TeachingRowDto row, string kind) => RvpSubjects.Recognize(row.SubjectName, row.SubjectAbbreviation)?.Kind == kind;
        if (program.Rules.EnglishFrom is { } englishFrom)
        {
            // Another language from the first grade gives a year more.
            var later = classes.Any(c => c.Grade == program.Stages[0].FromGrade && rows[c.Id].Any(r => Is(r, RvpSubjects.Second)));
            if (grade >= englishFrom + (later ? 1 : 0) && !own.Any(r => Is(r, RvpSubjects.English)))
            {
                yield return $"Třída {schoolClass.Name}: angličtina má být nejpozději od {englishFrom + (later ? 1 : 0)}. ročníku.";
            }
        }
        var minority = schoolClass.TeachingLanguage is { } language && RvpSubjects.Normalize(language) is not ("" or "cestina" or "cesky");
        if (program.Rules.SecondFrom is { } secondFrom && grade >= secondFrom && !minority && !own.Any(r => Is(r, RvpSubjects.Second)))
        {
            yield return $"Třída {schoolClass.Name}: další cizí jazyk má být nejpozději od {secondFrom}. ročníku.";
        }
        if (program.Rules.PeMin is { } peMin && own.Where(r => Is(r, RvpSubjects.Pe)).Sum(r => r.PlanHours ?? 0) < peMin)
        {
            yield return $"Třída {schoolClass.Name}: tělesná výchova má mít aspoň {Hours(peMin)} týdně.";
        }
    }

    private static string Hours(decimal hours) => $"{hours:0.#} h".Replace('.', ',');
}

/// <param name="ClassesWithout">Classes with no program, not checked.</param>
public record RvpCheckDto(IReadOnlyList<RvpProgramCheckDto> Programs, IReadOnlyList<string> ClassesWithout)
{
    public IEnumerable<string> AllProblems =>
        Programs.SelectMany(p => p.Problems.Select(problem => $"{p.Name} — {problem}"));
}

public record RvpProgramCheckDto(
    string Code,
    string Name,
    IReadOnlyList<string> Classes,
    IReadOnlyList<RvpStageCheckDto> Stages,
    IReadOnlyList<string> Problems,
    IReadOnlyList<RvpSubjectDto> Subjects,
    IReadOnlyList<RvpAreaOptionDto> Areas);

/// <param name="MissingGrades">No class of the program there, totals and areas aren't checked.</param>
/// <param name="DisponibleUsed">Hours over the minima; null = not known (grade missing, or no minima).</param>
public record RvpStageCheckDto(
    string Label,
    int FromGrade,
    int ToGrade,
    IReadOnlyList<int> MissingGrades,
    decimal Total,
    decimal Have,
    decimal? Disponible,
    decimal? DisponibleUsed,
    IReadOnlyList<RvpAreaCheckDto> Areas);

public record RvpAreaCheckDto(string Key, string Name, decimal Min, decimal Have, bool Ok);

/// <param name="AreaKey">Null = only towards the disponibilní hours.</param>
/// <param name="Chosen">The school chose, the system didn't.</param>
public record RvpSubjectDto(int SubjectId, string SubjectName, string? AreaKey, bool Chosen);

public record RvpAreaOptionDto(string Key, string Name);
