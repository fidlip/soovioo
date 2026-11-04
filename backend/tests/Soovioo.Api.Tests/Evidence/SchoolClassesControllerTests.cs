using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Evidence;

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
    public async Task HomeroomIsSavedAndCleared()
    {
        using var db = NewDb();
        var room = new Classroom { Name = "101", Type = ClassroomType.Homeroom, SchoolId = SchoolId };
        db.Classrooms.Add(room);
        await db.SaveChangesAsync();
        var controller = new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId));

        var created = Assert.IsType<SchoolClass>(Assert.IsType<CreatedAtActionResult>(
            (await controller.AddSchoolClass(new CreateSchoolClassRequest("1.A", HomeroomId: room.Id))).Result).Value);
        Assert.Equal(room.Id, created.HomeroomId);

        await controller.UpdateSchoolClass(created.Id, new CreateSchoolClassRequest("1.A"));
        Assert.Null(created.HomeroomId);
    }

    [Fact]
    public async Task HomeroomOfAnotherSchoolIsRejected()
    {
        using var db = NewDb();
        var foreign = new Classroom { Name = "101", SchoolId = SchoolId + 1 };
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.AddRange(foreign, schoolClass);
        await db.SaveChangesAsync();
        var controller = new SchoolClassesController(db, new FakeCurrentSchoolContext(SchoolId));
        var request = new CreateSchoolClassRequest("1.A", HomeroomId: foreign.Id);

        Assert.IsType<BadRequestObjectResult>((await controller.AddSchoolClass(request)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.UpdateSchoolClass(schoolClass.Id, request)).Result);
    }

    [Fact]
    public async Task DeleteSchoolClassRejectsWhenItHasStudents()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        db.Students.Add(new StudentProfile { Person = new Person { FirstName = "A", LastName = "B", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId });
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
        db.TimetableEntries.Add(new Soovioo.Api.Timetable.TimetableEntry
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
