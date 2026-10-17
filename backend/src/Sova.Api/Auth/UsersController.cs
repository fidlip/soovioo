using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Auth;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await db.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.Role.ToString(), u.Phone, u.Email))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> AddUser(CreateUserRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return BadRequest("Username already exists.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role) || role == UserRole.SuperAdmin)
        {
            return BadRequest("Unknown role.");
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = string.Empty,
            Role = role,
            // Admin only ever manages their own school — never trust a
            // client-supplied SchoolId here.
            SchoolId = currentSchool.SchoolId,
            Phone = request.Phone,
            Email = request.Email,
            // Admin picked this password, not the account's owner — force a
            // change on first login (see User.MustChangePassword).
            MustChangePassword = true,
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dto = new UserDto(user.Id, user.Username, user.Role.ToString(), user.Phone, user.Email);
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, dto);
    }

    /// <summary>Password is optional — omit it to change only username/role.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, UpdateUserRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await db.Users.AnyAsync(u => u.Id != id && u.Username == request.Username))
        {
            return BadRequest("Username already exists.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role) || role == UserRole.SuperAdmin)
        {
            return BadRequest("Unknown role.");
        }

        if (user.Role == UserRole.Admin && role != UserRole.Admin && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        user.Username = request.Username;
        user.Role = role;
        user.Phone = request.Phone;
        user.Email = request.Email;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            // Admin reset this password on the user's behalf — same forced
            // first-login change as a brand new account, see AddUser above.
            user.MustChangePassword = true;
        }

        await db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Username, user.Role.ToString(), user.Phone, user.Email));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == currentUserId)
        {
            return BadRequest("Nelze smazat vlastní účet.");
        }

        if (user.Role == UserRole.Admin && await IsLastAdminAsync(id))
        {
            return BadRequest("Poslední admin školy — nejdřív přidejte jiného.");
        }

        db.ParentStudents.RemoveRange(db.ParentStudents.Where(ps => ps.UserId == id));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>True if <paramref name="userId"/> is the only Admin left for
    /// the current school — deleting/demoting them would leave the school
    /// with no one able to manage it.</summary>
    private async Task<bool> IsLastAdminAsync(int userId) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin);
}

public record CreateUserRequest(string Username, string Password, string Role, string? Phone = null, string? Email = null);

public record UpdateUserRequest(string Username, string Role, string? Password = null, string? Phone = null, string? Email = null);

public record UserDto(int Id, string Username, string Role, string? Phone, string? Email);
