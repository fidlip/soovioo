using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Messaging;
using Soovioo.Api.People;
using Soovioo.Api.Reference;

namespace Soovioo.Api.Tests.Evidence;

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

    private static StudentsController NewController(AppDbContext db, int currentUserId, string role = "Admin")
    {
        var principal = TestRoles.Principal(db, currentUserId, Enum.Parse<UserRole>(role), SchoolId, null);
        return new StudentsController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    [Fact]
    public async Task UpdateStudentChangesFieldsAndClass()
    {
        using var db = NewDb();
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var classB = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = classA.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999)
            .UpdateStudent(student.Id, new CreateStudentRequest("Honza", "Novák", classB.Id));

        var dto = Assert.IsType<StudentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Honza", dto.FirstName);
        Assert.Equal(classB.Id, dto.SchoolClassId);
    }

    [Fact]
    public async Task DeleteStudentRefusesOneWithHistory()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var subject = new Soovioo.Api.Evidence.Subject { Name = "Matematika", SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Students.Add(student);
        db.Subjects.Add(subject);
        db.Users.Add(parent);
        await db.SaveChangesAsync();

        db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, SchoolId = SchoolId });
        db.Absences.Add(new Absence { StudentId = student.Id, Date = DateOnly.FromDateTime(DateTime.Today), SchoolId = SchoolId });
        db.Conversations.Add(new Conversation { StudentId = student.Id, Subject = "s", SchoolId = SchoolId });
        await db.LinkChildAsync(parent.Id, student.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).DeleteStudent(student.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(
            "Žáka nejde smazat, už má 1 známku, 1 absenci, 1 zprávu. Místo smazání ukončete jeho vzdělávání.",
            conflict.Value);
        Assert.NotNull(await db.Students.FindAsync(student.Id));
        Assert.Single(db.Grades.Where(g => g.StudentId == student.Id));
    }

    [Fact]
    public async Task DeleteStudentWithoutHistoryTakesLinksAndUnpaidInstallmentsAlong()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(parent);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var payment = new Soovioo.Api.Payments.Payment { Title = "Výlet", SchoolId = SchoolId, CreatedByUserId = parent.Id };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        db.PaymentRecipients.Add(new Soovioo.Api.Payments.PaymentRecipient
        {
            PaymentId = payment.Id, StudentId = student.Id, SchoolId = SchoolId, Amount = 100,
        });
        await db.LinkChildAsync(parent.Id, student.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).DeleteStudent(student.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Students.FindAsync(student.Id));
        Assert.Empty(db.PaymentRecipients);
        Assert.Empty(db.ParentStudents);
        Assert.NotNull(await db.Users.FindAsync(parent.Id));
        Assert.Null(await db.Persons.FindAsync(student.PersonId));
    }

    [Fact]
    public async Task DeleteStudentAlsoDeletesTheirOwnLoginAccount()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new StudentProfile
        {
            Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId },
            SchoolClassId = schoolClass.Id,
            SchoolId = SchoolId,
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).DeleteStudent(student.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Users.FindAsync(studentUser.Id));
    }

    [Fact]
    public async Task UpdateStudentResolvesCountryAndMunicipalityNamesForDisplay()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Countries.Add(new Country { Code = "203", Name = "Česká republika" });
        db.Districts.Add(new District { Code = "CZ0100", Name = "Praha" });
        db.Municipalities.Add(new Municipality { Code = "500054", Name = "Praha 1", DistrictCode = "CZ0100" });
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999)
            .UpdateStudent(student.Id, new CreateStudentRequest(
                "Jan", "Novák", schoolClass.Id, CountryCode: "203", MunicipalityCode: "500054"));

        var dto = Assert.IsType<StudentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Česká republika", dto.CountryName);
        Assert.Equal("Praha 1", dto.MunicipalityName);
    }

    [Fact]
    public async Task ClassTeacherCanAddStudentToTheirOwnClass()
    {
        using var db = NewDb();
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var ownClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        db.SchoolClasses.Add(ownClass);
        await db.SaveChangesAsync();

        var result = await NewController(db, teacher.Id, "Teacher")
            .AddStudent(new CreateStudentRequest("Jan", "Novák", ownClass.Id));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task NonClassTeacherCannotAddStudentToAnyClass()
    {
        using var db = NewDb();
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        // Not the class teacher of this class — just an ordinary Teacher.
        var otherClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(otherClass);
        await db.SaveChangesAsync();

        var result = await NewController(db, teacher.Id, "Teacher")
            .AddStudent(new CreateStudentRequest("Jan", "Novák", otherClass.Id));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ClassTeacherCannotMoveAStudentIntoAClassTheyDoNotOwn()
    {
        using var db = NewDb();
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var ownClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id };
        var otherClass = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(ownClass, otherClass);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = ownClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, teacher.Id, "Teacher")
            .UpdateStudent(student.Id, new CreateStudentRequest("Jan", "Novák", otherClass.Id));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ClassTeacherCannotDeleteAStudentFromAnotherClass()
    {
        using var db = NewDb();
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        db.SchoolClasses.Add(new SchoolClass { Name = "1.A", SchoolId = SchoolId, ClassTeacherId = teacher.Id });
        var otherClass = new SchoolClass { Name = "1.B", SchoolId = SchoolId };
        db.SchoolClasses.Add(otherClass);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = otherClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, teacher.Id, "Teacher").DeleteStudent(student.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.NotNull(await db.Students.FindAsync(student.Id));
    }

    [Fact]
    public void VariableSymbolsContinueTheYearsSequence()
    {
        Assert.Equal("260001", VariableSymbols.Next([], 2026));
        Assert.Equal("260003", VariableSymbols.Next(["260002", "250009", "abc", null], 2026));
        Assert.Equal("250010", VariableSymbols.Next(["260002", "250009"], 2025));
    }

    [Fact]
    public async Task AddStudentGeneratesVariableSymbolAndRejectsDuplicates()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", ActiveRole = db.Role(UserRole.Admin, SchoolId), SchoolId = SchoolId };
        db.Users.Add(admin);
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var controller = NewController(db, admin.Id, "Admin");

        var first = await controller.AddStudent(new CreateStudentRequest("Jan", "Novák", schoolClass.Id));
        var dto = Assert.IsType<StudentDto>(Assert.IsType<CreatedAtActionResult>(first.Result).Value);
        var expected = $"{DateTime.Today.Year % 100:00}0001";
        Assert.Equal(expected, dto.DefaultVariableSymbol);

        var duplicate = await controller.AddStudent(
            new CreateStudentRequest("Eva", "Nováková", schoolClass.Id, DefaultVariableSymbol: expected));
        var problem = Assert.IsType<ValidationProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(duplicate.Result).Value);
        Assert.True(problem.Errors.ContainsKey("defaultVariableSymbol"));
    }
}
