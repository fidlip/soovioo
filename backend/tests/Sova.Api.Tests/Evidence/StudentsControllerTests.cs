using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;
using Sova.Api.Grades;
using Sova.Api.Messaging;

namespace Sova.Api.Tests.Evidence;

public class StudentsControllerTests
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
    public async Task UpdateStudentChangesFieldsAndClass()
    {
        using var db = NewDb();
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var classB = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = classA.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await new StudentsController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateStudent(student.Id, new CreateStudentRequest("Honza", "Novák", classB.Id));

        var dto = Assert.IsType<StudentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Honza", dto.FirstName);
        Assert.Equal(classB.Id, dto.SchoolClassId);
    }

    [Fact]
    public async Task DeleteStudentRemovesGradesAbsencesMessagesAndParentLinks()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var subject = new Sova.Api.Evidence.Subject { Name = "Matematika", SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Students.Add(student);
        db.Subjects.Add(subject);
        db.Users.Add(parent);
        await db.SaveChangesAsync();

        db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, SchoolId = SchoolId });
        db.Absences.Add(new Absence { StudentId = student.Id, Date = DateOnly.FromDateTime(DateTime.Today), SchoolId = SchoolId });
        db.Messages.Add(new Message { StudentId = student.Id, SenderName = "x", Subject = "s", Body = "b", SchoolId = SchoolId });
        db.ParentStudents.Add(new ParentStudent { StudentId = student.Id, UserId = parent.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await new StudentsController(db, new FakeCurrentSchoolContext(SchoolId)).DeleteStudent(student.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Students.FindAsync(student.Id));
        Assert.Empty(db.Grades.Where(g => g.StudentId == student.Id));
        Assert.Empty(db.Absences.Where(a => a.StudentId == student.Id));
        Assert.Empty(db.Messages.Where(m => m.StudentId == student.Id));
        Assert.Empty(db.ParentStudents.Where(ps => ps.StudentId == student.Id));
    }
}
