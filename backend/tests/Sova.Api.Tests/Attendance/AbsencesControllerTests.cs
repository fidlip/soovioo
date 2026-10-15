using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
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
}
