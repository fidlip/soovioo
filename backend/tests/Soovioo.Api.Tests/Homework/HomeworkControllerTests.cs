using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;
using HomeworkEntity = Soovioo.Api.Homework.Homework;

namespace Soovioo.Api.Tests.Homework;

public class HomeworkControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static Soovioo.Api.Homework.HomeworkController NewController(AppDbContext db, int userId, string role, string username = "tester")
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Name, username),
        ]);
        return new Soovioo.Api.Homework.HomeworkController(
            db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()),
            new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static IFormFile FakeFile(string fileName, string content = "hello", string contentType = "application/pdf")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static async Task<(SchoolClass Class, Subject Subject)> SeedClassAndSubjectAsync(AppDbContext db, int teacherId)
    {
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId };
        var subject = new Subject { Name = "Matematika", SchoolId = SchoolId };
        db.SchoolClasses.Add(schoolClass);
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        db.TimetableEntries.Add(new TimetableEntry
        {
            SchoolClassId = schoolClass.Id,
            SubjectId = subject.Id,
            SchoolId = SchoolId,
            DayOfWeek = DayOfWeek.Monday,
            PeriodNumber = 1,
            TeacherId = teacherId,
        });
        await db.SaveChangesAsync();
        return (schoolClass, subject);
    }

    [Fact]
    public async Task CreateHomeworkSucceedsForTeacherWhoTeachesSubjectInClass()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);

        var result = await NewController(db, 7, "Teacher")
            .CreateHomework(new Soovioo.Api.Homework.CreateHomeworkRequest(
                schoolClass.Id, subject.Id, "Strana 10, cvičení 3", new DateOnly(2026, 10, 24), false));

        var dto = Assert.IsType<Soovioo.Api.Homework.HomeworkDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Strana 10, cvičení 3", dto.Text);
        Assert.Empty(dto.TargetStudentIds);
    }

    [Fact]
    public async Task CreateHomeworkRejectsTeacherWhoDoesNotTeachSubjectInClass()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);

        var result = await NewController(db, 999, "Teacher")
            .CreateHomework(new Soovioo.Api.Homework.CreateHomeworkRequest(
                schoolClass.Id, subject.Id, "Text", new DateOnly(2026, 10, 24), false));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task CreateHomeworkAllowsAdminRegardlessOfTimetable()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);

        var result = await NewController(db, 999, "Admin")
            .CreateHomework(new Soovioo.Api.Homework.CreateHomeworkRequest(
                schoolClass.Id, subject.Id, "Text", new DateOnly(2026, 10, 24), false));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreateHomeworkRejectsStudentNotInTargetClass()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var otherClass = new SchoolClass { Name = "6.B", SchoolId = SchoolId };
        db.SchoolClasses.Add(otherClass);
        var outsider = new StudentProfile { Person = new Person { FirstName = "Cizí", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = 0, SchoolId = SchoolId };
        db.Students.Add(outsider);
        await db.SaveChangesAsync();
        outsider.SchoolClassId = otherClass.Id;
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher")
            .CreateHomework(new Soovioo.Api.Homework.CreateHomeworkRequest(
                schoolClass.Id, subject.Id, "Text", new DateOnly(2026, 10, 24), false, [outsider.Id]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetHomeworkShowsEverythingUnfilteredForTeacher()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        db.Homework.Add(new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher").GetHomework();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Homework.HomeworkDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetHomeworkIncludesWholeClassAssignmentForParentOfClassMember()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var parentUser = new User { Username = "rodic", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentUser.Id, student.Id, SchoolId);
        db.Homework.Add(new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Celá třída", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetHomework();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Homework.HomeworkDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetHomeworkExcludesUnrelatedParent()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var parentUser = new User { Username = "rodic2", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        db.Homework.Add(new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Celá třída", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetHomework();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Homework.HomeworkDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetHomeworkOnlyIncludesSpecificallyTargetedStudentsOnceNarrowed()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var parentUser = new User { Username = "rodic3", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        var myKid = new StudentProfile { Person = new Person { FirstName = "Můj", LastName = "Syn", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var otherKid = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        db.Students.AddRange(myKid, otherKid);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentUser.Id, myKid.Id, SchoolId);
        var narrowed = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Jen pro vybrané", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(narrowed);
        await db.SaveChangesAsync();
        db.HomeworkStudents.Add(new Soovioo.Api.Homework.HomeworkStudent { HomeworkId = narrowed.Id, StudentId = otherKid.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetHomework();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Homework.HomeworkDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items); // narrowed to otherKid only — not visible to myKid's parent
    }

    [Fact]
    public async Task MarkCompleteSucceedsForTargetedStudentWhenRequired()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            RequiresCompletion = true, CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();

        var result = await NewController(db, studentUser.Id, "Student")
            .MarkComplete(homework.Id, new Soovioo.Api.Homework.MarkHomeworkCompleteRequest(student.Id));

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(await db.HomeworkCompletions.FindAsync(homework.Id, student.Id));
    }

    [Fact]
    public async Task MarkCompleteIsIdempotent()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            RequiresCompletion = true, CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        var controller = NewController(db, studentUser.Id, "Student");

        await controller.MarkComplete(homework.Id, new Soovioo.Api.Homework.MarkHomeworkCompleteRequest(student.Id));
        var second = await controller.MarkComplete(homework.Id, new Soovioo.Api.Homework.MarkHomeworkCompleteRequest(student.Id));

        Assert.IsType<NoContentResult>(second);
        Assert.Equal(1, await db.HomeworkCompletions.CountAsync());
    }

    [Fact]
    public async Task MarkCompleteRejectsWhenHomeworkDoesNotRequireIt()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            RequiresCompletion = false, CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();

        var result = await NewController(db, studentUser.Id, "Student")
            .MarkComplete(homework.Id, new Soovioo.Api.Homework.MarkHomeworkCompleteRequest(student.Id));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task MarkCompleteRejectsStudentNotTargetedByNarrowedHomework()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var studentUser = new User { Username = "zak", PasswordHash = "x", ActiveRole = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new StudentProfile { Person = new Person { FirstName = "Jan", LastName = "Novák", UserId = studentUser.Id, SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        var otherStudent = new StudentProfile { Person = new Person { FirstName = "Jiný", LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.AddRange(student, otherStudent);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Jen pro jiného", DueDate = new DateOnly(2026, 10, 24),
            RequiresCompletion = true, CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        db.HomeworkStudents.Add(new Soovioo.Api.Homework.HomeworkStudent { HomeworkId = homework.Id, StudentId = otherStudent.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, studentUser.Id, "Student")
            .MarkComplete(homework.Id, new Soovioo.Api.Homework.MarkHomeworkCompleteRequest(student.Id));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadAttachmentSucceedsAndIsReturnedByGetHomework()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        var controller = NewController(db, 7, "Teacher");

        var uploadResult = await controller.UploadAttachment(homework.Id, FakeFile("zadani.pdf"));
        var attachmentDto = Assert.IsType<Soovioo.Api.Homework.HomeworkAttachmentDto>(
            Assert.IsType<OkObjectResult>(uploadResult.Result).Value);
        Assert.Equal("zadani.pdf", attachmentDto.FileName);

        var getResult = await controller.GetHomework();
        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Homework.HomeworkDto>>(
            Assert.IsType<OkObjectResult>(getResult.Result).Value).ToList();
        Assert.Single(Assert.Single(items).Attachments);
    }

    [Fact]
    public async Task DownloadAttachmentForbiddenForUnrelatedParent()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        var teacherController = NewController(db, 7, "Teacher");
        var uploadResult = await teacherController.UploadAttachment(homework.Id, FakeFile("zadani.pdf"));
        var attachmentDto = (Soovioo.Api.Homework.HomeworkAttachmentDto)Assert.IsType<OkObjectResult>(uploadResult.Result).Value!;
        var unrelatedParent = new User { Username = "rodic4", PasswordHash = "x", ActiveRole = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(unrelatedParent);
        await db.SaveChangesAsync();

        var result = await NewController(db, unrelatedParent.Id, "Parent")
            .DownloadAttachment(homework.Id, attachmentDto.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteHomeworkRejectsTeacherWhoDoesNotTeachIt()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, "Teacher").DeleteHomework(homework.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteHomeworkRemovesAttachmentFileFromDisk()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var homework = new HomeworkEntity
        {
            SchoolClassId = schoolClass.Id, SubjectId = subject.Id, Text = "Úkol", DueDate = new DateOnly(2026, 10, 24),
            CreatedByName = "ucitel", SchoolId = SchoolId,
        };
        db.Homework.Add(homework);
        await db.SaveChangesAsync();
        var env = new FakeWebHostEnvironment();
        var controller = new Soovioo.Api.Homework.HomeworkController(
            db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(env), new Notifier(db, TimeProvider.System))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "7"),
                        new Claim(ClaimTypes.Role, "Teacher"),
                        new Claim(ClaimTypes.Name, "ucitel"),
                    ])),
                },
            },
        };
        var uploadResult = await controller.UploadAttachment(homework.Id, FakeFile("zadani.pdf"));
        var attachmentDto = (Soovioo.Api.Homework.HomeworkAttachmentDto)Assert.IsType<OkObjectResult>(uploadResult.Result).Value!;
        var storedFile = Directory.GetFiles(env.ContentRootPath, "*", SearchOption.AllDirectories).Single();
        Assert.True(File.Exists(storedFile));

        await controller.DeleteHomework(homework.Id);

        Assert.False(File.Exists(storedFile));
    }

    [Fact]
    public async Task UpdateHomeworkChangesTextAndDueDateForOwningTeacher()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var controller = NewController(db, 7, "Teacher");
        var created = Assert.IsType<Soovioo.Api.Homework.HomeworkDto>(
            Assert.IsType<CreatedAtActionResult>((await controller.CreateHomework(
                new Soovioo.Api.Homework.CreateHomeworkRequest(
                    schoolClass.Id, subject.Id, "Původní", new DateOnly(2026, 10, 24), false))).Result).Value);

        var result = await controller.UpdateHomework(created.Id, new Soovioo.Api.Homework.CreateHomeworkRequest(
            schoolClass.Id, subject.Id, "Opravené zadání", new DateOnly(2026, 10, 27), true));

        var dto = Assert.IsType<Soovioo.Api.Homework.HomeworkDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Opravené zadání", dto.Text);
        Assert.Equal(new DateOnly(2026, 10, 27), dto.DueDate);
        Assert.True(dto.RequiresCompletion);
    }

    [Fact]
    public async Task UpdateHomeworkRejectsTeacherWhoDoesNotTeachIt()
    {
        using var db = NewDb();
        var (schoolClass, subject) = await SeedClassAndSubjectAsync(db, teacherId: 7);
        var created = Assert.IsType<Soovioo.Api.Homework.HomeworkDto>(
            Assert.IsType<CreatedAtActionResult>((await NewController(db, 7, "Teacher").CreateHomework(
                new Soovioo.Api.Homework.CreateHomeworkRequest(
                    schoolClass.Id, subject.Id, "Text", new DateOnly(2026, 10, 24), false))).Result).Value);

        var result = await NewController(db, 999, "Teacher").UpdateHomework(created.Id,
            new Soovioo.Api.Homework.CreateHomeworkRequest(schoolClass.Id, subject.Id, "X", new DateOnly(2026, 10, 24), false));

        Assert.IsType<ForbidResult>(result.Result);
    }
}
