using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class MainBlocksTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero));
    private readonly School school = new() { Id = SchoolId, Name = "ZŠ Lipová", Slug = "lipova" };
    private readonly TimetableVersion draft, other;
    private readonly SchoolClass firstA, firstB, fifth, noGrade, elsewhere;
    private readonly Subject czech, pe;

    public MainBlocksTests()
    {
        firstA = new SchoolClass { Name = "1.A", Rocnik = 1, SchoolId = SchoolId };
        firstB = new SchoolClass { Name = "1.B", Rocnik = 1, SchoolId = SchoolId };
        fifth = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        noGrade = new SchoolClass { Name = "Přípravná", SchoolId = SchoolId };
        elsewhere = new SchoolClass { Name = "1.A", Rocnik = 1, SchoolId = 2 };
        czech = new Subject { Name = "Český jazyk", Abbreviation = "Čj", SchoolId = SchoolId };
        pe = new Subject { Name = "Tělesná výchova", Abbreviation = "Tv", SchoolId = SchoolId };
        draft = Version("Od února");
        other = Version("Varianta");
        db.AddRange(school, firstA, firstB, fifth, noGrade, elsewhere, czech, pe, draft, other);
        db.SaveChanges();
    }

    private static TimetableVersion Version(string name) => new()
    {
        SchoolId = SchoolId, Name = name, ValidFrom = new DateOnly(2027, 2, 1), Status = TimetableVersionStatus.Draft,
    };

    private MainBlockController School() => new(db, new FakeCurrentSchoolContext(SchoolId));

    private TeachingController Teaching() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    private TimetableVersionsController Versions() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        };

    private static TeachingOverviewDto Value(ActionResult<TeachingOverviewDto> result) =>
        Assert.IsType<TeachingOverviewDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<TeachingOverviewDto> Overview(TimetableVersion version) =>
        Value(await Teaching().GetTeaching(version.Id));

    private static (MainBlock?, MainBlock?, bool) BlockOf(TeachingOverviewDto overview, SchoolClass schoolClass) =>
        overview.Classes.Single(c => c.Id == schoolClass.Id) is var c ? (c.MainBlock, c.InheritedMainBlock, c.OwnMainBlock) : default;

    [Fact]
    public async Task AClassHasItsOwnBlockElseItsGradesElseTheSchoolsOnlyInItsVersion()
    {
        await School().Set(new MainBlock(1, 5));
        await Teaching().SaveCurriculum(draft.Id, new([], [new GradeMainBlockDto(1, 1, 3)]));
        await Teaching().SetClassMainBlock(draft.Id, new(firstB.Id, 2, 4));

        var blocks = await MainBlock.ForClassesAsync(db, draft);

        Assert.Equal(new MainBlock(1, 3), blocks[firstA.Id]);
        Assert.Equal(new MainBlock(2, 4), blocks[firstB.Id]);
        Assert.Equal(new MainBlock(1, 5), blocks[fifth.Id]);
        Assert.Equal(new MainBlock(1, 5), blocks[noGrade.Id]);
        Assert.Equal(4, blocks.Count);
        Assert.All((await MainBlock.ForClassesAsync(db, other)).Values, b => Assert.Equal(new MainBlock(1, 5), b));
    }

    [Fact]
    public async Task TheSchoolHasOneFromTheFirstToTheFourthPeriodUntilSetOtherwise()
    {
        Assert.Equal(MainBlock.Default, (await School().Get()).Value);

        var overview = await Overview(draft);

        Assert.Equal(new MainBlock(1, 4), overview.SchoolMainBlock);
        Assert.Empty(overview.GradeMainBlocks);
        Assert.Equal((new MainBlock(1, 4), new MainBlock(1, 4), false), BlockOf(overview, firstA));
    }

    [Fact]
    public async Task AGradesBlockIsSavedWithThePlanAnEndLeftEmptyAsTheSchoolsAndOneAsTheSchoolsIsNoneOfItsOwn()
    {
        var overview = Value(await Teaching().SaveCurriculum(
            draft.Id, new([], [new(1, null, 5), new(5, 1, 4), new(9, 2, 6)])));

        Assert.Equal([new GradeMainBlockDto(1, 1, 5), new GradeMainBlockDto(9, 2, 6)], overview.GradeMainBlocks);
        Assert.Equal((new MainBlock(1, 5), new MainBlock(1, 5), false), BlockOf(overview, firstA));

        // A plan saved without blocks leaves them, with none clears them.
        Assert.Equal(2, Value(await Teaching().SaveCurriculum(draft.Id, new([]))).GradeMainBlocks.Count);
        Assert.Empty(Value(await Teaching().SaveCurriculum(draft.Id, new([], []))).GradeMainBlocks);
    }

    [Fact]
    public async Task AClassesBlockWithAnEndLeftEmptyTakesItsGradesAndOneAsItsGradesIsNoneOfItsOwn()
    {
        await Teaching().SaveCurriculum(draft.Id, new([], [new(1, 2, 5)]));

        var overview = Value(await Teaching().SetClassMainBlock(draft.Id, new(firstA.Id, null, 6)));
        Assert.Equal((new MainBlock(2, 6), new MainBlock(2, 5), true), BlockOf(overview, firstA));
        Assert.Equal((new MainBlock(2, 5), new MainBlock(2, 5), false), BlockOf(overview, firstB));

        overview = Value(await Teaching().SetClassMainBlock(draft.Id, new(firstA.Id, 2, 5)));
        Assert.Equal((new MainBlock(2, 5), new MainBlock(2, 5), false), BlockOf(overview, firstA));
        Assert.Empty(db.ClassMainBlocks);
    }

    [Theory]
    [InlineData(3, 2, "to")]
    [InlineData(-1, 2, "from")]
    [InlineData(1, 16, "to")]
    public async Task ABlockEndsNoEarlierThanItStartsWithinThePeriods(int from, int to, string field)
    {
        var problem = Assert.IsType<ValidationProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(await School().Set(new MainBlock(from, to))).Value);
        Assert.True(problem.Errors.ContainsKey(field));
        Assert.IsType<BadRequestObjectResult>(
            (await Teaching().SaveCurriculum(draft.Id, new([], [new(1, from, to)]))).Result);
        Assert.IsType<BadRequestObjectResult>(
            (await Teaching().SetClassMainBlock(draft.Id, new(firstA.Id, from, to))).Result);

        Assert.Equal((1, 4), (school.MainBlockFrom, school.MainBlockTo));
        Assert.Empty(db.GradeMainBlocks);
        Assert.Empty(db.ClassMainBlocks);
    }

    [Fact]
    public async Task OnlyADraftsBlocksChangeAndOnlyOfTheSchoolsClasses()
    {
        other.Status = TimetableVersionStatus.Published;
        db.SaveChanges();

        Assert.IsType<BadRequestObjectResult>((await Teaching().SaveCurriculum(other.Id, new([], [new(1, 2, 3)]))).Result);
        Assert.IsType<BadRequestObjectResult>((await Teaching().SetClassMainBlock(other.Id, new(firstA.Id, 2, 3))).Result);
        Assert.IsType<BadRequestObjectResult>((await Teaching().SetClassMainBlock(draft.Id, new(elsewhere.Id, 2, 3))).Result);
        Assert.Empty(db.GradeMainBlocks);
        Assert.Empty(db.ClassMainBlocks.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ACopyOfTheVersionHasItsBlocksAnEmptyOneTheSchools()
    {
        await Teaching().SaveCurriculum(draft.Id, new([], [new(1, 1, 3)]));
        await Teaching().SetClassMainBlock(draft.Id, new(firstB.Id, 2, 4));

        var copy = Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(
            (await Versions().CreateVersion(new("Kopie", new DateOnly(2027, 3, 1), draft.Id))).Result).Value);
        var empty = Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(
            (await Versions().CreateVersion(new("Prázdná", new DateOnly(2027, 3, 1)))).Result).Value);

        var copied = await MainBlock.ForClassesAsync(db, db.TimetableVersions.Single(v => v.Id == copy.Id));
        Assert.Equal((new MainBlock(1, 3), new MainBlock(2, 4)), (copied[firstA.Id], copied[firstB.Id]));
        Assert.All(
            (await MainBlock.ForClassesAsync(db, db.TimetableVersions.Single(v => v.Id == empty.Id))).Values,
            b => Assert.Equal(MainBlock.Default, b));
    }

    [Fact]
    public async Task AClassWithFewerHoursForAllItsPupilsThanItsBlockNeedsIsToCheckADivisionCountingAGroupOnItsOwnNot()
    {
        db.CurriculumItems.Add(new CurriculumItem
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 1, SubjectId = czech.Id, HoursPerWeek = 18,
        });
        db.StudentGroups.Add(new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Sportovní hry", SubjectId = pe.Id, HoursPerWeek = 2,
            Classes = [new() { SchoolId = SchoolId, SchoolClassId = firstA.Id }],
        });
        db.SaveChanges();

        const string tooFew = "Hodin pro celou třídu je 18 týdně, hlavní výukový blok (1.–4. hodina) jich potřebuje 20.";
        var overview = await Overview(draft);
        Assert.Equal([tooFew], overview.Classes.Single(c => c.Id == firstA.Id).Problems);
        Assert.Equal([tooFew], overview.Classes.Single(c => c.Id == firstB.Id).Problems);
        // Without a plan the class has nothing to fill it with yet.
        Assert.Empty(overview.Classes.Single(c => c.Id == fifth.Id).Problems);

        var division = new GroupDivision
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = "Tv", HoursPerWeek = 2, ClassIds = [firstA.Id],
        };
        db.GroupDivisions.Add(division);
        db.StudentGroups.AddRange(new[] { "Dívky", "Chlapci" }.Select(name => new StudentGroup
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, Name = name, SubjectId = pe.Id, HoursPerWeek = 2,
            GroupDivision = division, Classes = [new() { SchoolId = SchoolId, SchoolClassId = firstA.Id }],
        }));
        db.SaveChanges();
        await Teaching().SetClassMainBlock(draft.Id, new(firstB.Id, 1, 3));

        overview = await Overview(draft);
        Assert.Empty(overview.Classes.Single(c => c.Id == firstA.Id).Problems);
        Assert.Empty(overview.Classes.Single(c => c.Id == firstB.Id).Problems);
    }
}
