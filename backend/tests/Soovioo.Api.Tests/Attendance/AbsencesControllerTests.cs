using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Attendance;

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
        return new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
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
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System))
            .UpdateAbsence(absence.Id, new CreateAbsenceRequest(student.Id, new DateOnly(2026, 1, 2)));

        var dto = Assert.IsType<AbsenceDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new DateOnly(2026, 1, 2), dto.Date);
    }

    [Fact]
    public async Task DeleteAbsenceRemovesIt()
    {
        using var db = NewDb();
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)).DeleteAbsence(absence.Id);

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
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
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
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentUser.Id, student.Id, SchoolId);
        var absence = new Absence { StudentId = student.Id, Date = new DateOnly(2026, 1, 1), SchoolId = SchoolId };
        db.Absences.Add(absence);
        await db.SaveChangesAsync();

        var result = await NewControllerAs(db, parentUser.Id, UserRole.Parent)
            .SubmitExcuse(absence.Id, new SubmitExcuseRequest("Nemoc"));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("Nemoc", (await db.Absences.FindAsync(absence.Id))!.ExcuseReason);
    }

    [Fact]
    public async Task AddAbsenceStoresSortedDistinctPeriodsAndRejectsOutOfRange()
    {
        using var db = NewDb();
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var controller = new AbsencesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System));

        var result = await controller.AddAbsence(new CreateAbsenceRequest(student.Id, new DateOnly(2026, 1, 5), [3, 0, 2, 3]));
        var dto = Assert.IsType<AbsenceDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal([0, 2, 3], dto.Periods);

        var invalid = await controller.AddAbsence(new CreateAbsenceRequest(student.Id, new DateOnly(2026, 1, 6), [16]));
        Assert.IsType<BadRequestObjectResult>(invalid.Result);
    }
}
