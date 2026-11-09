using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Dashboard;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Messaging;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Dashboard;

public class DashboardControllerTests
{
    private const int SchoolId = 1;
    // The school's day, which the dashboard goes by: in UTC it's still
    // yesterday for an hour or two after midnight.
    private static readonly DateOnly TodayDate = Api.Common.SchoolClock.Today(TimeProvider.System);
    private static readonly DayOfWeek Today = TodayDate.DayOfWeek;
    private static readonly DayOfWeek NotToday = (DayOfWeek)(((int)Today + 1) % 7);

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId)) { Today = TodayDate };
    }

    private static DashboardController NewController(AppDbContext db, int currentUserId, string role)
    {
        var principal = TestRoles.Principal(db, currentUserId, Enum.Parse<UserRole>(role), SchoolId, null);
        return new DashboardController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal },
            },
        };
    }

    [Fact]
    public async Task AdminGetsSchoolCountsAndNoLessonsOrGrades()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        db.Students.Add(new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Admin").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(dto.SchoolCounts);
        Assert.Equal(1, dto.SchoolCounts!.StudentCount);
        Assert.Equal(1, dto.SchoolCounts.TeacherCount);
        Assert.Equal(1, dto.SchoolCounts.ClassCount);
        Assert.Empty(dto.TodayLessons);
        Assert.Empty(dto.RecentGrades);
    }

    [Fact]
    public async Task TeacherGetsOnlyTheirOwnTodayLessonsAndPendingExcuseAbsences()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "1.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        var otherTeacher = new User { Username = "ucitel2", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.AddRange(teacher, otherTeacher);
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        db.TimetableEntries.AddRange(
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id, SubjectId = subject.Id, DayOfWeek = Today, PeriodNumber = 1, TeacherId = teacher.Id },
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id, SubjectId = subject.Id, DayOfWeek = NotToday, PeriodNumber = 2, TeacherId = teacher.Id },
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id, SubjectId = subject.Id, DayOfWeek = Today, PeriodNumber = 3, TeacherId = otherTeacher.Id });
        db.Absences.AddRange(
            new Absence { StudentId = student.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.PendingExcuse },
            new Absence { StudentId = student.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.Unexcused },
            new Absence { StudentId = student.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.Excused });
        await db.SaveChangesAsync();

        var result = await NewController(db, teacher.Id, "Teacher").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(dto.TodayLessons);
        Assert.Equal(1, dto.TodayLessons[0].PeriodNumber);
        Assert.Null(dto.TodayLessonsContext);
        Assert.Single(dto.PendingAbsences);
        Assert.Equal(AbsenceStatus.PendingExcuse, dto.PendingAbsences[0].Status);
        Assert.Null(dto.SchoolCounts);
    }

    [Fact]
    public async Task ParentGetsRecentGradesTodayLessonsContextAndUnresolvedAbsences()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(parent);
        var subject = new Subject { Name = "Čeština", SchoolId = SchoolId };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(student, otherStudent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, student.Id, SchoolId);
        db.Grades.Add(new Grade { StudentId = student.Id, SubjectId = subject.Id, Value = 1, SchoolId = SchoolId });
        db.Grades.Add(new Grade { StudentId = otherStudent.Id, SubjectId = subject.Id, Value = 5, SchoolId = SchoolId });
        db.TimetableEntries.AddRange(
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id, SubjectId = subject.Id, DayOfWeek = Today, PeriodNumber = 1 },
            new TimetableEntry { TimetableVersion = db.CurrentTimetableVersion(SchoolId), SchoolId = SchoolId, SchoolClassId = schoolClass.Id, SubjectId = subject.Id, DayOfWeek = NotToday, PeriodNumber = 2 });
        db.Absences.AddRange(
            new Absence { StudentId = student.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.Unexcused },
            new Absence { StudentId = student.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.Excused },
            new Absence { StudentId = otherStudent.Id, Date = TodayDate, SchoolId = SchoolId, Status = AbsenceStatus.Unexcused });
        await db.SaveChangesAsync();

        var result = await NewController(db, parent.Id, "Parent").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(dto.RecentGrades);
        Assert.Equal(student.Id, dto.RecentGrades[0].StudentId);
        Assert.Single(dto.TodayLessons);
        Assert.Equal("Jan (5.A)", dto.TodayLessonsContext);
        Assert.Contains("5.A", dto.TodayLessonsContext);
        Assert.Single(dto.PendingAbsences);
        Assert.Equal(student.Id, dto.PendingAbsences[0].StudentId);
    }

    [Fact]
    public async Task UnreadMessageCountCountsOnlyOwnUnreadConversations()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(parent);
        await db.SaveChangesAsync();
        var myStudent = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(myStudent, otherStudent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, myStudent.Id, SchoolId);
        var teacher = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = db.Role(UserRole.Teacher, SchoolId), SchoolId = SchoolId };
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        // Unread for the parent.
        db.Conversations.Add(ConversationsController.NewConversation(
            "s1", myStudent.Id, teacher.Id, "ucitel", [parent.Id], "b1", SchoolId, now));
        // Already read by the parent.
        var read = ConversationsController.NewConversation(
            "s2", myStudent.Id, teacher.Id, "ucitel", [parent.Id], "b2", SchoolId, now);
        read.Participants.First(p => p.UserId == parent.Id).LastReadAt = now.AddMinutes(1);
        db.Conversations.Add(read);
        // Someone else's family — the parent isn't a participant.
        db.Conversations.Add(ConversationsController.NewConversation(
            "s3", otherStudent.Id, teacher.Id, "ucitel", [], "b3", SchoolId, now));
        await db.SaveChangesAsync();

        var result = await NewController(db, parent.Id, "Parent").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, dto.UnreadMessageCount);
        var unread = Assert.Single(dto.UnreadConversations);
        Assert.Equal("s1", unread.Subject);
        Assert.Equal("ucitel", unread.LastSenderName);
        Assert.Equal("b1", unread.LastMessagePreview);
        Assert.True(unread.Unread);
    }

    [Fact]
    public async Task UnreadBehaviorRecordsIsScopedToAccessibleStudents()
    {
        using var db = NewDb();
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        var parent = new User { Username = "rodic", PasswordHash = "x", ActiveRole = db.Role(UserRole.Parent, SchoolId), SchoolId = SchoolId };
        db.Users.Add(parent);
        await db.SaveChangesAsync();
        var myStudent = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(myStudent, otherStudent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, myStudent.Id, SchoolId);
        db.BehaviorRecords.AddRange(
            new Soovioo.Api.Behavior.BehaviorRecord
            {
                StudentId = myStudent.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
                Text = "b1", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
            },
            new Soovioo.Api.Behavior.BehaviorRecord
            {
                StudentId = myStudent.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
                Text = "b2", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
                ReadAt = DateTimeOffset.UtcNow,
            },
            new Soovioo.Api.Behavior.BehaviorRecord
            {
                StudentId = otherStudent.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
                Text = "b3", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
            });
        await db.SaveChangesAsync();

        var result = await NewController(db, parent.Id, "Parent").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(dto.UnreadBehaviorRecords);
        Assert.Equal(myStudent.Id, dto.UnreadBehaviorRecords[0].StudentId);
    }

    [Fact]
    public async Task StudentWithNoLinkedStudentGetsEmptyWidgetsNotAnError()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = db.Role(UserRole.Student, SchoolId), SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();

        var result = await NewController(db, studentUser.Id, "Student").GetDashboard();

        var dto = Assert.IsType<DashboardDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(dto.TodayLessons);
        Assert.Null(dto.TodayLessonsContext);
        Assert.Empty(dto.RecentGrades);
        Assert.Empty(dto.PendingAbsences);
        Assert.Equal(0, dto.UnreadMessageCount);
        Assert.Empty(dto.UnreadBehaviorRecords);
    }
}
