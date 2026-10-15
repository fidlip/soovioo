using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;
using Sova.Api.Evidence;
using Sova.Api.Timetable;

namespace Sova.Api.Tests.Timetable;

public class TimetableControllerTests
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
    public async Task UpdateEntryChangesDayAndPeriod()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        var entry = new TimetableEntry
        {
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
        };
        db.TimetableEntries.Add(entry);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .UpdateEntry(
                schoolClass.Id,
                entry.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Tuesday, 3, WeekParity.Odd));

        var dto = Assert.IsType<TimetableEntryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(DayOfWeek.Tuesday, dto.DayOfWeek);
        Assert.Equal(3, dto.PeriodNumber);
        Assert.Equal(WeekParity.Odd, dto.WeekParity);
    }

    [Fact]
    public async Task DeleteEntryRemovesIt()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        var entry = new TimetableEntry
        {
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
        };
        db.TimetableEntries.Add(entry);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteEntry(schoolClass.Id, entry.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.TimetableEntries.FindAsync(entry.Id));
    }
}
