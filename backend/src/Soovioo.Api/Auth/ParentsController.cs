using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Auth;

/// <summary>Minimal, name-only Parent list for pickers (e.g. the parent↔student
/// link form) that any authenticated school member needs — same pattern as
/// <see cref="TeachersController"/>, unlike the full <see cref="UsersController"/>
/// (Admin-only — exposes phone/email). Lets a class teacher's restricted
/// Administrace view pick a parent to link without granting them the full
/// user list.</summary>
[Authorize]
[ApiController]
[Route("api/parents")]
public class ParentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentDto>>> GetParents()
    {
        var parents = await db.Users
            .Where(u => u.Roles.Contains(UserRole.Parent))
            .OrderBy(u => u.Username)
            .Select(u => new ParentDto(u.Id, u.Username))
            .ToListAsync();

        return Ok(parents);
    }
}

public record ParentDto(int Id, string Username);
