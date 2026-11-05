using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;
using Soovioo.Api.Timetable.Rvp;

namespace Soovioo.Api.Tests.Timetable;

public class RvpTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly TimetableVersion version;

    public RvpTests()
    {
        version = db.CurrentTimetableVersion();
        version.Status = TimetableVersionStatus.Draft;
        db.SaveChanges();
    }

    private SchoolClass Class(string name, int? grade, string? program, string? language = null)
    {
        var schoolClass = new SchoolClass { Name = name, Rocnik = grade, RvpProgramCode = program, TeachingLanguage = language, SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.SaveChanges();
        return schoolClass;
    }

    private Subject Subject(string name, string? abbreviation = null)
    {
        var subject = new Subject { Name = name, Abbreviation = abbreviation, SchoolId = SchoolId };
        db.Subjects.Add(subject);
        db.SaveChanges();
        return subject;
    }

    private void Plan(int grade, Subject subject, decimal hours)
    {
        db.CurriculumItems.Add(new CurriculumItem
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, Grade = grade, SubjectId = subject.Id, HoursPerWeek = hours,
        });
        db.SaveChanges();
    }

    private async Task<RvpProgramCheckDto> CheckAsync(string code)
    {
        var plan = await TeachingPlan.LoadAsync(db, version);
        return (await RvpCheck.LoadAsync(db, plan)).Programs.Single(p => p.Code == code);
    }

    private Task<RvpFillSummaryDto> FillAsync(
        bool apply = true, bool overwrite = false, bool create = true, bool spread = true, bool removeOthers = false) =>
        RvpFill.RunAsync(db, version, new RvpFillOptions(overwrite, create, spread, removeOthers), apply);

    [Fact]
    public void Catalog_has_the_programs_of_all_school_types()
    {
        Assert.NotNull(RvpCatalog.Find("ZV2021"));
        Assert.NotNull(RvpCatalog.Find("ZV2025"));
        Assert.NotNull(RvpCatalog.Find("G4"));
        Assert.Equal(271, RvpCatalog.Programs.Count(p => p.Family == "SOV"));
    }

    [Fact]
    public void Elementary_schools_get_only_elementary_programs_and_secondary_the_rest()
    {
        Assert.Equal(["ZV2021", "ZV2025"], RvpCatalog.For(SchoolType.Elementary).Select(p => p.Code));
        var secondary = RvpCatalog.For(SchoolType.Secondary).Select(p => p.Code).ToList();
        Assert.Contains("G4", secondary);
        Assert.Contains("ZV2025-G8", secondary);
        Assert.Contains("16-01-M/01", secondary);
        Assert.DoesNotContain("ZV2025", secondary);
    }

    private School AddSchool(SchoolType type)
    {
        var school = new School { Id = SchoolId, Name = "Škola", Slug = "skola", Type = type };
        db.Schools.Add(school);
        db.SaveChanges();
        return school;
    }

    [Theory]
    [InlineData(SchoolType.Elementary, "ZV2025", "G4")]
    [InlineData(SchoolType.Secondary, "G4", "ZV2025")]
    public async Task A_class_takes_only_a_program_of_its_school_type(SchoolType type, string fits, string doesNot)
    {
        AddSchool(type);
        var controller = new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId));

        Assert.IsType<CreatedAtActionResult>((await controller.AddSchoolClass(new CreateSchoolClassRequest("1.A", 1, RvpProgramCode: fits))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.AddSchoolClass(new CreateSchoolClassRequest("1.B", 1, RvpProgramCode: doesNot))).Result);
        var schoolClass = await db.SchoolClasses.SingleAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.UpdateSchoolClass(schoolClass.Id, new CreateSchoolClassRequest("1.A", 1, RvpProgramCode: doesNot))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.UpdateSchoolClass(schoolClass.Id, new CreateSchoolClassRequest("1.A", 1, RvpProgramCode: "nic"))).Result);
    }

    [Fact]
    public async Task The_endpoint_offers_programs_of_the_school_type()
    {
        AddSchool(SchoolType.Elementary);
        var result = await new RvpProgramsController(db, new FakeCurrentSchoolContext(SchoolId)).GetPrograms();
        var programs = Assert.IsAssignableFrom<IEnumerable<RvpProgramOptionDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["ZV2021", "ZV2025"], programs.Select(p => p.Code));
    }

    [Fact]
    public async Task Changing_the_school_type_clears_programs_not_of_it()
    {
        AddSchool(SchoolType.Secondary);
        var gymnasium = Class("1.A", 1, "G4");
        var lower = Class("1.B", 1, "ZV2025-G8");
        var none = Class("1.C", 1, null);

        await new MySchoolController(db, new FakeCurrentSchoolContext(SchoolId)).SetType(new SetSchoolTypeRequest(SchoolType.Secondary));
        Assert.Equal("G4", gymnasium.RvpProgramCode);

        await new MySchoolController(db, new FakeCurrentSchoolContext(SchoolId)).SetType(new SetSchoolTypeRequest(SchoolType.Elementary));
        Assert.Null(gymnasium.RvpProgramCode);
        Assert.Null(lower.RvpProgramCode);
        Assert.Null(none.RvpProgramCode);
    }

    [Theory]
    [InlineData("ZV2021", 118, 16, 8)]
    [InlineData("G4", 132, 26, 9)]
    [InlineData("16-01-M/01", 128, 31, 12)]
    public void Catalog_reads_first_stage(string code, decimal total, decimal disponible, int areas)
    {
        var stage = RvpCatalog.Find(code)!.Stages[0];
        Assert.Equal(total, stage.Total);
        Assert.Equal(disponible, stage.Disponible);
        Assert.Equal(areas, stage.Areas.Count);
    }

    [Fact]
    public void Revised_elementary_rvp_has_no_area_minima()
    {
        Assert.All(RvpCatalog.Find("ZV2025")!.Stages, s => Assert.Empty(s.Areas));
    }

    [Fact]
    public void Every_program_adds_up_to_its_total_but_for_the_two_with_a_mistake_in_the_source()
    {
        var wrong = RvpCatalog.Programs.Where(p => p.Stages.Any(s => s.Areas.Count > 0
                && s.Areas.GroupBy(a => a.Alt ?? a.Key).Sum(g => g.First().Hours) + (s.Disponible ?? 0) != s.Total))
            .Select(p => p.Code).Order();
        Assert.Equal(["69-41-L/51", "75-41-M/01"], wrong);
    }

    [Theory]
    [InlineData("Matematika", null, "ZV2021", "matematika-a-jeji-aplikace")]
    [InlineData("Něco", "M", "ZV2021", "matematika-a-jeji-aplikace")]
    [InlineData("Německý jazyk", "Nj", "ZV2021", "dalsi-cizi-jazyk")]
    [InlineData("Německý jazyk", "Nj", "16-01-M/01", "cizi-jazyk")]
    [InlineData("Přírodopis", "Př", "ZV2021", "clovek-a-priroda")]
    [InlineData("Dějepis", "D", "G4", "clovek-a-priroda-clovek-a-spolecnost")]
    [InlineData("Monitorování životního prostředí", null, "16-01-M/01", "monitorovani-zivotniho-prostredi")]
    [InlineData("Monitorování", null, "16-01-M/01", "monitorovani-zivotniho-prostredi")]
    public void Subjects_are_recognised(string name, string? abbreviation, string program, string area)
    {
        Assert.Equal(area, RvpSubjects.AreaOf(RvpCatalog.Find(program)!, name, abbreviation)?.Key);
    }

    [Fact]
    public void Unknown_subject_counts_only_towards_disponible()
    {
        Assert.Null(RvpSubjects.AreaOf(RvpCatalog.Find("ZV2021")!, "Kroužek", "Kr"));
    }

    [Theory]
    [InlineData("ZV2021")]
    [InlineData("ZV2025")]
    [InlineData("ZV2021-G8")]
    [InlineData("ZV2025-G8")]
    [InlineData("ZV2021-G6")]
    [InlineData("G4")]
    [InlineData("G-V8")]
    [InlineData("G-V6")]
    [InlineData("GSP4")]
    [InlineData("16-01-M/01")]
    [InlineData("29-51-E/01")]
    [InlineData("21-52-H/01")]
    public async Task A_filled_plan_passes_the_check(string code)
    {
        var program = RvpCatalog.Find(code)!;
        foreach (var grade in program.Grades)
        {
            Class($"{grade}.A", grade, code);
        }
        await FillAsync();
        var check = await CheckAsync(code);
        Assert.Empty(check.Problems);
    }

    [Fact]
    public void Every_program_proposes_all_its_grades_in_the_weekly_limits()
    {
        foreach (var program in RvpCatalog.Programs)
        {
            var proposal = RvpTemplates.Propose(program, spreadDisponible: true);
            foreach (var grade in program.Grades)
            {
                var hours = proposal.ByGrade[grade].Values.Sum();
                var week = program.WeeklyOf(grade)!;
                Assert.True(hours >= week.Min - 1, $"{program.Code} {grade}: {hours} < {week.Min}");
                Assert.True(week.Max is null || hours <= week.Max + 1, $"{program.Code} {grade}: {hours} > {week.Max}");
            }
        }
    }

    [Fact]
    public async Task Check_reports_an_area_under_its_minimum_and_a_low_week()
    {
        foreach (var grade in Enumerable.Range(1, 5))
        {
            Class($"{grade}.A", grade, "ZV2021");
        }
        await FillAsync();
        var maths = await db.Subjects.SingleAsync(s => s.Name == "Matematika");
        var item = await db.CurriculumItems.SingleAsync(i => i.Grade == 1 && i.SubjectId == maths.Id);
        item.HoursPerWeek = 0.5m;
        var other = await db.CurriculumItems.Where(i => i.Grade <= 5 && i.SubjectId == maths.Id && i.Grade > 1).ToListAsync();
        other.ForEach(o => o.HoursPerWeek = 1);
        await db.SaveChangesAsync();

        var check = await CheckAsync("ZV2021");
        Assert.Contains(check.Problems, p => p.Contains("Matematika a její aplikace: 4,5 h, minimum je 20 h"));
        Assert.Contains(check.Problems, p => p.Contains("1. stupeň: ") && p.Contains("RVP žádá 118 h"));
        Assert.Contains(check.Problems, p => p.StartsWith("Třída 1.A:") && p.Contains("týdně, RVP žádá aspoň 18 h"));
    }

    [Fact]
    public async Task Check_skips_totals_of_a_stage_missing_a_grade()
    {
        Class("1.A", 1, "ZV2021");
        await FillAsync();
        var check = await CheckAsync("ZV2021");
        Assert.Equal([2, 3, 4, 5], check.Stages[0].MissingGrades);
        Assert.Empty(check.Problems);
    }

    [Fact]
    public async Task Check_uses_the_class_with_the_least_in_a_grade()
    {
        foreach (var grade in Enumerable.Range(1, 5))
        {
            Class($"{grade}.A", grade, "ZV2021");
        }
        var second = Class("1.B", 1, "ZV2021");
        await FillAsync();
        var maths = await db.Subjects.SingleAsync(s => s.Name == "Matematika");
        db.ClassCurriculumItems.Add(new ClassCurriculumItem
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = second.Id, SubjectId = maths.Id, HoursPerWeek = 0,
        });
        await db.SaveChangesAsync();
        var check = await CheckAsync("ZV2021");
        Assert.Equal(20, check.Stages[0].Areas.Single(a => a.Key == "matematika-a-jeji-aplikace").Have);
    }

    [Fact]
    public async Task Revised_rvp_asks_for_english_a_second_language_and_physical_education()
    {
        foreach (var grade in Enumerable.Range(1, 9))
        {
            Class($"{grade}.A", grade, "ZV2025");
        }
        await FillAsync();
        var removed = await db.CurriculumItems.Include(i => i.Subject)
            .Where(i => i.Subject!.Name == "Anglický jazyk" && i.Grade == 3
                || i.Subject.Name == "Německý jazyk" && i.Grade == 7
                || i.Subject.Name == "Tělesná výchova" && i.Grade == 5)
            .ToListAsync();
        db.CurriculumItems.RemoveRange(removed);
        await db.SaveChangesAsync();

        var check = await CheckAsync("ZV2025");
        Assert.Contains("Třída 3.A: angličtina má být nejpozději od 3. ročníku.", check.Problems);
        Assert.Contains("Třída 7.A: další cizí jazyk má být nejpozději od 7. ročníku.", check.Problems);
        Assert.Contains("Třída 5.A: tělesná výchova má mít aspoň 2 h týdně.", check.Problems);
    }

    [Fact]
    public async Task Another_language_from_the_first_grade_gives_english_a_year_more()
    {
        foreach (var grade in Enumerable.Range(1, 3))
        {
            Class($"{grade}.A", grade, "ZV2025");
        }
        Plan(1, Subject("Německý jazyk", "Nj"), 2);
        Assert.DoesNotContain(
            (await CheckAsync("ZV2025")).Problems, p => p.Contains("angličtina"));
    }

    [Fact]
    public async Task Minority_language_class_needs_no_second_language()
    {
        Class("7.A", 7, "ZV2025", "polština");
        Plan(7, Subject("Matematika", "M"), 4);
        Assert.DoesNotContain((await CheckAsync("ZV2025")).Problems, p => p.Contains("další cizí jazyk"));
    }

    [Fact]
    public async Task Alternative_areas_are_one_minimum()
    {
        foreach (var grade in Enumerable.Range(1, 3))
        {
            Class($"{grade}.A", grade, "29-51-E/01");
        }
        var summary = await FillAsync();
        Assert.Contains(summary.Notes, n => n.Contains("navržený okruh Cukrářská výroba") && n.Contains("Řeznicko-uzenářská výroba"));
        var check = await CheckAsync("29-51-E/01");
        Assert.Empty(check.Problems);
        Assert.Contains(check.Stages[0].Areas, a => a.Name == "Cukrářská výroba / Řeznicko-uzenářská výroba / Mlékařské práce");
    }

    [Fact]
    public async Task A_school_choice_of_area_wins()
    {
        var course = Subject("Kroužek", "Kr");
        Class("1.A", 1, "ZV2021");
        Plan(1, course, 3);
        Assert.Null((await CheckAsync("ZV2021")).Subjects.Single().AreaKey);

        db.SubjectRvpAreas.Add(new SubjectRvpArea { SchoolId = SchoolId, SubjectId = course.Id, ProgramCode = "ZV2021", AreaKey = "umeni-a-kultura" });
        await db.SaveChangesAsync();
        var subject = (await CheckAsync("ZV2021")).Subjects.Single();
        Assert.Equal("umeni-a-kultura", subject.AreaKey);
        Assert.True(subject.Chosen);
    }

    [Fact]
    public async Task Dry_run_changes_nothing()
    {
        Class("1.A", 1, "ZV2021");
        var summary = await FillAsync(apply: false);
        Assert.True(summary.Added > 0);
        Assert.Contains("Matematika", summary.SubjectsCreated);
        Assert.Empty(await db.CurriculumItems.ToListAsync());
        Assert.Empty(await db.Subjects.ToListAsync());
    }

    [Fact]
    public async Task Fill_keeps_hours_unless_told_to_overwrite()
    {
        var maths = Subject("Matematika", "M");
        Class("1.A", 1, "ZV2021");
        Plan(1, maths, 7);

        var kept = await FillAsync();
        Assert.Equal(1, kept.Kept);
        Assert.Equal(7, (await db.CurriculumItems.SingleAsync(i => i.SubjectId == maths.Id)).HoursPerWeek);

        var overwritten = await FillAsync(overwrite: true);
        Assert.Equal(1, overwritten.Overwritten);
        Assert.Equal(4, (await db.CurriculumItems.SingleAsync(i => i.SubjectId == maths.Id)).HoursPerWeek);
    }

    [Fact]
    public async Task Fill_uses_the_school_subject_by_abbreviation_and_makes_no_new_one_unless_told()
    {
        var czech = Subject("Čeština", "Čj");
        Class("1.A", 1, "ZV2021");

        var summary = await FillAsync(create: false);
        Assert.Contains("Matematika", summary.SubjectsMissing);
        Assert.Empty(summary.SubjectsCreated);
        var item = await db.CurriculumItems.SingleAsync();
        Assert.Equal(czech.Id, item.SubjectId);
        Assert.Single(await db.Subjects.ToListAsync());
    }

    [Fact]
    public async Task Fill_removes_other_subjects_only_when_told()
    {
        var other = Subject("Kroužek", "Kr");
        Class("1.A", 1, "ZV2021");
        Plan(1, other, 1);

        await FillAsync();
        Assert.Contains(await db.CurriculumItems.ToListAsync(), i => i.SubjectId == other.Id);

        var summary = await FillAsync(removeOthers: true);
        Assert.Equal(1, summary.Removed);
        Assert.DoesNotContain(await db.CurriculumItems.ToListAsync(), i => i.SubjectId == other.Id);
    }

    [Fact]
    public async Task Disponible_hours_are_spread_only_when_told()
    {
        foreach (var grade in Enumerable.Range(1, 4))
        {
            Class($"{grade}.A", grade, "16-01-M/01");
        }
        await FillAsync(spread: false);
        Assert.Equal(128 - 31, (await db.CurriculumItems.ToListAsync()).Sum(i => i.HoursPerWeek));

        await FillAsync(spread: true, overwrite: true, removeOthers: true);
        Assert.Equal(128, (await db.CurriculumItems.ToListAsync()).Sum(i => i.HoursPerWeek));
    }

    [Fact]
    public async Task Different_programs_in_a_grade_fill_each_class()
    {
        var a = Class("1.A", 1, "16-01-M/01");
        var b = Class("1.B", 1, "21-52-H/01");
        var c = Class("1.C", 1, null);
        var summary = await FillAsync();
        Assert.Empty(await db.CurriculumItems.ToListAsync());
        var items = await db.ClassCurriculumItems.ToListAsync();
        Assert.Contains(items, i => i.SchoolClassId == a.Id);
        Assert.Contains(items, i => i.SchoolClassId == b.Id);
        Assert.DoesNotContain(items, i => i.SchoolClassId == c.Id);
        Assert.Contains(c.Name, summary.ClassesSkipped);
    }

    [Fact]
    public async Task A_class_out_of_its_program_is_skipped_and_reported()
    {
        Class("9.A", 9, "G4");
        var summary = await FillAsync();
        Assert.Contains("9.A", summary.ClassesSkipped);
        Assert.Contains((await CheckAsync("G4")).Problems, p => p.Contains("Třída 9.A: ročník 9 v tomto programu není."));
    }

    [Fact]
    public async Task The_check_before_publishing_lists_rvp_problems()
    {
        foreach (var grade in Enumerable.Range(1, 5))
        {
            Class($"{grade}.A", grade, "ZV2021");
        }
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = (await db.SchoolClasses.FirstAsync()).Id,
            SubjectId = Subject("Matematika", "M").Id, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1,
        });
        await db.SaveChangesAsync();
        var problems = await TimetableCheck.ProblemsAsync(db, version);
        Assert.Contains(problems, p => p.StartsWith("Základní vzdělávání (dosavadní RVP ZV) — "));
    }
}
