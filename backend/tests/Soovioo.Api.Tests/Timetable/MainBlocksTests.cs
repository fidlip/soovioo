using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class MainBlocksTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly School school = new() { Id = SchoolId, Name = "ZŠ Lipová", Slug = "lipova" };
    private readonly SchoolClass firstA, firstB, fifth, noGrade;

    public MainBlocksTests()
    {
        firstA = new SchoolClass { Name = "1.A", Rocnik = 1, SchoolId = SchoolId };
        firstB = new SchoolClass { Name = "1.B", Rocnik = 1, SchoolId = SchoolId };
        fifth = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        noGrade = new SchoolClass { Name = "Přípravná", SchoolId = SchoolId };
        db.AddRange(school, firstA, firstB, fifth, noGrade,
            new SchoolClass { Name = "1.A", Rocnik = 1, SchoolId = 2, MainBlockFrom = 3, MainBlockTo = 3 },
            new GradeMainBlock { SchoolId = 2, Grade = 5, From = 3, To = 3 });
        db.SaveChanges();
    }

    private MainBlocksController Controller() => new(db, new FakeCurrentSchoolContext(SchoolId));

    [Fact]
    public async Task AClassHasItsOwnBlockElseItsGradesElseTheSchools()
    {
        await Controller().SetSchool(new SetMainBlockRequest(1, 5));
        await Controller().SetGrade(1, new SetMainBlockRequest(1, 3));
        await Controller().SetClass(firstB.Id, new SetMainBlockRequest(2, 4));

        var blocks = await MainBlock.ForClassesAsync(db, SchoolId);

        Assert.Equal(new MainBlock(1, 3), blocks[firstA.Id]);
        Assert.Equal(new MainBlock(2, 4), blocks[firstB.Id]);
        Assert.Equal(new MainBlock(1, 5), blocks[fifth.Id]);
        Assert.Equal(new MainBlock(1, 5), blocks[noGrade.Id]);
        Assert.Equal(4, blocks.Count);
    }

    [Fact]
    public async Task TheSchoolHasOneFromTheFirstToTheFourthPeriodUntilSetOtherwise()
    {
        var dto = (await Controller().Get()).Value!;

        Assert.Equal((1, 4), (dto.From, dto.To));
        Assert.Equal([new GradeMainBlockDto(1, null, null), new GradeMainBlockDto(5, null, null)], dto.Grades);
        Assert.Equal(
            ["1.A", "1.B", "5.A", "Přípravná"],
            dto.Classes.Select(c => c.Name));
        Assert.All(dto.Classes, c => Assert.Null(c.From));
        Assert.Equal(MainBlock.Default, (await MainBlock.ForClassesAsync(db, SchoolId))[firstA.Id]);
    }

    [Fact]
    public async Task OwnBlocksAreListedAndClearedBackToTheLevelAbove()
    {
        await Controller().SetGrade(1, new SetMainBlockRequest(1, 3));
        await Controller().SetGrade(9, new SetMainBlockRequest(2, 6));
        await Controller().SetClass(firstB.Id, new SetMainBlockRequest(2, 4));

        var dto = (await Controller().Get()).Value!;
        Assert.Equal(
            [new GradeMainBlockDto(1, 1, 3), new GradeMainBlockDto(5, null, null), new GradeMainBlockDto(9, 2, 6)],
            dto.Grades);
        Assert.Equal(new ClassMainBlockDto(firstB.Id, "1.B", 1, 2, 4), dto.Classes.Single(c => c.Id == firstB.Id));

        await Controller().ClearGrade(1);
        await Controller().ClearClass(firstB.Id);

        var blocks = await MainBlock.ForClassesAsync(db, SchoolId);
        Assert.Equal(MainBlock.Default, blocks[firstA.Id]);
        Assert.Equal(MainBlock.Default, blocks[firstB.Id]);
        Assert.Single(db.GradeMainBlocks.Where(g => g.SchoolId == SchoolId));
    }

    [Theory]
    [InlineData(3, 2, "to")]
    [InlineData(-1, 2, "from")]
    [InlineData(1, 16, "to")]
    public async Task ABlockEndsNoEarlierThanItStartsWithinThePeriods(int from, int to, string field)
    {
        var request = new SetMainBlockRequest(from, to);

        foreach (var result in new[]
                 {
                     await Controller().SetSchool(request), await Controller().SetGrade(1, request),
                     await Controller().SetClass(firstA.Id, request),
                 })
        {
            var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
            Assert.True(problem.Errors.ContainsKey(field));
        }
        Assert.Equal((1, 4), (school.MainBlockFrom, school.MainBlockTo));
        Assert.Empty(db.GradeMainBlocks.Where(g => g.SchoolId == SchoolId));
        Assert.Null(firstA.MainBlockFrom);
    }

    [Fact]
    public async Task AnotherSchoolsClassIsNotFound()
    {
        var other = db.SchoolClasses.IgnoreQueryFilters().Single(c => c.SchoolId == 2);

        Assert.IsType<NotFoundResult>(await Controller().SetClass(other.Id, new SetMainBlockRequest(1, 2)));
        Assert.Equal(3, other.MainBlockFrom);
    }
}
