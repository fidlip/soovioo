using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Evidence;

public class ClassroomsControllerTests
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
    public async Task AddClassroomSucceeds()
    {
        using var db = NewDb();

        var result = await new ClassroomsController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddClassroom(new CreateClassroomRequest("12", "kmenová", 30));

        var classroom = Assert.IsType<Classroom>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("12", classroom.Name);
        Assert.Equal("kmenová", classroom.Type);
        Assert.Equal(30, classroom.Capacity);
    }

    [Fact]
    public async Task UpdateClassroomRenames()
    {
        using var db = NewDb();
        var classroom = new Classroom { Name = "12", SchoolId = SchoolId };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();

        var result = await new ClassroomsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateClassroom(classroom.Id, new CreateClassroomRequest("14"));

        var updated = Assert.IsType<Classroom>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("14", updated.Name);
    }

    [Fact]
    public async Task DeleteClassroomRejectsWhenUsedInTimetable()
    {
        using var db = NewDb();
        var classroom = new Classroom { Name = "12", SchoolId = SchoolId };
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Classrooms.Add(classroom);
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            ClassroomId = classroom.Id,
        });
        await db.SaveChangesAsync();

        var result = await new ClassroomsController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteClassroom(classroom.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteClassroomRejectsWhenDefaultForSubject()
    {
        using var db = NewDb();
        var classroom = new Classroom { Name = "Tělocvična", SchoolId = SchoolId };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        db.Subjects.Add(new Subject { Name = "Tělocvik", SchoolId = SchoolId, DefaultClassroomId = classroom.Id });
        await db.SaveChangesAsync();

        var result = await new ClassroomsController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteClassroom(classroom.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteClassroomSucceedsWhenUnused()
    {
        using var db = NewDb();
        var classroom = new Classroom { Name = "12", SchoolId = SchoolId };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();

        var result = await new ClassroomsController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteClassroom(classroom.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Classrooms.FindAsync(classroom.Id));
    }
}
