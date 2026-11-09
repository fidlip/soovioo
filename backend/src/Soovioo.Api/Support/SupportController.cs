using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>SuperAdmin support: support sessions — redeeming the code,
/// leaving, the log (entering is for a ticket, see
/// <see cref="SupportTicketsController.Enter"/>) — and managing the
/// SuperAdmin accounts.</summary>
[ApiController]
public class SupportController(
    AppDbContext db, SupportSessionService supportSessions, RefreshTokenService refreshTokens, TimeProvider clock)
    : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>The built-in SuperAdmin account (seeded at startup) — can't be
    /// deleted or renamed by anyone, so there's always a way back in.</summary>
    public const string BuiltInUsername = "superadmin";

    /// <summary>One-time code → support access token + support cookie.
    /// Anonymous: on a school's subdomain there's no session yet.</summary>
    [HttpPost("api/support-sessions/redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Redeem(RedeemSupportCodeRequest request)
    {
        var response = await supportSessions.RedeemAsync(HttpContext, request.Code);
        return response is null
            ? BadRequest("Odkaz pro vstup do školy je neplatný nebo vypršel.")
            : Ok(response);
    }

    public const int MaxResultLength = SupportTicket.MaxTextLength;

    /// <summary>"Opustit" — called with the support token itself, optionally
    /// with what was done (Markdown), which goes to the ticket's thread.</summary>
    [HttpPost("api/support-sessions/current/end")]
    [Authorize]
    public async Task<IActionResult> EndCurrentSession(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EndSupportSessionRequest? request = null)
    {
        var result = request?.Result?.Trim();
        if (result?.Length > MaxResultLength)
        {
            return this.FieldError("result", $"Nejvýš {MaxResultLength} znaků.");
        }
        if (!int.TryParse(User.FindFirstValue(SupportClaims.SessionId), out var sessionId))
        {
            return BadRequest("Nejste v režimu podpory.");
        }
        var session = await db.SupportSessions.FindAsync(sessionId);
        if (session is not null && session.EndedAt is null)
        {
            var now = clock.GetUtcNow();
            session.EndedAt = now;
            session.RefreshSecretHash = null;
            session.Result = string.IsNullOrEmpty(result) ? null : result;
            if (session.TicketId is { } ticketId)
            {
                var author = await db.Users.IgnoreQueryFilters().Include(u => u.Person)
                    .FirstOrDefaultAsync(u => u.Id == session.SuperAdminUserId);
                db.SupportTicketEntries.Add(new SupportTicketEntry
                {
                    TicketId = ticketId,
                    SchoolId = session.SchoolId,
                    At = now,
                    Kind = SupportTicketEntryKind.Visit,
                    AuthorUserId = session.SuperAdminUserId,
                    AuthorName = author is null ? session.SuperAdminUsername : PersonNames.Display(author),
                    BySupport = true,
                    Text = session.Result,
                    SessionId = session.Id,
                });
            }
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
                s.Ticket != null ? s.Ticket.Title : null, s.Result,
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

/// <param name="Purpose">The title of the ticket it was entered for.</param>
/// <param name="Result">What was done, written on leaving.</param>
public record SupportSessionDto(
    int Id, string SuperAdminUsername, int SchoolId, string SchoolName, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string? Purpose, string? Result, IReadOnlyList<SupportActionDto> Actions);

public record EndSupportSessionRequest(string? Result);

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
