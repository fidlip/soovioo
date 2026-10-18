using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;

namespace Soovioo.Api.Tests.Grades;

public class GradesControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static async Task<(Student student, Subject subject)> SeedStudentAndSubject(AppDbContext db)
    {
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Students.Add(student);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return (student, subject);
    }

    [Fact]
    public async Task UpdateGradeChangesValueAndWeight()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);
        var grade = new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 3, Weight = 1, SchoolId = SchoolId };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var result = await new GradesController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateGrade(grade.Id, new CreateGradeRequest(student.Id, subject.Id, 1, 3, "opravena"));

        var dto = Assert.IsType<GradeDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.Value);
        Assert.Equal(3, dto.Weight);
        Assert.Equal("opravena", dto.Note);
    }

    [Fact]
    public async Task DeleteGradeRemovesIt()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);
        var grade = new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 2, SchoolId = SchoolId };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var result = await new GradesController(db, new FakeCurrentSchoolContext(SchoolId)).DeleteGrade(grade.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Grades.FindAsync(grade.Id));
    }
}
