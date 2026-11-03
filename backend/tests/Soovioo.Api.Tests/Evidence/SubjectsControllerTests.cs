using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Evidence;

public class SubjectsControllerTests
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
    public async Task UpdateSubjectRenames()
    {
        using var db = NewDb();
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateSubject(subject.Id, new CreateSubjectRequest("Matika"));

        var updated = Assert.IsType<Subject>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Matika", updated.Name);
    }

    [Fact]
    public async Task AddSubjectAcceptsValidColor()
    {
        using var db = NewDb();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddSubject(new CreateSubjectRequest("Matematika", Color: "#1ABC9C"));

        var dto = Assert.IsType<Subject>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("#1ABC9C", dto.Color);
    }

    [Fact]
    public async Task AddSubjectRejectsMalformedColor()
    {
        using var db = NewDb();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddSubject(new CreateSubjectRequest("Matematika", Color: "red"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteSubjectRejectsWhenItHasGrades()
    {
        using var db = NewDb();
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var student = new StudentProfile { Person = new Person { FirstName = "A", LastName = "B", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Subjects.Add(subject);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId)).DeleteSubject(subject.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSubjectSucceedsWhenUnused()
    {
        using var db = NewDb();
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId)).DeleteSubject(subject.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Subjects.FindAsync(subject.Id));
    }

    [Fact]
    public async Task AddSubjectAcceptsAClassroom()
    {
        using var db = NewDb();
        var classroom = new Classroom { Name = "Tělocvična", SchoolId = SchoolId };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddSubject(new CreateSubjectRequest("Tělocvik", Classrooms: [new(classroom.Id)]));

        var dto = Assert.IsType<Subject>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(classroom.Id, Assert.Single(dto.Classrooms).ClassroomId);
    }

    [Fact]
    public async Task AddSubjectRejectsUnknownClassroom()
    {
        using var db = NewDb();

        var result = await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddSubject(new CreateSubjectRequest("Tělocvik", Classrooms: [new(999)]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ASubjectsClassroomIsAClassroomOrAType()
    {
        using var db = NewDb();
        var controller = new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId));

        var both = await controller.AddSubject(new CreateSubjectRequest("Tělocvik", Classrooms: [new(1, ClassroomType.Gym)]));
        var twice = await controller.AddSubject(new CreateSubjectRequest("Tělocvik", Classrooms: [new(Type: ClassroomType.Gym), new(Type: ClassroomType.Gym)]));

        Assert.IsType<BadRequestObjectResult>(both.Result);
        Assert.IsType<BadRequestObjectResult>(twice.Result);
    }
}
