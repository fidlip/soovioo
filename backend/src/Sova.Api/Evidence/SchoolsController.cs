using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

/// <summary>Tenant management. SuperAdmin-only — this is the one controller
/// that spans schools on purpose.</summary>
[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/schools")]
public class SchoolsController(AppDbContext db) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolDto>>> GetSchools()
    {
        var schools = await db.Schools
            .OrderBy(s => s.Name)
            .Select(s => new SchoolDto(s.Id, s.Name, s.Slug, s.CreatedAt, s.Izo, s.RedIzo))
            .ToListAsync();

        return Ok(schools);
    }

    /// <summary>Creates a school and its first Admin user in one step, so
    /// there's immediately someone who can log in and run it. No infra
    /// provisioning — new tenant is just a row.</summary>
    [HttpPost]
    public async Task<ActionResult<SchoolDto>> AddSchool(CreateSchoolRequest request)
    {
        if (await db.Schools.AnyAsync(s => s.Slug == request.Slug))
        {
            return BadRequest("Slug already exists.");
        }

        var school = new School
        {
            Name = request.Name,
            Slug = request.Slug,
            Izo = request.Izo,
            RedIzo = request.RedIzo,
        };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var admin = new User
        {
            Username = request.AdminUsername,
            PasswordHash = string.Empty,
            Role = UserRole.Admin,
            SchoolId = school.Id,
        };
        admin.PasswordHash = Hasher.HashPassword(admin, request.AdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var dto = new SchoolDto(school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo);
        return CreatedAtAction(nameof(GetSchools), new { id = school.Id }, dto);
    }

    /// <summary>Sets a school's IZO/RED_IZO — needed to back-fill them onto a
    /// school created before matrika support existed (e.g. the seeded demo
    /// school had to migrate this way).</summary>
    [HttpPut("{id:int}/matrika-ids")]
    public async Task<ActionResult<SchoolDto>> SetMatrikaIds(int id, SetSchoolMatrikaIdsRequest request)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null)
        {
            return NotFound();
        }

        school.Izo = request.Izo;
        school.RedIzo = request.RedIzo;
        await db.SaveChangesAsync();

        return Ok(new SchoolDto(school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo));
    }
}

public record CreateSchoolRequest(
    string Name, string Slug, string AdminUsername, string AdminPassword, string? Izo = null, string? RedIzo = null);

public record SetSchoolMatrikaIdsRequest(string? Izo, string? RedIzo);

public record SchoolDto(int Id, string Name, string Slug, DateTimeOffset CreatedAt, string? Izo, string? RedIzo);
