using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Minimal, name-only Teacher list for pickers (e.g. timetable entry
/// assignment) that any authenticated school member needs, unlike the full
/// <see cref="UsersController"/> (Admin-only — exposes phone/email).</summary>
[Authorize]
[ApiController]
[Route("api/teachers")]
public class TeachersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherDto>>> GetTeachers()
    {
        var teachers = await db.Users
            .Where(u => u.Roles.Contains(UserRole.Teacher))
            .OrderBy(u => u.Username)
            .Select(u => new TeacherDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
            .ToListAsync();

        return Ok(teachers);
    }
}

/// <param name="Name">Display name — what pickers show.</param>
public record TeacherDto(int Id, string Username, string Name);
