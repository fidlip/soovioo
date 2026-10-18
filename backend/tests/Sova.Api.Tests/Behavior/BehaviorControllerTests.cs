using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Tests.Behavior;

public class BehaviorControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static Sova.Api.Behavior.BehaviorController NewController(AppDbContext db, int userId, string role)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Name, "tester"),
        ]);
        return new Sova.Api.Behavior.BehaviorController(db, new FakeCurrentSchoolContext(SchoolId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static async Task<(SchoolClass Class, Student Student)> SeedClassAndStudentAsync(AppDbContext db, int? classTeacherId = null)
    {
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId, ClassTeacherId = classTeacherId };
        db.SchoolClasses.Add(schoolClass);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return (schoolClass, student);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.Pochvala)]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.Poznamka)]
    public async Task CreateAllowsAnyTeacherForPochvalaOrPoznamka(Sova.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Sova.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele)]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.DutkaTridnihoUcitele)]
    public async Task CreateAllowsOnlyTheClassTeacherForTridniUcitelTypes(Sova.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Sova.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele)]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.DutkaTridnihoUcitele)]
    public async Task CreateRejectsNonClassTeacherForTridniUcitelTypes(Sova.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Sova.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.NapomenutiReditele)]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.DutkaReditele)]
    public async Task CreateRejectsTeacherEvenAsClassTeacherForReditelTypes(Sova.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Sova.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.NapomenutiReditele)]
    [InlineData(Sova.Api.Behavior.BehaviorRecordType.DutkaReditele)]
    public async Task CreateAllowsAdminForReditelTypes(Sova.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);

        var result = await NewController(db, 1, "Admin")
            .CreateBehaviorRecord(new Sova.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task DeleteAppliesSameRuleAsCreate()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "jiny", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();
        var record = await db.BehaviorRecords.FirstAsync();

        var result = await NewController(db, 7, "Teacher").DeleteBehaviorRecord(record.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetBehaviorRecordsUnfilteredForTeacher()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Sova.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetBehaviorRecordsFiltersForUnrelatedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Sova.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetBehaviorRecordsIncludesLinkedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic2", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        db.ParentStudents.Add(new ParentStudent { UserId = parentUser.Id, StudentId = student.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Sova.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task SetClassificationVelmiDobreAllowedForClassTeacher()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Sova.Api.Behavior.SetBehaviorClassificationRequest(
                student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, Sova.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetClassificationVelmiDobreRejectedForNonClassTeacher()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Sova.Api.Behavior.SetBehaviorClassificationRequest(
                student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, Sova.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorClassificationValue.Uspokojive)]
    [InlineData(Sova.Api.Behavior.BehaviorClassificationValue.Neuspokojive)]
    public async Task SetClassificationWorseThanVelmiDobreRejectedForClassTeacher(Sova.Api.Behavior.BehaviorClassificationValue value)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Sova.Api.Behavior.SetBehaviorClassificationRequest(student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, value));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Sova.Api.Behavior.BehaviorClassificationValue.Uspokojive)]
    [InlineData(Sova.Api.Behavior.BehaviorClassificationValue.Neuspokojive)]
    public async Task SetClassificationWorseThanVelmiDobreAllowedForAdmin(Sova.Api.Behavior.BehaviorClassificationValue value)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);

        var result = await NewController(db, 1, "Admin").SetClassification(
            new Sova.Api.Behavior.SetBehaviorClassificationRequest(student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, value));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetClassificationUpsertReplacesExistingValue()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        var controller = NewController(db, 1, "Admin");
        await controller.SetClassification(new Sova.Api.Behavior.SetBehaviorClassificationRequest(
            student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, Sova.Api.Behavior.BehaviorClassificationValue.Uspokojive));

        var result = await controller.SetClassification(new Sova.Api.Behavior.SetBehaviorClassificationRequest(
            student.Id, 2026, Sova.Api.Behavior.SchoolHalfYear.First, Sova.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        var dto = Assert.IsType<Sova.Api.Behavior.BehaviorClassificationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(Sova.Api.Behavior.BehaviorClassificationValue.VelmiDobre, dto.Value);
        Assert.Equal(1, await db.BehaviorClassifications.CountAsync());
    }

    [Fact]
    public async Task MarkReadAllowedForLinkedParentAndIsIdempotent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic4", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        db.ParentStudents.Add(new ParentStudent { UserId = parentUser.Id, StudentId = student.Id, SchoolId = SchoolId });
        await db.SaveChangesAsync();
        var record = await db.BehaviorRecords.FirstAsync();
        var controller = NewController(db, parentUser.Id, "Parent");

        var firstResult = await controller.MarkBehaviorRecordRead(record.Id);
        Assert.IsType<NoContentResult>(firstResult);
        var firstReadAt = (await db.BehaviorRecords.FirstAsync()).ReadAt;
        Assert.NotNull(firstReadAt);

        var secondResult = await controller.MarkBehaviorRecordRead(record.Id);
        Assert.IsType<NoContentResult>(secondResult);
        Assert.Equal(firstReadAt, (await db.BehaviorRecords.FirstAsync()).ReadAt);
    }

    [Fact]
    public async Task MarkReadRejectedForUnrelatedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Sova.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Sova.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic5", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        var record = await db.BehaviorRecords.FirstAsync();

        var result = await NewController(db, parentUser.Id, "Parent").MarkBehaviorRecordRead(record.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.Null((await db.BehaviorRecords.FirstAsync()).ReadAt);
    }

    [Fact]
    public async Task GetBehaviorClassificationsFiltersForUnrelatedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorClassifications.Add(new Sova.Api.Behavior.BehaviorClassification
        {
            StudentId = student.Id, SchoolYearStartYear = 2026, HalfYear = Sova.Api.Behavior.SchoolHalfYear.First,
            Value = Sova.Api.Behavior.BehaviorClassificationValue.VelmiDobre, SetByName = "admin", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic3", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorClassifications();

        var items = Assert.IsAssignableFrom<IEnumerable<Sova.Api.Behavior.BehaviorClassificationDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }
}
