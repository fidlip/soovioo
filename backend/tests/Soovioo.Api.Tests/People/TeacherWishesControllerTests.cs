using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.People;

public class TeacherWishesControllerTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    public TeacherWishesControllerTests()
    {
        db.Schools.Add(new School { Id = SchoolId, Name = "Škola", Slug = "skola" });
        db.SaveChanges();
    }

    private TeacherWishesController Controller() => new(db, new FakeCurrentSchoolContext(SchoolId));

    [Fact]
    public async Task TheSchoolsWishesAreNoneUntilSetThenSaved()
    {
        Assert.Equal(new TeacherWishes(), Assert.IsType<OkObjectResult>((await Controller().Get()).Result).Value);

        await Controller().Set(new TeacherWishes(2, 6, 1));

        Assert.Equal(new TeacherWishes(2, 6, 1), Assert.IsType<OkObjectResult>((await Controller().Get()).Result).Value);
        var school = await db.Schools.SingleAsync();
        Assert.Equal((2, 6, 1), (school.TeacherMinLessonsPerDay, school.TeacherMaxLessonsPerDay, school.TeacherMaxGapsPerDay));
    }

    [Theory]
    [InlineData(4, 3, null)]
    [InlineData(0, null, null)]
    [InlineData(null, 13, null)]
    [InlineData(null, null, 11)]
    public async Task WishesOutOfRangeAreRefused(int? min, int? max, int? gaps)
    {
        var result = await Controller().Set(new TeacherWishes(min, max, gaps));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null((await db.Schools.SingleAsync()).TeacherMaxLessonsPerDay);
    }
}
