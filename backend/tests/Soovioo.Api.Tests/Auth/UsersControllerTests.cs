using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Auth;

public class UsersControllerTests
{
    private const int SchoolId = 1;

    private static readonly AppLinkOptions LinkOptions =
        new() { PathBasedTenant = true, FrontendBaseUrl = "http://localhost:4200" };

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    private static UsersController NewController(
        AppDbContext db, int currentUserId, Soovioo.Api.Email.IEmailSender? emailSender = null, AppLinkOptions? linkOptions = null)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())]);
        return new UsersController(
            db, new FakeCurrentSchoolContext(SchoolId), emailSender ?? new FakeEmailSender(), Options.Create(linkOptions ?? LinkOptions),
            AuthTestServices.RefreshTokens(db), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    /// <summary>A person without an account, with the profiles of
    /// <paramref name="roles"/> (Student = a pupil).</summary>
    private static Person NewPerson(AppDbContext db, string lastName, string? email, params UserRole[] roles)
    {
        var person = new Person { FirstName = "Jana", LastName = lastName, Email = email, SchoolId = SchoolId };
        if (roles.Contains(UserRole.Teacher))
        {
            person.TeacherProfile = new TeacherProfile { Person = person, SchoolId = SchoolId };
        }
        if (roles.Contains(UserRole.Parent))
        {
            person.ParentProfile = new ParentProfile { Person = person, SchoolId = SchoolId };
        }
        if (roles.Contains(UserRole.Student))
        {
            db.Students.Add(new StudentProfile { Person = person, SchoolClassId = 1, SchoolId = SchoolId });
        }
        db.Persons.Add(person);
        db.SaveChanges();
        return person;
    }

    /// <summary>An account with roles, on a person with their profiles.</summary>
    private static User NewUser(AppDbContext db, string username, params UserRole[] roles)
    {
        var user = new User { Username = username, PasswordHash = "x", Roles = [.. roles], ActiveRole = roles[0], SchoolId = SchoolId };
        db.Users.Add(user);
        db.SaveChanges();
        NewPerson(db, username, null, roles).UserId = user.Id;
        db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task UpdateUserRenamesAndChangesRole()
    {
        using var db = NewDb();
        var user = NewUser(db, "ucitel", UserRole.Teacher);

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"], user.Person!.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("ucitelka", dto.Username);
    }

    [Fact]
    public async Task AddUserForcesPasswordChangeOnFirstLogin()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Nová", null, UserRole.Teacher);

        await NewController(db, 999).AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"], person.Id));

        var created = await db.Users.SingleAsync(u => u.Username == "novy");
        Assert.True(created.MustChangePassword);
        Assert.Equal(created.Id, person.UserId);
    }

    [Fact]
    public async Task UpdateUserWithNewPasswordForcesPasswordChange()
    {
        using var db = NewDb();
        var user = NewUser(db, "ucitel", UserRole.Teacher);

        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitel", ["Teacher"], user.Person!.Id, "nove-heslo"));

        Assert.True((await db.Users.FindAsync(user.Id))!.MustChangePassword);
    }

    [Fact]
    public async Task UpdateUserWithoutPasswordLeavesMustChangePasswordUnchanged()
    {
        using var db = NewDb();
        var user = NewUser(db, "ucitel", UserRole.Teacher);

        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"], user.Person!.Id));

        Assert.False((await db.Users.FindAsync(user.Id))!.MustChangePassword);
    }

    [Fact]
    public async Task AddUserRequiresAPerson()
    {
        using var db = NewDb();

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("zak", "heslo123", ["Student"], null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.Users.AnyAsync());
    }

    [Theory]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Parent)]
    [InlineData(UserRole.Student)]
    public async Task AddUserRequiresTheProfileOfEachRole(UserRole role)
    {
        using var db = NewDb();
        var person = NewPerson(db, "Bez profilu", null);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("novy", "heslo123", [role.ToString()], person.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null(person.UserId);
    }

    [Fact]
    public async Task AnAdminNeedsNoProfile()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Správcová", null);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("spravce", "heslo123", ["Admin"], person.Id));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task AddUserWithStudentRoleIsThePupilsAccount()
    {
        using var db = NewDb();
        var pupil = NewPerson(db, "Novák", null, UserRole.Student);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("zak", "heslo123", ["Student"], pupil.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(pupil.Student!.Id, dto.StudentId);
        Assert.Equal(dto.Id, pupil.UserId);
    }

    [Fact]
    public async Task AddUserRejectsAPersonWithAnAccount()
    {
        using var db = NewDb();
        var existing = NewUser(db, "zak1", UserRole.Student);

        var result = await NewController(db, 999)
            .AddUser(new CreateUserRequest("zak2", "heslo123", ["Student"], existing.Person!.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateUserMovesTheAccountToAnotherPersonAndLeavesTheFirst()
    {
        using var db = NewDb();
        var user = NewUser(db, "zak", UserRole.Student);
        var first = user.Person!;
        var other = NewPerson(db, "Nový", null, UserRole.Student);

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("zak", ["Student"], other.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(other.Student!.Id, dto.StudentId);
        Assert.Null(first.UserId);
        Assert.Equal(user.Id, other.UserId);
        Assert.Equal(2, await db.Persons.CountAsync());
    }

    [Fact]
    public async Task DeleteUserLeavesThePerson()
    {
        using var db = NewDb();
        var admin = NewUser(db, "admin", UserRole.Admin);
        var parent = NewUser(db, "rodic", UserRole.Parent);
        var person = parent.Person!;
        await db.LinkChildAsync(parent.Id, 1, SchoolId);
        await db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await NewController(db, admin.Id).DeleteUser(parent.Id));

        Assert.Null(await db.Users.FindAsync(parent.Id));
        Assert.Null(person.UserId);
        Assert.Single(await db.ParentStudents.IgnoreQueryFilters().Where(ps => ps.ParentProfileId == person.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteUserRejectsDeletingYourself()
    {
        using var db = NewDb();
        var admin = NewUser(db, "admin", UserRole.Admin);
        NewUser(db, "admin2", UserRole.Admin);

        var result = await NewController(db, admin.Id).DeleteUser(admin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Users.FindAsync(admin.Id));
    }

    [Fact]
    public async Task DeleteUserRejectsRemovingTheLastAdmin()
    {
        using var db = NewDb();
        var onlyAdmin = NewUser(db, "admin", UserRole.Admin);

        // Acting as a different (hypothetical) caller id so the self-delete
        // guard doesn't mask the last-admin guard this test targets.
        var result = await NewController(db, 999).DeleteUser(onlyAdmin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Users.FindAsync(onlyAdmin.Id));
    }

    [Fact]
    public async Task SendInviteRejectsUserWithoutEmail()
    {
        using var db = NewDb();
        var user = NewUser(db, "ucitel", UserRole.Teacher);

        var result = await NewController(db, 999).SendInvite(user.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SendInviteGeneratesATokenAndEmailsTheLink()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        var user = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = UserRole.Teacher, SchoolId = SchoolId, Person = new Person { Email = "a@b.cz", SchoolId = SchoolId } };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender).SendInvite(user.Id);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Users.FindAsync(user.Id) ?? throw new InvalidOperationException();
        Assert.NotNull(reloaded.InvitationToken);
        Assert.True(reloaded.InvitationTokenExpiresAt > DateTimeOffset.UtcNow);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("a@b.cz", sent.To);
        Assert.Contains(reloaded.InvitationToken!, sent.HtmlBody);
        Assert.Contains("http://localhost:4200/demo/accept-invite?token=", sent.HtmlBody);
    }

    [Fact]
    public async Task SendInviteFailsWhenFrontendLinkIsNotConfigured()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        var user = new User { Username = "ucitel", PasswordHash = "x", ActiveRole = UserRole.Teacher, SchoolId = SchoolId, Person = new Person { Email = "a@b.cz", SchoolId = SchoolId } };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999, linkOptions: new AppLinkOptions { PathBasedTenant = true })
            .SendInvite(user.Id);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
    }

    [Fact]
    public async Task AddUserWithEmailSendsTheInviteRightAway()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        await db.SaveChangesAsync();
        var person = NewPerson(db, "Nová", "a@b.cz", UserRole.Teacher);
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender)
            .AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"], person.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(nameof(InviteOutcome.Sent), dto.Invite);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("a@b.cz", sent.To);
        var created = await db.Users.SingleAsync(u => u.Username == "novy");
        Assert.Contains(created.InvitationToken!, sent.HtmlBody);
        Assert.True(created.MustChangePassword);
    }

    [Fact]
    public async Task AddUserWithEmailNeedsNoPasswordAndCannotLogInWithoutTheInvite()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        await db.SaveChangesAsync();
        var person = NewPerson(db, "Nová", "a@b.cz", UserRole.Parent);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("novy", null, ["Parent"], person.Id));

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var created = await db.Users.SingleAsync(u => u.Username == "novy");
        Assert.Equal(string.Empty, created.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Failed,
            new PasswordHasher<User>().VerifyHashedPassword(created, created.PasswordHash, ""));
        Assert.NotNull(created.InvitationToken);
    }

    [Fact]
    public async Task AddUserWithoutEmailRequiresAPassword()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Nová", null, UserRole.Teacher);
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender).AddUser(new CreateUserRequest("novy", " ", ["Teacher"], person.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.Users.AnyAsync());
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task AddUserWithoutEmailSendsNoInvite()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Nová", null, UserRole.Teacher);
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender).AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"], person.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Null(dto.Invite);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task AddUserStillCreatesTheAccountWhenInvitesAreNotConfigured()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Nová", "a@b.cz", UserRole.Teacher);
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender, new AppLinkOptions { PathBasedTenant = true })
            .AddUser(new CreateUserRequest("novy", null, ["Teacher"], person.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(nameof(InviteOutcome.NotConfigured), dto.Invite);
        Assert.Empty(emailSender.Sent);
        Assert.True(await db.Users.AnyAsync(u => u.Username == "novy"));
    }

    [Fact]
    public async Task AddUserStillCreatesTheAccountWhenTheInviteFailsToSend()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        await db.SaveChangesAsync();
        var person = NewPerson(db, "Nová", "a@b.cz", UserRole.Teacher);

        var result = await NewController(db, 999, new FailingEmailSender())
            .AddUser(new CreateUserRequest("novy", null, ["Teacher"], person.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(nameof(InviteOutcome.Failed), dto.Invite);
        Assert.True(await db.Users.AnyAsync(u => u.Username == "novy"));
    }

    [Fact]
    public async Task UpdateUserRejectsRemovingYourOwnAdminRole()
    {
        using var db = NewDb();
        var admin = NewUser(db, "admin", UserRole.Admin, UserRole.Teacher);
        NewUser(db, "admin2", UserRole.Admin);

        var result = await NewController(db, admin.Id).UpdateUser(admin.Id, new UpdateUserRequest("admin", ["Teacher"], admin.Person!.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(UserRole.Admin, (await db.Users.FindAsync(admin.Id))!.Roles);
    }

    [Fact]
    public async Task AddUserWithSeveralRolesStartsInTheFirstOne()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Učitelka", null, UserRole.Teacher, UserRole.Parent);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("ucitelka", "heslo123", ["Parent", "Teacher"], person.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Teacher", dto.Role);
        Assert.Equal(["Teacher", "Parent"], dto.Roles);
    }

    [Fact]
    public async Task StudentRoleCannotBeCombined()
    {
        using var db = NewDb();
        var person = NewPerson(db, "Novák", null, UserRole.Student, UserRole.Parent);

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("zak", "heslo123", ["Student", "Parent"], person.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemovingTheActiveRoleFallsBackToAnotherOne()
    {
        using var db = NewDb();
        var user = NewUser(db, "ucitelka", UserRole.Parent, UserRole.Teacher);

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"], user.Person!.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Teacher", dto.Role);
        Assert.Equal(["Teacher"], dto.Roles);
    }

    [Fact]
    public async Task RemovingParentRoleKeepsChildrenForWhenItComesBack()
    {
        using var db = NewDb();
        var user = NewUser(db, "mama", UserRole.Parent, UserRole.Teacher);
        await db.LinkChildAsync(user.Id, 1, SchoolId);
        await db.SaveChangesAsync();

        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("mama", ["Teacher"], user.Person!.Id));
        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("mama", ["Teacher", "Parent"], user.Person.Id));

        Assert.Single(await db.ParentStudents.IgnoreQueryFilters().Where(ps => ps.ParentProfileId == user.Person.Id).ToListAsync());
    }

    private class FailingEmailSender : Soovioo.Api.Email.IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody) =>
            throw new InvalidOperationException("SMTP down");
    }
}
