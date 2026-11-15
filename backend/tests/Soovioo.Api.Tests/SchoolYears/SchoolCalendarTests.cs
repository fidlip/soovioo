using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.SchoolYears;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.SchoolYears;

public class SchoolCalendarTests : IDisposable
{
    private const int SchoolId = 1;

    private static readonly DateOnly Thursday = new(2026, 10, 29);
    private static readonly DateOnly Friday = new(2026, 10, 30);
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly SchoolClass classA, classB;
    private readonly SchoolYear year;

    /// <summary>5.A and 5.B have maths in the 1st period every weekday;
    /// podzimní prázdniny on Thursday and Friday, ředitelské volno of 5.B on
    /// Monday.</summary>
    public SchoolCalendarTests()
    {
        year = db.SchoolYear(2026, SchoolId);
        classA = new SchoolClass { Name = "5.A", Rocnik = 5, SchoolId = SchoolId };
        classB = new SchoolClass { Name = "5.B", Rocnik = 5, SchoolId = SchoolId };
        var maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        var version = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Rozvrh", ValidFrom = new DateOnly(2026, 9, 1), Status = TimetableVersionStatus.Published,
        };
        db.AddRange(classA, classB, maths, version);
        db.SaveChanges();
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            foreach (var schoolClass in new[] { classA, classB })
            {
                db.TimetableEntries.Add(new TimetableEntry
                {
                    SchoolId = SchoolId, TimetableVersionId = version.Id, SchoolClassId = schoolClass.Id, SubjectId = maths.Id,
                    DayOfWeek = day, PeriodNumber = 1,
                });
            }
        }
        db.SchoolDaysOff.AddRange(
            new SchoolDayOff { SchoolId = SchoolId, SchoolYearId = year.Id, From = Thursday, To = Friday, Name = "Podzimní prázdniny" },
            new SchoolDayOff
            {
                SchoolId = SchoolId, SchoolYearId = year.Id, From = Monday, To = Monday, Name = "Ředitelské volno",
                Kind = DayOffKind.DirectorsDay, ClassIds = [classB.Id],
            });
        db.SaveChanges();
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task ADayOffHasNoLessonsOfItsClasses()
    {
        var lessons = await LessonChanges.BaseLessonsAsync(db, Thursday.AddDays(-1), Monday);

        Assert.Equal(
            [(Thursday.AddDays(-1), classA.Id), (Thursday.AddDays(-1), classB.Id), (Monday, classA.Id)],
            lessons.Select(l => (l.Key.Date, l.ClassIds.Single())).OrderBy(l => l));
    }

    [Fact]
    public async Task TheDaysOfAYearOutsideItsLessonsAreTheHlavniPrazdniny()
    {
        var calendar = await SchoolCalendar.LoadAsync(db, new DateOnly(2026, 6, 1), new DateOnly(2027, 9, 30));

        Assert.True(calendar.IsTeachingDay(new DateOnly(2027, 6, 30)));
        Assert.Equal(SchoolCalendar.SummerHolidays, calendar.Off(new DateOnly(2027, 7, 1))?.Name);
        Assert.Equal(SchoolCalendar.SummerHolidays, calendar.Off(new DateOnly(2027, 8, 31))?.Name);
        Assert.Equal("Podzimní prázdniny", calendar.Off(Friday, classA.Id)?.Name);
        Assert.Null(calendar.Off(Monday));
        Assert.Null(calendar.Off(Monday, classA.Id));
        Assert.Equal("Ředitelské volno", calendar.Off(Monday, classB.Id)?.Name);
        Assert.False(calendar.IsTeachingDay(new DateOnly(2026, 11, 7)));

        // Without a year the school didn't say: a day of lessons.
        Assert.True(calendar.IsTeachingDay(new DateOnly(2026, 6, 15)));
    }
}
