using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

/// <summary>Tenant management. SuperAdmin-only — this is the one controller
/// that spans schools on purpose.</summary>
[Authorize(Roles = "SuperAdmin")]
[ApiController]
[Route("api/schools")]
[AuditArea("Školy")]
public class SchoolsController(AppDbContext db) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    private const string ReservedSlugMessage = "Adresy „demo“ a „demo-…“ jsou vyhrazené pro ukázkové školy.";

    /// <summary>Paginated — a real multi-tenant install can accumulate far
    /// more schools than fit comfortably in one unpaginated table.
    /// <paramref name="search"/> matches name or slug (case-insensitive,
    /// substring) — filtered server-side, same as pagination, so the two
    /// combine correctly instead of a client-side filter only ever seeing
    /// whatever happened to be on the current page.</summary>
    [HttpGet]
    /// <param name="demos">"Vyzkoušet" demo schools instead of the real
    /// ones — kept apart so they never clutter the list or its counts.</param>
    public async Task<ActionResult<PagedResult<SchoolDto>>> GetSchools(
        int page = 1, int pageSize = 20, string? search = null, bool demos = false)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Schools.Where(s => s.IsDemo == demos);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                TextSearch.Fold(s.Name).Contains(TextSearch.Fold(search))
                || TextSearch.Fold(s.Slug).Contains(TextSearch.Fold(search)));
        }

        var totalCount = await query.CountAsync();

        var schools = await (demos ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.Name))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SchoolDto(
                s.Id, s.Name, s.Slug, s.CreatedAt, s.Izo, s.RedIzo,
                s.Street, s.City, s.PostalCode, s.DirectorName, s.Email, s.Phone, s.BankAccountNumber, s.Type,
                db.Students.Count(st => st.SchoolId == s.Id),
                db.Users.Count(u => u.SchoolId == s.Id),
                s.IsDemo,
                s.ExpiresAt))
            .ToListAsync();

        return Ok(new PagedResult<SchoolDto>(schools, totalCount, page, pageSize));
    }

    /// <summary>Creates a school and its first Admin user in one step, so
    /// there's immediately someone who can log in and run it. No infra
    /// provisioning — new tenant is just a row.</summary>
    [HttpPost]
    [AuditAction("Založena škola")]
    public async Task<ActionResult<SchoolDto>> AddSchool(CreateSchoolRequest request)
    {
        if (Demo.DemoSlugs.IsReserved(request.Slug))
        {
            return this.FieldError("slug", ReservedSlugMessage);
        }
        if (await db.Schools.AnyAsync(s => s.Slug == request.Slug))
        {
            return this.FieldError("slug", "Tato adresa školy (slug) už je obsazená.");
        }

        var school = new School
        {
            Name = request.Name,
            Slug = request.Slug,
            Izo = request.Izo,
            RedIzo = request.RedIzo,
        };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        var admin = new User
        {
            Username = request.AdminUsername,
            PasswordHash = string.Empty,
            ActiveRole = school.Roles.Single(r => r.BuiltIn == UserRole.Admin),
            SchoolId = school.Id,
        };
        admin.PasswordHash = Hasher.HashPassword(admin, request.AdminPassword);
        db.Users.Add(admin);
        db.Persons.Add(new People.Person { User = admin, LastName = admin.Username, SchoolId = school.Id });
        await db.SaveChangesAsync();

        var dto = await ToDtoAsync(db, school);
        return CreatedAtAction(nameof(GetSchools), new { id = school.Id }, dto);
    }

    /// <summary>Sets a school's IZO/RED_IZO — needed to back-fill them onto a
    /// school created before matrika support existed (e.g. the seeded demo
    /// school had to migrate this way).</summary>
    [HttpPut("{id:int}/matrika-ids")]
    [AuditAction("Nastaveny identifikátory školy (IZO, RED_IZO)")]
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

        return Ok(await ToDtoAsync(db, school));
    }

    [HttpPut("{id:int}")]
    [AuditAction("Upravena škola")]
    public async Task<ActionResult<SchoolDto>> UpdateSchool(int id, UpdateSchoolRequest request)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null)
        {
            return NotFound();
        }

        // A demo keeps its generated slug; a real school can't take one.
        if (request.Slug != school.Slug && Demo.DemoSlugs.IsReserved(request.Slug))
        {
            return this.FieldError("slug", ReservedSlugMessage);
        }

        if (await db.Schools.AnyAsync(s => s.Id != id && s.Slug == request.Slug))
        {
            return this.FieldError("slug", "Tato adresa školy (slug) už je obsazená.");
        }

        school.Name = request.Name;
        school.Slug = request.Slug;
        school.Izo = string.IsNullOrWhiteSpace(request.Izo) ? null : request.Izo.Trim();
        school.RedIzo = string.IsNullOrWhiteSpace(request.RedIzo) ? null : request.RedIzo.Trim();
        await db.SaveChangesAsync();

        return Ok(await ToDtoAsync(db, school));
    }

    internal static async Task<SchoolDto> ToDtoAsync(AppDbContext db, School school) => new(
        school.Id, school.Name, school.Slug, school.CreatedAt, school.Izo, school.RedIzo,
        school.Street, school.City, school.PostalCode, school.DirectorName, school.Email, school.Phone,
        school.BankAccountNumber,
        school.Type,
        await db.Students.CountAsync(s => s.SchoolId == school.Id),
        await db.Users.CountAsync(u => u.SchoolId == school.Id),
        school.IsDemo,
        school.ExpiresAt);

    /// <summary>Only allowed while the school is still empty (no users at
    /// all — every school gets its first Admin at creation time, so this is
    /// effectively "undo an accidental creation", never a real tenant with
    /// data). A real tenant's data must never disappear via a silent cascade
    /// delete — if that's ever needed, it needs its own deliberate,
    /// confirmed, non-generic flow.</summary>
    [HttpDelete("{id:int}")]
    [AuditAction("Smazána škola")]
    public async Task<IActionResult> DeleteSchool(int id, [FromServices] Demo.DemoSchoolPurger? demoPurger = null)
    {
        var school = await db.Schools.FindAsync(id);
        if (school is null)
        {
            return NotFound();
        }

        // A demo goes with everything in it — that's what demos are for.
        if (school.IsDemo && demoPurger is not null)
        {
            await demoPurger.PurgeAsync(id, deleteSchool: true);
            return NoContent();
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

/// <param name="Izo">Null/empty clears it (the dialog always sends the current value).</param>
public record UpdateSchoolRequest(string Name, string Slug, string? Izo = null, string? RedIzo = null);

public record SetSchoolMatrikaIdsRequest(string? Izo, string? RedIzo);

public record SetSchoolTypeRequest(SchoolType Type);

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
    string? Phone,
    string? BankAccountNumber,
    SchoolType Type,
    int StudentCount,
    int UserCount,
    bool IsDemo = false,
    DateTimeOffset? ExpiresAt = null);
