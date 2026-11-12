using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>SuperAdmin support: entering a school as its admin (audited,
/// see <see cref="SupportSession"/>) and managing the SuperAdmin accounts.</summary>
[ApiController]
[AuditArea("Podpora")]
public class SupportController(AppDbContext db, SupportSessionService supportSessions, RefreshTokenService refreshTokens) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>The built-in SuperAdmin account (seeded at startup) — can't be
    /// deleted or renamed by anyone, so there's always a way back in.</summary>
    public const string BuiltInUsername = "superadmin";

    /// <summary>Starts an audited support session (2 h) and returns a
    /// one-time code — the SuperAdmin's browser carries it to the school's
    /// own page (its subdomain in production), which redeems it at
    /// <see cref="Redeem"/>. The SuperAdmin's own session stays untouched.</summary>
    [HttpPost("api/schools/{schoolId:int}/support-sessions")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Zahájen režim podpory")]
    public async Task<ActionResult<SupportSessionStartDto>> StartSupportSession(int schoolId)
    {
        var school = await db.Schools.FindAsync(schoolId);
        var me = await db.Users.FindAsync(CurrentUserId());
        if (school is null || me is null)
        {
            return NotFound();
        }

        var session = new SupportSession
        {
            SuperAdminUserId = me.Id,
            SuperAdminUsername = me.Username,
            SchoolId = school.Id,
        };
        var code = await supportSessions.StartAsync(session);
        // A SuperAdmin token has no school — the school's admin sees this too.
        var audit = AuditContext.Of(HttpContext);
        audit.SchoolId = school.Id;
        audit.SupportSessionId = session.Id;
        audit.SupportBy = me.Username;
        return Ok(new SupportSessionStartDto(session.Id, code, school.Slug, school.Name, session.ExpiresAt));
    }

    /// <summary>One-time code → support access token + support cookie.
    /// Anonymous: on a school's subdomain there's no session yet.</summary>
    [HttpPost("api/support-sessions/redeem")]
    [AllowAnonymous]
    [AuditAction("Vstup do školy v režimu podpory")]
    public async Task<ActionResult<LoginResponse>> Redeem(RedeemSupportCodeRequest request)
    {
        var response = await supportSessions.RedeemAsync(HttpContext, request.Code);
        return response is null
            ? BadRequest("Odkaz pro vstup do školy je neplatný nebo vypršel.")
            : Ok(response);
    }

    /// <summary>"Opustit" — called with the support token itself.</summary>
    [HttpPost("api/support-sessions/current/end")]
    [Authorize]
    [AuditAction("Ukončen režim podpory")]
    public async Task<IActionResult> EndCurrentSession()
    {
        if (!int.TryParse(User.FindFirstValue(SupportClaims.SessionId), out var sessionId))
        {
            return BadRequest("Nejste v režimu podpory.");
        }
        var session = await db.SupportSessions.FindAsync(sessionId);
        if (session is not null && session.EndedAt is null)
        {
            session.EndedAt = DateTimeOffset.UtcNow;
            session.RefreshSecretHash = null;
            await db.SaveChangesAsync();
        }
        supportSessions.ClearCookie(HttpContext);
        return NoContent();
    }

    /// <summary>Audit log for the SuperAdmin page — newest first.</summary>
    [HttpGet("api/support-sessions")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IEnumerable<SupportSessionDto>>> GetSupportSessions(int? schoolId = null)
    {
        var query = db.SupportSessions.AsQueryable();
        if (schoolId is not null)
        {
            query = query.Where(s => s.SchoolId == schoolId);
        }
        return Ok(await query
            .OrderByDescending(s => s.StartedAt)
            .Take(50)
            .Select(s => new SupportSessionDto(
                s.Id, s.SuperAdminUsername, s.SchoolId, s.School!.Name, s.StartedAt, s.EndedAt,
                db.AuditLogEntries
                    // Entering and leaving aren't changes in the school.
                    .Where(a => a.SupportSessionId == s.Id && !a.Path.StartsWith("/api/support-sessions")
                                && !a.Path.EndsWith("/support-sessions"))
                    .OrderBy(a => a.At)
                    .Select(a => new SupportActionDto(a.Method, a.Path, a.StatusCode, a.At, a.Description))
                    .ToList()))
            .ToListAsync());
    }

    /// <summary>SuperAdmins are ordinary <see cref="User"/> rows (no school,
    /// role SuperAdmin) with a <see cref="People.Person"/> for name and
    /// contacts, like every other account.</summary>
    [HttpGet("api/superadmins")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IEnumerable<SuperAdminDto>>> GetSuperAdmins() =>
        Ok((await db.Users
                .Include(u => u.Person)
                .Where(u => u.IsSuperAdmin && u.SchoolId == null)
                .ToListAsync())
            .Select(ToDto)
            .OrderBy(d => d.Name));

    [HttpPost("api/superadmins")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Vytvořen superadmin")]
    public async Task<ActionResult<SuperAdminDto>> AddSuperAdmin(CreateSuperAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return this.FieldError("username", "Uživatelské jméno je povinné.");
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return this.FieldError("password", "Heslo musí mít alespoň 8 znaků.");
        }
        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.SchoolId == null && u.Username == username))
        {
            return this.FieldError("username", "Uživatelské jméno už existuje.");
        }

        var user = new User
        {
            Username = username,
            PasswordHash = "",
            IsSuperAdmin = true,
            SchoolId = null,
            // Picked by someone else — the new SuperAdmin sets their own.
            MustChangePassword = true,
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await ApplyPersonAsync(user, request.FirstName, request.LastName, request.Phone, request.Email);
        await db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    /// <summary>Name, contacts, username; password optional (a reset of
    /// someone else's forces a change on their next login and signs them out).</summary>
    [HttpPut("api/superadmins/{id:int}")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Upraven superadmin")]
    public async Task<ActionResult<SuperAdminDto>> UpdateSuperAdmin(int id, UpdateSuperAdminRequest request)
    {
        var user = await db.Users.Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == id && u.IsSuperAdmin && u.SchoolId == null);
        if (user is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return this.FieldError("username", "Uživatelské jméno je povinné.");
        }
        var username = request.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Id != id && u.SchoolId == null && u.Username == username))
        {
            return this.FieldError("username", "Uživatelské jméno už existuje.");
        }
        var passwordChanged = !string.IsNullOrWhiteSpace(request.Password);
        if (passwordChanged && request.Password!.Length < 8)
        {
            return this.FieldError("password", "Heslo musí mít alespoň 8 znaků.");
        }

        if (user.Username == BuiltInUsername && username != BuiltInUsername)
        {
            return this.FieldError("username", "Vestavěný účet superadmin nelze přejmenovat.");
        }

        user.Username = username;
        await ApplyPersonAsync(user, request.FirstName, request.LastName, request.Phone, request.Email);
        var isMe = id == CurrentUserId();
        if (passwordChanged)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password!);
            user.MustChangePassword = !isMe;
        }
        await db.SaveChangesAsync();
        if (passwordChanged && !isMe)
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id);
        }
        return Ok(ToDto(user));
    }

    /// <summary>Never yourself, never the last one.</summary>
    [HttpDelete("api/superadmins/{id:int}")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Smazán superadmin")]
    public async Task<IActionResult> DeleteSuperAdmin(int id)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsSuperAdmin && u.SchoolId == null);
        if (user is null)
        {
            return NotFound();
        }
        if (id == CurrentUserId())
        {
            return BadRequest("Nelze smazat vlastní účet.");
        }
        if (user.Username == BuiltInUsername)
        {
            return BadRequest("Vestavěný účet superadmin nelze smazat.");
        }
        if (await db.Users.CountAsync(u => u.IsSuperAdmin && u.SchoolId == null) <= 1)
        {
            return BadRequest("Poslední superadmin — nejdřív přidejte jiného.");
        }

        db.Persons.RemoveRange(db.Persons.Where(p => p.UserId == id));
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task ApplyPersonAsync(User user, string? firstName, string? lastName, string? phone, string? email)
    {
        var person = await db.EnsurePersonAsync(user);
        person.FirstName = firstName?.Trim() ?? "";
        person.LastName = string.IsNullOrWhiteSpace(lastName) ? user.Username : lastName.Trim();
        person.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        person.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        user.Person = person;
    }

    private static SuperAdminDto ToDto(User u) => new(
        u.Id, u.Username, u.Person?.FirstName ?? "", u.Person?.LastName ?? u.Username,
        PersonNames.Display(u), u.Person?.Phone, u.Person?.Email, u.Username == BuiltInUsername);

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <param name="Code">One-time, valid 1 minute — redeem at
/// <c>POST /api/support-sessions/redeem</c> from the school's page.</param>
public record SupportSessionStartDto(int SessionId, string Code, string SchoolSlug, string SchoolName, DateTimeOffset ExpiresAt);

public record RedeemSupportCodeRequest(string Code);

public record SupportActionDto(string Method, string Path, int StatusCode, DateTimeOffset At, string? Description);

public record SupportSessionDto(
    int Id, string SuperAdminUsername, int SchoolId, string SchoolName, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, IReadOnlyList<SupportActionDto> Actions);

/// <param name="BuiltIn">The seeded "superadmin" account — not deletable or renamable.</param>
public record SuperAdminDto(
    int Id, string Username, string FirstName, string LastName, string Name, string? Phone, string? Email,
    bool BuiltIn);

public record CreateSuperAdminRequest(
    string Username, string Password, string? FirstName = null, string? LastName = null,
    string? Phone = null, string? Email = null);

/// <param name="Password">Empty = keep the current one.</param>
public record UpdateSuperAdminRequest(
    string Username, string? Password = null, string? FirstName = null, string? LastName = null,
    string? Phone = null, string? Email = null);
