using System.Text;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Matrika;
using Soovioo.Api.Reference;

namespace Soovioo.Api.Tests.Matrika;

public class MatrikaExportServiceTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static string DecodeXml(byte[] bytes) => Encoding.GetEncoding("Windows-1250").GetString(bytes);

    private static School SeedSchool(AppDbContext db, string? izo = "308000048")
    {
        var school = new School { Id = SchoolId, Name = "Testovací škola", Slug = "test", Izo = izo };
        db.Schools.Add(school);
        db.SaveChanges();
        return school;
    }

    private static SchoolClass SeedClass(AppDbContext db, int? rocnik = 5)
    {
        var schoolClass = new SchoolClass { Name = "5.A", SchoolId = SchoolId, Rocnik = rocnik };
        db.SchoolClasses.Add(schoolClass);
        db.SaveChanges();
        return schoolClass;
    }

    private static Student SeedStudent(
        AppDbContext db,
        SchoolClass schoolClass,
        string? rodneCislo = null,
        DateOnly? dateOfBirth = null,
        Gender? gender = null,
        string? citizenshipQualifier = null,
        string? countryCode = null,
        string? municipalityCode = null)
    {
        var student = new Student
        {
            FirstName = "Jan",
            LastName = "Novák",
            SchoolClassId = schoolClass.Id,
            SchoolId = SchoolId,
            RodneCislo = rodneCislo,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            CitizenshipQualifier = citizenshipQualifier,
            CountryCode = countryCode,
            MunicipalityCode = municipalityCode,
        };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    [Fact]
    public async Task UsesExplicitRodneCisloWhenPresent()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, rodneCislo: "0503109999", dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var xml = DecodeXml(outcome.Content!);
        Assert.Contains("<RODC>0503109999</RODC>", xml);
    }

    [Theory]
    [InlineData(Gender.Male, "150310X")] // RR=15, NN=03 (no +50), DD=10
    [InlineData(Gender.Female, "155310X")] // NN = 03 + 50 = 53
    public async Task BuildsPseudoRodneCisloPerDocumentedRule(Gender gender, string expectedPrefix)
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: gender);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var xml = DecodeXml(outcome.Content!);
        Assert.Contains($"<RODC>{expectedPrefix}", xml);
    }

    [Theory]
    [InlineData(1, "2015aa")] // January
    [InlineData(8, "2015aa")] // August - still first half
    [InlineData(9, "2015bb")] // September - second half
    [InlineData(12, "2015bb")] // December
    public async Task BuildsDatNarozAsHalfYearBucket(int birthMonth, string expected)
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, birthMonth, 15), gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var xml = DecodeXml(outcome.Content!);
        Assert.Contains($"<DAT_NAROZ>{expected}</DAT_NAROZ>", xml);
    }

    [Fact]
    public async Task PohlaviMapsFemaleToTwoAndMaleToOne()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Female);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.Contains("<POHLAVI>2</POHLAVI>", DecodeXml(outcome.Content!));
    }

    [Fact]
    public async Task FileNameFollowsZIzoCastConvention()
    {
        using var db = NewDb();
        SeedSchool(db, izo: "308000048");
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        Assert.Equal("Z308000048_01.xml", outcome.FileName);
    }

    [Fact]
    public async Task FailsWithClearErrorWhenSchoolHasNoIzo()
    {
        using var db = NewDb();
        SeedSchool(db, izo: null);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("IZO"));
    }

    [Theory]
    [InlineData("", "777123456", "jana@test.cz", "01")]
    [InlineData("Jana Testová", "", "jana@test.cz", "01")]
    [InlineData("Jana Testová", "777123456", "", "01")]
    [InlineData("Jana Testová", "777123456", "jana@test.cz", "")]
    public async Task FailsWhenAnyContactFieldIsMissing(string contactName, string contactPhone, string contactEmail, string cast)
    {
        using var db = NewDb();
        SeedSchool(db);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), cast, contactName, contactPhone, contactEmail);

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Errors);
    }

    [Fact]
    public async Task FailsPerStudentWhenClassHasNoRocnik()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db, rocnik: null);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("ročník"));
    }

    [Fact]
    public async Task FailsPerStudentWhenGenderIsMissing()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: null);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("pohlaví"));
    }

    [Fact]
    public async Task FailsPerStudentWhenDateOfBirthIsMissing()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: null, gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.False(outcome.Success);
        Assert.Contains(outcome.Errors, e => e.Contains("narození"));
    }

    [Fact]
    public async Task EmitsKstprStprObecbOkresbWhenStudentHasThem()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        db.Districts.Add(new District { Code = "CZ0100", Name = "Praha" });
        db.Municipalities.Add(new Municipality { Code = "500054", Name = "Praha 1", DistrictCode = "CZ0100" });
        db.SaveChanges();
        SeedStudent(
            db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Male,
            citizenshipQualifier: "3", countryCode: "203", municipalityCode: "500054");

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var xml = DecodeXml(outcome.Content!);
        Assert.Contains("<KSTPR>3</KSTPR>", xml);
        Assert.Contains("<STPR>203</STPR>", xml);
        Assert.Contains("<OBECB>500054</OBECB>", xml);
        Assert.Contains("<OKRESB>CZ0100</OKRESB>", xml);
    }

    [Fact]
    public async Task OmitsKstprStprObecbOkresbWhenStudentLacksThem()
    {
        using var db = NewDb();
        SeedSchool(db);
        var schoolClass = SeedClass(db);
        SeedStudent(db, schoolClass, dateOfBirth: new DateOnly(2015, 3, 10), gender: Gender.Male);

        var outcome = await new MatrikaExportService(db, new FakeCurrentSchoolContext(SchoolId))
            .BuildBaseFileAsync(new DateOnly(2026, 9, 30), "01", "Jana Testová", "777123456", "jana@test.cz");

        Assert.True(outcome.Success, string.Join("; ", outcome.Errors));
        var xml = DecodeXml(outcome.Content!);
        Assert.DoesNotContain("<KSTPR>", xml);
        Assert.DoesNotContain("<STPR>", xml);
        Assert.DoesNotContain("<OBECB>", xml);
        Assert.DoesNotContain("<OKRESB>", xml);
    }
}
