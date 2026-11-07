using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.ProductFeedback;

namespace Soovioo.Api.Demo;

/// <summary>The "Vyzkoušet" demo, anonymous: knowing a demo's slug (its
/// random id) is what entitles one to it — list its users, log in as any of
/// them, reset or delete it. Every endpoint answers 404 for a real school,
/// so none of this can ever list or log into a real school's users.</summary>
[AllowAnonymous]
[ApiController]
[Route("api/public/demo")]
public class DemoController(AppDbContext db, DemoSchoolService demos, DemoSchoolPurger purger, AuthSessionService sessions,
    FeedbackService feedback)
    : ControllerBase
{
    public const string CreateRateLimit = "demo-create";

    [HttpPost]
    [EnableRateLimiting(CreateRateLimit)]
    public async Task<ActionResult<DemoCreatedDto>> Create(DemoSetupRequest request)
    {
        if (DemoSchoolService.Validate(request) is { } field)
        {
            return this.FieldError(field, "Neplatná hodnota.");
        }
        var (demo, error) = await demos.CreateAsync(request);
        if (error == DemoCreateError.TooManyActive)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Právě běží příliš mnoho ukázkových škol. Zkuste to prosím později, nebo nám napište na info@soovioo.cz.");
        }
        // Logged as the demo's own entry (the request carries the visitor's
        // e-mail), so it goes when the demo goes.
        var audit = AuditContext.Of(HttpContext);
        audit.SchoolId = demo!.SchoolId;
        audit.Description = $"Založení ukázkové školy {demo.Slug}";
        return Ok(ToDto(demo));
    }

    [HttpGet("{slug}/users")]
    public async Task<ActionResult<IEnumerable<DemoUserDto>>> GetUsers(string slug)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }

        var users = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == school.Id)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.ActiveRole!.BuiltIn,
                u.ActiveRole.Kind,
                u.Person,
                ClassOf = db.SchoolClasses.IgnoreQueryFilters().Where(c => c.ClassTeacherId == u.Id).Select(c => c.Name).FirstOrDefault(),
                OwnClass = db.Students.IgnoreQueryFilters().Where(s => s.Person!.UserId == u.Id).Select(s => s.SchoolClass!.Name).FirstOrDefault(),
                Children = u.Person!.ParentProfile!.Children
                    .Select(c => c.Student!.Person!.FirstName + " (" + c.Student.SchoolClass!.Name + ")")
                    .ToList(),
            })
            .ToListAsync();
        var qualifications = (await db.TeacherProfiles.IgnoreQueryFilters()
                .Where(t => t.SchoolId == school.Id && t.Person!.UserId != null)
                .Include(t => t.Person)
                .Include(t => t.Qualifications).ThenInclude(q => q.Subject)
                .ToListAsync())
            .ToDictionary(t => t.Person!.UserId!.Value, t => t.Describe());

        return Ok(users
            .Select(u => new { User = u, Role = u.BuiltIn ?? u.Kind switch
            {
                RoleKind.Parent => UserRole.Parent,
                RoleKind.Student => UserRole.Student,
                _ => UserRole.Teacher,
            } })
            .Select(x => new DemoUserDto(
                x.User.Id,
                PersonNames.Display(x.User.Person, x.User.Username),
                x.Role.ToString(),
                x.Role switch
                {
                    UserRole.Admin => "vedení školy",
                    UserRole.Teacher => string.Join(" · ", new[] { x.User.ClassOf is null ? null : $"třídní {x.User.ClassOf}", qualifications.GetValueOrDefault(x.User.Id) }.Where(n => n is not null)),
                    UserRole.Parent => string.Join(", ", x.User.Children),
                    UserRole.Student => x.User.OwnClass ?? "",
                    _ => "",
                },
                school.DemoFeaturedUserIds?.Contains(x.User.Id) == true))
            .OrderBy(u => u.Role switch { "Admin" => 0, "Teacher" => 1, "Parent" => 2, _ => 3 })
            .ThenBy(u => u.Name, StringComparer.Create(new System.Globalization.CultureInfo("cs-CZ"), false)));
    }

    /// <summary>Passwordless — the demo's users have no passwords at all.</summary>
    [HttpPost("{slug}/login")]
    public async Task<ActionResult<LoginResponse>> Login(string slug, DemoLoginRequest request)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }
        var user = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.SchoolId == school.Id);
        if (user is null)
        {
            return NotFound();
        }

        var audit = AuditContext.Of(HttpContext);
        audit.SchoolId = school.Id;
        audit.UserId = user.Id;
        audit.Username = user.Username;
        audit.Action = "Přihlášení";
        audit.Description = "Přihlášení (demo)";
        return Ok(await sessions.StartAsync(HttpContext, user, persistent: true, request.UseCookie));
    }

    [HttpPost("{slug}/reset")]
    [EnableRateLimiting(CreateRateLimit)]
    public async Task<ActionResult<DemoCreatedDto>> Reset(string slug, DemoSetupRequest request)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }
        if (DemoSchoolService.Validate(request) is { } field)
        {
            return this.FieldError(field, "Neplatná hodnota.");
        }
        AuditContext.Of(HttpContext).SchoolId = school.Id;
        // Old sessions point at users that are about to disappear.
        sessions.ClearCookie(HttpContext, AuthSessionService.CookieNameFor(school.Id));
        var demo = await demos.ResetAsync(school, request);
        return Ok(ToDto(demo!));
    }

    /// <summary>"Vymazat data" — see <see cref="DemoSchoolService.ClearAsync"/>.
    /// The caller is then logged in as the remaining admin.</summary>
    [HttpPost("{slug}/clear")]
    public async Task<ActionResult<DemoClearedDto>> Clear(string slug)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }
        sessions.ClearCookie(HttpContext, AuthSessionService.CookieNameFor(school.Id));
        var adminId = await demos.ClearAsync(school);
        if (adminId is null)
        {
            return BadRequest("Ukázková škola nemá žádného administrátora — použijte Vygenerovat nová data.");
        }
        var audit = AuditContext.Of(HttpContext);
        audit.SchoolId = school.Id;
        audit.Action = "Vymazání dat";
        audit.Description = "Vymazání dat ukázkové školy";
        return Ok(new DemoClearedDto(adminId.Value));
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }
        sessions.ClearCookie(HttpContext, AuthSessionService.CookieNameFor(school.Id));
        await purger.PurgeAsync(school.Id, deleteSchool: true);
        return NoContent();
    }

    /// <summary>The demo's feedback dialog (see <see cref="FeedbackService"/>).</summary>
    [HttpPost("{slug}/feedback")]
    public async Task<IActionResult> Feedback(string slug, FeedbackRequest request)
    {
        if (await DemoSchoolAsync(slug) is not { } school)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = school.Id;
        if (await feedback.SaveAsync(school, null, request) is { } error)
        {
            return this.FieldError(error.Field, error.Message);
        }
        return NoContent();
    }

    private Task<School?> DemoSchoolAsync(string slug) =>
        db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Slug == slug && s.IsDemo);

    private static DemoCreatedDto ToDto(DemoCreated demo) => new(
        demo.Slug, demo.ExpiresAt,
        new Dictionary<string, int>
        {
            [nameof(UserRole.Admin)] = demo.Featured.AdminId,
            [nameof(UserRole.Teacher)] = demo.Featured.TeacherId,
            [nameof(UserRole.Parent)] = demo.Featured.ParentId,
            [nameof(UserRole.Student)] = demo.Featured.StudentId,
        });
}

/// <param name="FeaturedUserIds">Role → the user to log in as for it.</param>
public record DemoCreatedDto(string Slug, DateTimeOffset ExpiresAt, IReadOnlyDictionary<string, int> FeaturedUserIds);

/// <param name="Detail">Class, children or aprobace — whatever tells the
/// users apart in the picker.</param>
/// <param name="Featured">Offered first: the most to see as this one.</param>
public record DemoUserDto(int Id, string Name, string Role, string Detail, bool Featured);

/// <param name="AdminUserId">The account left in the emptied school.</param>
public record DemoClearedDto(int AdminUserId);

public record DemoLoginRequest(int UserId, bool UseCookie = false);
