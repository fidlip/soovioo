using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Behavior;

public class BehaviorControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeCurrentSchoolContext(SchoolId));

    private static Soovioo.Api.Behavior.BehaviorController NewController(AppDbContext db, int userId, string role)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Name, "tester"),
        ]);
        return new Soovioo.Api.Behavior.BehaviorController(db, new FakeCurrentSchoolContext(SchoolId))
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
        var student = new Student { Person = new Person { FirstName = "Jan", LastName = "Novák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return (schoolClass, student);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.Pochvala)]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.Poznamka)]
    public async Task CreateAllowsAnyTeacherForPochvalaOrPoznamka(Soovioo.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Soovioo.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele)]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.DutkaTridnihoUcitele)]
    public async Task CreateAllowsOnlyTheClassTeacherForTridniUcitelTypes(Soovioo.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Soovioo.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele)]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.DutkaTridnihoUcitele)]
    public async Task CreateRejectsNonClassTeacherForTridniUcitelTypes(Soovioo.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Soovioo.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.NapomenutiReditele)]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.DutkaReditele)]
    public async Task CreateRejectsTeacherEvenAsClassTeacherForReditelTypes(Soovioo.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher")
            .CreateBehaviorRecord(new Soovioo.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.NapomenutiReditele)]
    [InlineData(Soovioo.Api.Behavior.BehaviorRecordType.DutkaReditele)]
    public async Task CreateAllowsAdminForReditelTypes(Soovioo.Api.Behavior.BehaviorRecordType type)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);

        var result = await NewController(db, 1, "Admin")
            .CreateBehaviorRecord(new Soovioo.Api.Behavior.CreateBehaviorRecordRequest(student.Id, type, "Text", new DateOnly(2026, 10, 17)));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task DeleteAppliesSameRuleAsCreate()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.NapomenutiTridnihoUcitele,
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
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, 7, "Teacher").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetBehaviorRecordsFiltersForUnrelatedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetBehaviorRecordsIncludesLinkedParent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic2", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentUser.Id, student.Id, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorRecords();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Behavior.BehaviorRecordDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(items);
    }

    [Fact]
    public async Task SetClassificationVelmiDobreAllowedForClassTeacher()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(
                student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, Soovioo.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetClassificationVelmiDobreRejectedForNonClassTeacher()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 999);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(
                student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, Soovioo.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorClassificationValue.Uspokojive)]
    [InlineData(Soovioo.Api.Behavior.BehaviorClassificationValue.Neuspokojive)]
    public async Task SetClassificationWorseThanVelmiDobreRejectedForClassTeacher(Soovioo.Api.Behavior.BehaviorClassificationValue value)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db, classTeacherId: 7);

        var result = await NewController(db, 7, "Teacher").SetClassification(
            new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, value));

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData(Soovioo.Api.Behavior.BehaviorClassificationValue.Uspokojive)]
    [InlineData(Soovioo.Api.Behavior.BehaviorClassificationValue.Neuspokojive)]
    public async Task SetClassificationWorseThanVelmiDobreAllowedForAdmin(Soovioo.Api.Behavior.BehaviorClassificationValue value)
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);

        var result = await NewController(db, 1, "Admin").SetClassification(
            new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, value));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SetClassificationUpsertReplacesExistingValue()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        var controller = NewController(db, 1, "Admin");
        await controller.SetClassification(new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(
            student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, Soovioo.Api.Behavior.BehaviorClassificationValue.Uspokojive));

        var result = await controller.SetClassification(new Soovioo.Api.Behavior.SetBehaviorClassificationRequest(
            student.Id, 2026, Soovioo.Api.Behavior.SchoolHalfYear.First, Soovioo.Api.Behavior.BehaviorClassificationValue.VelmiDobre));

        var dto = Assert.IsType<Soovioo.Api.Behavior.BehaviorClassificationDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(Soovioo.Api.Behavior.BehaviorClassificationValue.VelmiDobre, dto.Value);
        Assert.Equal(1, await db.BehaviorClassifications.CountAsync());
    }

    [Fact]
    public async Task MarkReadAllowedForLinkedParentAndIsIdempotent()
    {
        using var db = NewDb();
        var (_, student) = await SeedClassAndStudentAsync(db);
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
            Text = "Text", Date = new DateOnly(2026, 10, 17), RecordedByName = "ucitel", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic4", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parentUser.Id, student.Id, SchoolId);
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
        db.BehaviorRecords.Add(new Soovioo.Api.Behavior.BehaviorRecord
        {
            StudentId = student.Id, Type = Soovioo.Api.Behavior.BehaviorRecordType.Pochvala,
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
        db.BehaviorClassifications.Add(new Soovioo.Api.Behavior.BehaviorClassification
        {
            StudentId = student.Id, SchoolYearStartYear = 2026, HalfYear = Soovioo.Api.Behavior.SchoolHalfYear.First,
            Value = Soovioo.Api.Behavior.BehaviorClassificationValue.VelmiDobre, SetByName = "admin", SchoolId = SchoolId,
        });
        var parentUser = new User { Username = "rodic3", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(parentUser);
        await db.SaveChangesAsync();

        var result = await NewController(db, parentUser.Id, "Parent").GetBehaviorClassifications();

        var items = Assert.IsAssignableFrom<IEnumerable<Soovioo.Api.Behavior.BehaviorClassificationDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }
}
