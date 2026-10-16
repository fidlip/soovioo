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
            .Select(s => new SchoolDto(
                s.Id, s.Name, s.Slug, s.CreatedAt, s.Izo, s.RedIzo,
                s.Street, s.City, s.PostalCode, s.DirectorName, s.Email, s.Phone))
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

        var dto = ToDto(school);
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

        return Ok(ToDto(school));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SchoolDto>> UpdateSchool(int id, UpdateSchoolRequest request)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null)
        {
            return NotFound();
        }

        if (await db.Schools.AnyAsync(s => s.Id != id && s.Slug == request.Slug))
        {
            return BadRequest("Slug already exists.");
        }

        school.Name = request.Name;
        school.Slug = request.Slug;
        await db.SaveChangesAsync();

        return Ok(ToDto(school));
    }

    internal static SchoolDto ToDto(School school) => new(
        school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo,
        school.Street, school.City, school.PostalCode, school.DirectorName, school.Email, school.Phone);

    /// <summary>Only allowed while the school is still empty (no users at
    /// all — every school gets its first Admin at creation time, so this is
    /// effectively "undo an accidental creation", never a real tenant with
    /// data). A real tenant's data must never disappear via a silent cascade
    /// delete — if that's ever needed, it needs its own deliberate,
    /// confirmed, non-generic flow.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSchool(int id)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null)
        {
            return NotFound();
        }

        if (await db.Users.AnyAsync(u => u.SchoolId == id))
        {
            return BadRequest("Škola má uživatele — nelze smazat.");
        }

        db.Schools.Remove(school);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateSchoolRequest(
    string Name, string Slug, string AdminUsername, string AdminPassword, string? Izo = null, string? RedIzo = null);

public record UpdateSchoolRequest(string Name, string Slug);

public record SetSchoolMatrikaIdsRequest(string? Izo, string? RedIzo);

public record SetSchoolDetailsRequest(
    string? Street, string? City, string? PostalCode, string? DirectorName, string? Email, string? Phone);

public record SchoolDto(
    int Id,
    string Name,
    string Slug,
    DateTimeOffset CreatedAt,
    string? Izo,
    string? RedIzo,
    string? Street,
    string? City,
    string? PostalCode,
    string? DirectorName,
    string? Email,
    string? Phone);
