using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Tests.Attendance;

public class AbsencesControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static AbsencesController NewControllerAs(AppDbContext db, int userId, UserRole role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
            authenticationType: "Test");
        return new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public async Task UpdateAbsenceChangesDate()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateAbsence(absence.Id, new CreateAbsenceRequest(student.Id, new DateOnly(2026, 1, 2)));

        var dto = Assert.IsType<AbsenceDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new DateOnly(2026, 1, 2), dto.Date);
    }

    [Fact]
    public async Task DeleteAbsenceRemovesIt()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId)).DeleteAbsence(absence.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Absences.FindAsync(absence.Id));
    }

    [Fact]
    public async Task SubmitExcuseRejectsTheStudentThemselves()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId, UserId = studentUser.Id };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await NewControllerAs(db, studentUser.Id, UserRole.Student)
            .SubmitExcuse(absence.Id, new SubmitExcuseRequest("Nemoc"));

        Assert.IsType<ForbidResult>(result);
        Assert.Null((await db.Absences.FindAsync(absence.Id))!.ExcuseReason);
    }

    [Fact]
    public async Task SubmitExcuseAllowsTheLinkedParent()
    {
        using var db = NewDb();
        var parentUser = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        db.ParentStudents.Add(new ParentStudent { UserId = parentUser.Id, StudentId = student.Id, SchoolId = SchoolId });
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await NewControllerAs(db, parentUser.Id, UserRole.Parent)
            .SubmitExcuse(absence.Id, new SubmitExcuseRequest("Nemoc"));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("Nemoc", (await db.Absences.FindAsync(absence.Id))!.ExcuseReason);
    }
}
