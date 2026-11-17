using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;

namespace Soovioo.Api.Tests.Evidence;

public class SchoolSetupTests
{
    private readonly AppDbContext db;
    private readonly School school;

    public SchoolSetupTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using (var setup = new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: true)))
        {
            var created = new School { Name = "ZŠ Nová", Slug = "nova" };
            RoleSeeder.AddMissing(created);
            setup.Schools.Add(created);
            setup.SaveChanges();
        }
        db = new AppDbContext(options, new FakeCurrentSchoolContext(1));
        school = db.Schools.Single();
    }

    private static SchoolSetupRequest Request(
        string? email = "skola@nova.cz", string? bankAccount = "19-2000145399/0800", string? district = "Blansko",
        string name = " Gymnázium Nová Ves ") => new(
        name, SchoolType.Secondary, ReportCardMarkFormat.Digits,
        " Školní 1 ", "Nová Ves", "123 45", district, "Mgr. Jana Nová", email, "777 123 456",
        "123456789", "", bankAccount);

    [Fact]
    public async Task Saves_everything_and_counts_the_school_as_set_up()
    {
        var result = await new MySchoolController(db, new FakeCurrentSchoolContext(1)).CompleteSetup(Request());

        var dto = Assert.IsType<SchoolDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(dto.SetupCompletedAt);
        Assert.Equal("Gymnázium Nová Ves", school.Name);
        Assert.Equal(SchoolType.Secondary, school.Type);
        Assert.Equal(ReportCardMarkFormat.Digits, school.ReportCardMarkFormat);
        Assert.Equal("Školní 1", school.Street);
        Assert.Equal("Blansko", school.District);
        Assert.Equal("123456789", school.Izo);
        Assert.Null(school.RedIzo);
        Assert.Equal("CZ6508000000192000145399", school.BankAccountNumber);
        Assert.Equal("Student", (await db.Roles.SingleAsync(r => r.BuiltIn == UserRole.Student)).Name);
    }

    [Theory]
    [InlineData("email", "neni-email", null, null)]
    [InlineData("bankAccountNumber", null, "123", null)]
    [InlineData("district", null, null, "Atlantida")]
    public async Task A_wrong_field_saves_nothing(string field, string? email, string? bankAccount, string? district)
    {
        var result = await new MySchoolController(db, new FakeCurrentSchoolContext(1))
            .CompleteSetup(Request(email ?? "skola@nova.cz", bankAccount ?? "19-2000145399/0800", district ?? "Blansko"));

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Contains(field, problem.Errors.Keys);
        Assert.Null(school.SetupCompletedAt);
        Assert.Null(school.Street);
    }

    [Fact]
    public async Task The_name_is_required()
    {
        var result = await new MySchoolController(db, new FakeCurrentSchoolContext(1)).CompleteSetup(Request(name: "  "));

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Contains("name", problem.Errors.Keys);
        Assert.Equal("ZŠ Nová", school.Name);
    }
}
