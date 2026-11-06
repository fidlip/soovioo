using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
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

    private static async Task<(StudentProfile student, Subject subject)> SeedStudentAndSubject(AppDbContext db)
    {
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Students.Add(student);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        db.Teaches(1, student.SchoolClassId, subject.Id);
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

        var result = await NewTeacherController(db)
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

        var result = await NewTeacherController(db).DeleteGrade(grade.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Grades.FindAsync(grade.Id));
    }

    private static GradesController NewTeacherController(AppDbContext db)
    {
        var principal = TestRoles.Principal(db, 1, UserRole.Teacher, SchoolId);
        return new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
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
        var adamec = new StudentProfile { Person = new Person { FirstName = "Petr", LastName = "Adamec", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
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

        Assert.Equal([3, 2, 1], Page(result).Items.Select(g => g.Value));
    }

    [Fact]
    public async Task GetGradesReturnsOnePageAndTheTotal()
    {
        using var db = NewDb();
        var (student, subject) = await SeedStudentAndSubject(db);
        var createdAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var day = new DateOnly(2026, 9, 1);
        for (var i = 0; i < 5; i++)
        {
            db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, GradedOn = day, CreatedAt = createdAt, SchoolId = SchoolId });
        }
        await db.SaveChangesAsync();
        var controller = NewTeacherController(db);

        var first = Page(await controller.GetGrades(page: 1, pageSize: 2));
        var second = Page(await controller.GetGrades(page: 2, pageSize: 2));
        var last = Page(await controller.GetGrades(page: 3, pageSize: 2));

        Assert.Equal(5, first.TotalCount);
        // The same day and moment: without a tie-break a grade could show up on two pages.
        var ids = first.Items.Concat(second.Items).Concat(last.Items).Select(g => g.Id).ToList();
        Assert.Equal(5, ids.Distinct().Count());
        Assert.Single(last.Items);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 5000, 100)]
    public async Task GetGradesKeepsThePageWithinLimits(int page, int pageSize, int expectedSize)
    {
        using var db = NewDb();

        var result = Page(await NewTeacherController(db).GetGrades(page: page, pageSize: pageSize));

        Assert.Equal(Math.Max(page, 1), result.Page);
        Assert.Equal(expectedSize, result.PageSize);
    }

    [Fact]
    public async Task GetGradesFiltersByStudentAndSubject()
    {
        using var db = NewDb();
        var (novak, maths) = await SeedStudentAndSubject(db);
        var adamec = new StudentProfile { Person = new Person { FirstName = "Petr", LastName = "Adamec", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        var czech = new Subject { Name = "Čeština", SchoolId = SchoolId };
        db.Students.Add(adamec);
        db.Subjects.Add(czech);
        await db.SaveChangesAsync();
        db.Teaches(1, adamec.SchoolClassId, czech.Id);
        db.Grades.AddRange(
            new Grade { StudentId = novak.Id, SubjectId = maths.Id, Value = 1, SchoolId = SchoolId },
            new Grade { StudentId = novak.Id, SubjectId = czech.Id, Value = 2, SchoolId = SchoolId },
            new Grade { StudentId = adamec.Id, SubjectId = maths.Id, Value = 3, SchoolId = SchoolId });
        await db.SaveChangesAsync();
        var controller = NewTeacherController(db);

        var byStudent = Page(await controller.GetGrades(studentId: novak.Id));
        var bySubject = Page(await controller.GetGrades(subjectId: maths.Id));
        var both = Page(await controller.GetGrades(studentId: novak.Id, subjectId: czech.Id));

        Assert.Equal(2, byStudent.TotalCount);
        Assert.Equal([3, 1], bySubject.Items.Select(g => g.Value));
        Assert.Equal(2, Assert.Single(both.Items).Value);
    }

    private static PagedResult<GradeDto> Page(ActionResult<PagedResult<GradeDto>> result) =>
        Assert.IsType<PagedResult<GradeDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
}
