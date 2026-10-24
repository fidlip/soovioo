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

    [Fact]
    public async Task UpdateUserRenamesAndChangesRole()
    {
        using var db = NewDb();
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"]));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("ucitelka", dto.Username);
    }

    [Fact]
    public async Task AddUserForcesPasswordChangeOnFirstLogin()
    {
        using var db = NewDb();

        await NewController(db, 999).AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"]));

        var created = await db.Users.SingleAsync(u => u.Username == "novy");
        Assert.True(created.MustChangePassword);
    }

    [Fact]
    public async Task UpdateUserWithNewPasswordForcesPasswordChange()
    {
        using var db = NewDb();
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitel", ["Teacher"], "nove-heslo"));

        Assert.True((await db.Users.FindAsync(user.Id))!.MustChangePassword);
    }

    [Fact]
    public async Task UpdateUserWithoutPasswordLeavesMustChangePasswordUnchanged()
    {
        using var db = NewDb();
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId, MustChangePassword = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"]));

        Assert.False((await db.Users.FindAsync(user.Id))!.MustChangePassword);
    }

    [Fact]
    public async Task AddUserWithStudentRoleRequiresStudentId()
    {
        using var db = NewDb();

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("zak", "heslo123", ["Student"]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddUserWithStudentRoleLinksTheStudent()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999)
            .AddUser(new CreateUserRequest("zak", "heslo123", ["Student"], StudentId: student.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(student.Id, dto.StudentId);
        Assert.Equal(dto.Id, (await db.Students.FindAsync(student.Id))!.UserId);
    }

    [Fact]
    public async Task AddUserWithStudentRoleRejectsAnAlreadyLinkedStudent()
    {
        using var db = NewDb();
        var existingAccount = new User { Username = "zak1", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(existingAccount);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId, UserId = existingAccount.Id };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999)
            .AddUser(new CreateUserRequest("zak2", "heslo123", ["Student"], StudentId: student.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateUserCanRelinkStudentRoleToADifferentStudent()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var oldStudent = new Student { FirstName = "Starý", LastName = "Žák", SchoolClassId = 1, SchoolId = SchoolId, UserId = studentUser.Id };
        var newStudent = new Student { FirstName = "Nový", LastName = "Žák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.AddRange(oldStudent, newStudent);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999)
            .UpdateUser(studentUser.Id, new UpdateUserRequest("zak", ["Student"], StudentId: newStudent.Id));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(newStudent.Id, dto.StudentId);
        Assert.Null((await db.Students.FindAsync(oldStudent.Id))!.UserId);
        Assert.Equal(studentUser.Id, (await db.Students.FindAsync(newStudent.Id))!.UserId);
    }

    [Fact]
    public async Task UpdateUserAwayFromStudentRoleUnlinksTheStudent()
    {
        using var db = NewDb();
        var studentUser = new User { Username = "zak", PasswordHash = "x", Role = UserRole.Student, SchoolId = SchoolId };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId, UserId = studentUser.Id };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        await NewController(db, 999).UpdateUser(studentUser.Id, new UpdateUserRequest("zak", ["Parent"]));

        Assert.Null((await db.Students.FindAsync(student.Id))!.UserId);
    }

    [Fact]
    public async Task DeleteUserRejectsDeletingYourself()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        var otherAdmin = new User { Username = "admin2", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        db.Users.AddRange(admin, otherAdmin);
        await db.SaveChangesAsync();

        var result = await NewController(db, admin.Id).DeleteUser(admin.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(await db.Users.FindAsync(admin.Id));
    }

    [Fact]
    public async Task DeleteUserRejectsRemovingTheLastAdmin()
    {
        using var db = NewDb();
        var onlyAdmin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        db.Users.Add(onlyAdmin);
        await db.SaveChangesAsync();

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
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).SendInvite(user.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SendInviteGeneratesATokenAndEmailsTheLink()
    {
        using var db = NewDb();
        db.Schools.Add(new School { Id = SchoolId, Name = "Demo škola", Slug = "demo" });
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId, Person = new Soovioo.Api.People.Person { Email = "a@b.cz", SchoolId = SchoolId } };
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
        var user = new User { Username = "ucitel", PasswordHash = "x", Role = UserRole.Teacher, SchoolId = SchoolId, Person = new Soovioo.Api.People.Person { Email = "a@b.cz", SchoolId = SchoolId } };
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
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender)
            .AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"], Email: "a@b.cz"));

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

        var result = await NewController(db, 999)
            .AddUser(new CreateUserRequest("novy", null, ["Parent"], Email: "a@b.cz"));

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
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender).AddUser(new CreateUserRequest("novy", " ", ["Teacher"]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.Users.AnyAsync());
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task AddUserWithoutEmailSendsNoInvite()
    {
        using var db = NewDb();
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender).AddUser(new CreateUserRequest("novy", "heslo123", ["Teacher"]));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Null(dto.Invite);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task AddUserStillCreatesTheAccountWhenInvitesAreNotConfigured()
    {
        using var db = NewDb();
        var emailSender = new FakeEmailSender();

        var result = await NewController(db, 999, emailSender, new AppLinkOptions { PathBasedTenant = true })
            .AddUser(new CreateUserRequest("novy", null, ["Teacher"], Email: "a@b.cz"));

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

        var result = await NewController(db, 999, new FailingEmailSender())
            .AddUser(new CreateUserRequest("novy", null, ["Teacher"], Email: "a@b.cz"));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(nameof(InviteOutcome.Failed), dto.Invite);
        Assert.True(await db.Users.AnyAsync(u => u.Username == "novy"));
    }

    [Fact]
    public async Task UpdateUserRejectsRemovingYourOwnAdminRole()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, Roles = [UserRole.Admin, UserRole.Teacher], SchoolId = SchoolId };
        var otherAdmin = new User { Username = "admin2", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        db.Users.AddRange(admin, otherAdmin);
        await db.SaveChangesAsync();

        var result = await NewController(db, admin.Id).UpdateUser(admin.Id, new UpdateUserRequest("admin", ["Teacher"]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(UserRole.Admin, (await db.Users.FindAsync(admin.Id))!.Roles);
    }

    [Fact]
    public async Task DeleteUserSucceedsAndRemovesParentLinks()
    {
        using var db = NewDb();
        var admin = new User { Username = "admin", PasswordHash = "x", Role = UserRole.Admin, SchoolId = SchoolId };
        var parent = new User { Username = "rodic", PasswordHash = "x", Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.AddRange(admin, parent);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(parent.Id, 1, SchoolId);
        await db.SaveChangesAsync();

        var result = await NewController(db, admin.Id).DeleteUser(parent.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Users.FindAsync(parent.Id));
        Assert.Empty(db.ChildIdsOf(parent.Id));
    }

    [Fact]
    public async Task AddUserWithSeveralRolesStartsInTheFirstOne()
    {
        using var db = NewDb();

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("ucitelka", "heslo123", ["Parent", "Teacher"]));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Teacher", dto.Role);
        Assert.Equal(["Teacher", "Parent"], dto.Roles);
    }

    [Fact]
    public async Task StudentRoleCannotBeCombined()
    {
        using var db = NewDb();

        var result = await NewController(db, 999).AddUser(new CreateUserRequest("zak", "heslo123", ["Student", "Parent"]));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemovingTheActiveRoleFallsBackToAnotherOne()
    {
        using var db = NewDb();
        var user = new User { Username = "ucitelka", PasswordHash = "x", Roles = [UserRole.Teacher, UserRole.Parent], Role = UserRole.Parent, SchoolId = SchoolId };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await NewController(db, 999).UpdateUser(user.Id, new UpdateUserRequest("ucitelka", ["Teacher"]));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Teacher", dto.Role);
        Assert.Equal(["Teacher"], dto.Roles);
    }

    [Fact]
    public async Task AddUserCreatesPersonWithProfilesOfItsRoles()
    {
        using var db = NewDb();

        var result = await NewController(db, 999).AddUser(new CreateUserRequest(
            "novakova", "heslo123", ["Teacher", "Parent"], Phone: "777", Email: "j@n.cz",
            FirstName: "Jana", LastName: "Nováková"));

        var dto = Assert.IsType<UserDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("Jana Nováková", dto.Name);
        Assert.Equal("j@n.cz", dto.Email);
        var person = await db.Persons.Include(p => p.TeacherProfile).Include(p => p.ParentProfile).SingleAsync();
        Assert.Equal(dto.Id, person.UserId);
        Assert.NotNull(person.TeacherProfile);
        Assert.NotNull(person.ParentProfile);
    }

    [Fact]
    public async Task RemovingParentRoleKeepsChildrenForWhenItComesBack()
    {
        using var db = NewDb();
        var created = await NewController(db, 999).AddUser(new CreateUserRequest("mama", "heslo123", ["Parent"]));
        var id = ((UserDto)((CreatedAtActionResult)created.Result!).Value!).Id;
        var child = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(child);
        await db.SaveChangesAsync();
        await db.LinkChildAsync(id, child.Id, SchoolId);
        await db.SaveChangesAsync();

        await NewController(db, 999).UpdateUser(id, new UpdateUserRequest("mama", ["Teacher"]));
        await NewController(db, 999).UpdateUser(id, new UpdateUserRequest("mama", ["Teacher", "Parent"]));

        Assert.Equal([child.Id], db.ChildIdsOf(id));
    }

    [Fact]
    public async Task SetTeacherProfileStoresQualification()
    {
        using var db = NewDb();
        var created = await NewController(db, 999).AddUser(new CreateUserRequest("ucitel", "heslo123", ["Teacher"]));
        var id = ((UserDto)((CreatedAtActionResult)created.Result!).Value!).Id;

        var result = await NewController(db, 999).SetTeacherProfile(id, new SetTeacherProfileRequest(" M, F "));

        var dto = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("M, F", dto.Qualification);
    }

    private class FailingEmailSender : Soovioo.Api.Email.IEmailSender
    {
        public Task SendAsync(string to, string subject, string htmlBody) =>
            throw new InvalidOperationException("SMTP down");
    }
}
