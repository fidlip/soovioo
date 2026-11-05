using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Minimal Parent list for staff pickers (e.g. the parent↔student
/// link form) — same pattern as <see cref="TeachersController"/>, unlike the
/// full <see cref="UsersController"/> (Admin-only — exposes phone/email to
/// everyone listed; here only a class teacher gets them, of their own class's
/// parents). Lets a class teacher's restricted
/// Administrace view pick a parent to link without granting them the full
/// user list. Not for parents and pupils: who the other parents are is the
/// school's call ("Viditelnost údajů", see <see cref="People.ClassDirectoryController"/>).</summary>
[Authorize(Roles = nameof(RoleKind.Staff))]
[ApiController]
[Route("api/parents")]
public class ParentsController(AppDbContext db, PhotoAccess photoAccess) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentDto>>> GetParents()
    {
        var users = await db.Users
            .Include(u => u.Person)
            .Where(u => u.Roles.Any(r => r.Kind == RoleKind.Parent))
            .OrderBy(u => u.Username)
            .ToListAsync();
        var photos = await photoAccess.VisibleUserPhotosAsync(User, users);
        var ownClassIds = await db.GetOwnClassIdsAsClassTeacherAsync(User.UserId());
        var ofOwnClass = (await db.ParentStudents
                .Where(ps => ownClassIds.Contains(ps.Student!.SchoolClassId))
                .Select(ps => ps.ParentProfile!.Person!.UserId)
                .ToListAsync())
            .ToHashSet();

        return Ok(users.Select(u =>
        {
            var contact = ofOwnClass.Contains(u.Id) ? u.Person : null;
            return new ParentDto(
                u.Id, u.Username, PersonNames.Display(u.Person, u.Username), photos.GetValueOrDefault(u.Id),
                contact?.Phone, contact?.Email);
        }));
    }
}

/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
/// <param name="Phone">Only for the class teacher of the parent's child.</param>
/// <param name="Email">Only for the class teacher of the parent's child.</param>
public record ParentDto(int Id, string Username, string Name, string? Photo, string? Phone = null, string? Email = null);
