using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

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
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
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

        var result = await new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
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

        var result = await new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)).DeleteGrade(grade.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Grades.FindAsync(grade.Id));
    }

    private static GradesController NewTeacherController(AppDbContext db)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, nameof(UserRole.Teacher))],
            authenticationType: "Test");
        return new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public async Task AddGradeDefaultsGradedOnToToday()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);

        var result = await NewTeacherController(db)
            .AddGrade(new CreateGradeRequest(student.Id, subject.Id, 1, 1, null));

        var dto = Assert.IsType<GradeDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), dto.GradedOn);
    }

    [Fact]
    public async Task AddGradeKeepsChosenGradedOn()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);
        var gradedOn = new DateOnly(2026, 9, 15);

        var result = await NewTeacherController(db)
            .AddGrade(new CreateGradeRequest(student.Id, subject.Id, 2, 1, null, gradedOn));

        var dto = Assert.IsType<GradeDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(gradedOn, dto.GradedOn);
    }

    [Fact]
    public async Task UpdateGradeWithoutGradedOnKeepsIt()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);
        var gradedOn = new DateOnly(2026, 9, 15);
        var grade = new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 3, GradedOn = gradedOn, SchoolId = SchoolId };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var result = await NewTeacherController(db)
            .UpdateGrade(grade.Id, new CreateGradeRequest(student.Id, subject.Id, 1, 1, null));

        var dto = Assert.IsType<GradeDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(gradedOn, dto.GradedOn);
    }

    [Fact]
    public async Task GetGradesOrdersByGradedOnDescThenStudentName()
    {
        using var db = NewDb();
        var (novak, subject) = await SeedStudentAndSubject(db);
        var adamec = new Student { Person = new Person { FirstName = "Petr", LastName = "Adamec", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(adamec);
        await db.SaveChangesAsync();
        var older = new DateOnly(2026, 9, 1);
        var newer = new DateOnly(2026, 9, 2);
        db.Grades.AddRange(
            new Grade { StudentId = novak.Id, SubjectId = subject.Id, Value = 1, GradedOn = older, SchoolId = SchoolId },
            new Grade { StudentId = novak.Id, SubjectId = subject.Id, Value = 2, GradedOn = newer, SchoolId = SchoolId },
            new Grade { StudentId = adamec.Id, SubjectId = subject.Id, Value = 3, GradedOn = newer, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewTeacherController(db).GetGrades();

        var grades = Assert.IsType<List<GradeDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([3, 2, 1], grades.Select(g => g.Value));
    }
}
