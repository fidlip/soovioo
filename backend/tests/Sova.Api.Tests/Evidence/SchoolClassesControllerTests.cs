using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Tests.Evidence;

public class SchoolClassesControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    [Fact]
    public async Task UpdateSchoolClassRenamesAndChangesRocnik()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, Rocnik = 1 };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();

        var result = await new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateSchoolClass(schoolClass.Id, new CreateSchoolClassRequest("1.B", 2));

        var updated = Assert.IsType<SchoolClass>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("1.B", updated.Name);
        Assert.Equal(2, updated.Rocnik);
    }

    [Fact]
    public async Task DeleteSchoolClassRejectsWhenItHasStudents()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        db.Students.Add(new Student { FirstName = "A", LastName = "B", SchoolClassId = schoolClass.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteSchoolClass(schoolClass.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSchoolClassSucceedsAndRemovesItsTimetableEntries()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new Sova.Api.Timetable.TimetableEntry
        {
            SchoolClassId = schoolClass.Id,
            SubjectId = 1,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
        });
        await db.SaveChangesAsync();

        var result = await new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteSchoolClass(schoolClass.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.SchoolClasses.FindAsync(schoolClass.Id));
        Assert.Empty(db.TimetableEntries.Where(e => e.SchoolClassId == schoolClass.Id));
    }
}
