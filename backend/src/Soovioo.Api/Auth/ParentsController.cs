using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Minimal, name-only Parent list for staff pickers (e.g. the
/// parent↔student link form) — same pattern as
/// <see cref="TeachersController"/>, unlike the full <see cref="UsersController"/>
/// (Admin-only — exposes phone/email). Lets a class teacher's restricted
/// Administrace view pick a parent to link without granting them the full
/// user list. Not for parents and pupils: who the other parents are is the
/// school's call ("Viditelnost údajů", see <see cref="People.ClassDirectoryController"/>).</summary>
[Authorize(Roles = "Teacher,Admin")]
[ApiController]
[Route("api/parents")]
public class ParentsController(AppDbContext db, PhotoAccess photoAccess) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentDto>>> GetParents()
    {
        var users = await db.Users
            .Include(u => u.Person)
            .Where(u => u.Roles.Contains(UserRole.Parent))
            .OrderBy(u => u.Username)
            .ToListAsync();
        var photos = await photoAccess.VisibleUserPhotosAsync(User, users);

        return Ok(users.Select(u => new ParentDto(
            u.Id, u.Username, PersonNames.Display(u.Person, u.Username), photos.GetValueOrDefault(u.Id))));
    }
}

/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
public record ParentDto(int Id, string Username, string Name, string? Photo);
