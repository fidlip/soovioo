using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
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

    [Fact]
    public async Task AddEntryAssignsTeacherAndReturnsName()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var teacher = new User { Username = "ucitel", PasswordHash = "", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        db.Users.Add(teacher);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                schoolClass.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Every, teacher.Id));

        var dto = Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(teacher.Id, dto.TeacherId);
        Assert.Equal("ucitel", dto.TeacherName);
    }

    [Fact]
    public async Task AddEntryRejectsNonTeacherUserAsTeacher()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        // An Admin, not a Teacher — picking them as a timetable teacher is invalid.
        var admin = new User { Username = "admin", PasswordHash = "", Role = UserRole.Admin, SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                schoolClass.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Every, admin.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTeacherTimetableSpansMultipleClassesAndExcludesOtherTeachers()
    {
        using var db = NewDb();
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var classB = new SchoolClass { Name = "2.B", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var teacher = new User { Username = "ucitel", PasswordHash = "", Role = UserRole.Teacher, SchoolId = SchoolId };
        var otherTeacher = new User { Username = "ucitel2", PasswordHash = "", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        db.Subjects.Add(subject);
        db.Users.AddRange(teacher, otherTeacher);
        await db.SaveChangesAsync();
        db.TimetableEntries.AddRange(
            new TimetableEntry { SchoolClassId = classA.Id, SubjectId = subject.Id, SchoolId = SchoolId, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, TeacherId = teacher.Id },
            new TimetableEntry { SchoolClassId = classB.Id, SubjectId = subject.Id, SchoolId = SchoolId, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 2, TeacherId = teacher.Id },
            new TimetableEntry { SchoolClassId = classA.Id, SubjectId = subject.Id, SchoolId = SchoolId, DayOfWeek = DayOfWeek.Wednesday, PeriodNumber = 3, TeacherId = otherTeacher.Id });
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId)).GetTeacherTimetable(teacher.Id);

        var entries = Assert.IsAssignableFrom<IEnumerable<TeacherTimetableEntryDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.SchoolClassName == "1.A");
        Assert.Contains(entries, e => e.SchoolClassName == "2.B");
    }

    [Fact]
    public async Task AddEntryAssignsClassroomAndReturnsName()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var classroom = new Classroom { Name = "12", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                schoolClass.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Every, ClassroomId: classroom.Id));

        var dto = Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(classroom.Id, dto.ClassroomId);
        Assert.Equal("12", dto.ClassroomName);
        Assert.Null(dto.ClassroomConflict);
    }

    [Fact]
    public async Task AddEntryRejectsUnknownClassroom()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                schoolClass.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Every, ClassroomId: 999));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>Non-blocking: the second entry still saves, but its DTO
    /// carries a warning naming the class already in that classroom — see
    /// TimetableController.FindClassroomConflictAsync's doc comment.</summary>
    [Fact]
    public async Task AddEntryReportsNonBlockingClassroomConflict()
    {
        using var db = NewDb();
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var classB = new SchoolClass { Name = "2.B", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var classroom = new Classroom { Name = "Tělocvična", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        db.Subjects.Add(subject);
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolClassId = classA.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            WeekParity = WeekParity.Every,
            ClassroomId = classroom.Id,
        });
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                classB.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Every, ClassroomId: classroom.Id));

        var dto = Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.NotNull(dto.ClassroomConflict);
        Assert.Contains("1.A", dto.ClassroomConflict);
        // Still saved despite the conflict — this is a warning, not a rejection.
        Assert.Equal(2, await db.TimetableEntries.CountAsync());
    }

    /// <summary>Odd and Even weeks never share a physical room on the same
    /// slot, so no conflict should be reported between them.</summary>
    [Fact]
    public async Task AddEntryNoConflictWhenWeekParityDoesNotOverlap()
    {
        using var db = NewDb();
        var classA = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        var classB = new SchoolClass { Name = "2.B", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        var classroom = new Classroom { Name = "Tělocvična", SchoolId = SchoolId };
        db.SchoolClasses.AddRange(classA, classB);
        db.Subjects.Add(subject);
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolClassId = classA.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            WeekParity = WeekParity.Odd,
            ClassroomId = classroom.Id,
        });
        await db.SaveChangesAsync();

        var result = await new TimetableController(db, new FakeCurrentSchoolContext(SchoolId))
            .AddEntry(
                classB.Id,
                new CreateTimetableEntryRequest(subject.Id, DayOfWeek.Monday, 1, WeekParity.Even, ClassroomId: classroom.Id));

        var dto = Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Null(dto.ClassroomConflict);
    }
}
