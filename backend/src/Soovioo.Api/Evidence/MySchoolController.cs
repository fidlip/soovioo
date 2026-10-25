using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Evidence;

/// <summary>Self-service settings for the Admin's own school — currently just
/// the IZO/RED_IZO the matrika export needs. Separate from the SuperAdmin-only
/// <see cref="SchoolsController"/> so an Admin can fill these in without
/// needing SuperAdmin involvement.</summary>
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/my-school")]
public class MySchoolController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchoolDto>> Get()
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    [HttpPut("matrika-ids")]
    public async Task<ActionResult<SchoolDto>> SetMatrikaIds(SetSchoolMatrikaIdsRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        school.Izo = request.Izo;
        school.RedIzo = request.RedIzo;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>ZŠ/SŠ — switches "žák"/"student" across the UI. Takes effect
    /// for each user with their next access token (≤ 15 min).</summary>
    [HttpPut("type")]
    public async Task<ActionResult<SchoolDto>> SetType(SetSchoolTypeRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        school.Type = request.Type;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>Institutional address/contact/director — used on real
    /// documents, not transmitted to matrika itself.</summary>
    [HttpPut("details")]
    public async Task<ActionResult<SchoolDto>> SetDetails(SetSchoolDetailsRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        school.Street = request.Street;
        school.City = request.City;
        school.PostalCode = request.PostalCode;
        school.DirectorName = request.DirectorName;
        school.Email = request.Email;
        school.Phone = request.Phone;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>The account payments (Payments) get paid into — used to
    /// build the client-generated SPAYD QR string. Without it set, the
    /// frontend just skips rendering a QR code.</summary>
    [HttpPut("bank-account")]
    public async Task<ActionResult<SchoolDto>> SetBankAccount(SetSchoolBankAccountRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.BankAccountNumber))
        {
            school.BankAccountNumber = null;
        }
        else
        {
            var (iban, error) = BankAccounts.ToIban(request.BankAccountNumber);
            if (error is not null)
            {
                return this.FieldError("bankAccountNumber", error);
            }
            school.BankAccountNumber = iban;
        }
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>"Viditelnost údajů" — for every <see cref="Visibility.Keys"/>
    /// entry the range individuals choose from and the default, and whether
    /// pupils' and parents' photos are on at all.</summary>
    [HttpGet("visibility")]
    public async Task<ActionResult<VisibilityRulesDto>> GetVisibility()
    {
        var schoolId = currentSchool.SchoolId!.Value;
        return Ok(ToDto(await Visibility.LoadAsync(db, schoolId), await db.Schools.FirstAsync(s => s.Id == schoolId)));
    }

    /// <summary>Replaces the whole set; entries left out mean
    /// <see cref="VisibilityScope.Nobody"/>.</summary>
    [HttpPut("visibility")]
    public async Task<ActionResult<VisibilityRulesDto>> SetVisibility(VisibilityRulesDto request)
    {
        var schoolId = currentSchool.SchoolId!.Value;
        var keys = request.Rules.Select(r => new VisibilityKey(r.Owner, r.Field, r.Audience)).ToList();
        if (keys.Any(k => !Visibility.Keys.Contains(k)) || keys.Distinct().Count() != keys.Count
            || request.Rules.Any(r => !Enum.IsDefined(r.Min) || !Enum.IsDefined(r.Default) || !Enum.IsDefined(r.Max)))
        {
            return BadRequest("Neznámá volba viditelnosti.");
        }
        if (request.Rules.Any(r => r.Min > r.Default || r.Default > r.Max))
        {
            return BadRequest("Výchozí viditelnost musí být mezi nejmenší a největší.");
        }

        var school = await db.Schools.FirstAsync(s => s.Id == schoolId);
        school.StudentPhotos = request.StudentPhotos;
        school.ParentPhotos = request.ParentPhotos;
        var existing = await db.VisibilityRules.Where(r => r.SchoolId == schoolId).ToListAsync();
        db.VisibilityRules.RemoveRange(existing);
        db.VisibilityRules.AddRange(request.Rules
            .Where(r => r.Max != VisibilityScope.Nobody)
            .Select(r => new VisibilityRule
            {
                SchoolId = schoolId,
                Owner = r.Owner,
                Field = r.Field,
                Audience = r.Audience,
                Min = r.Min,
                Default = r.Default,
                Max = r.Max,
            }));
        await db.SaveChangesAsync();

        return Ok(ToDto(await Visibility.LoadAsync(db, schoolId), school));
    }

    /// <summary>The daily e-mail of notifications — when it goes out for
    /// people who didn't pick their own time, and the kinds nobody can
    /// switch off; push — whether it may carry the whole text, and the
    /// night quiet.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<SchoolNotificationSettingsDto>> GetNotifications()
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        return Ok(NotificationsOf(school));
    }

    [HttpPut("notifications")]
    public async Task<ActionResult<SchoolNotificationSettingsDto>> SetNotifications(SchoolNotificationSettingsDto request)
    {
        if (request.ForcedEmail.Any(t => !Enum.IsDefined(t)))
        {
            return BadRequest("Neznámý druh upozornění.");
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.DigestTime = new TimeOnly(request.DigestTime.Hour, request.DigestTime.Minute);
        school.ForcedEmailNotifications = request.ForcedEmail.Distinct().Order().ToList();
        school.PushFullText = request.PushFullText;
        school.PushQuietFrom = new TimeOnly(request.PushQuietFrom.Hour, request.PushQuietFrom.Minute);
        school.PushQuietTo = new TimeOnly(request.PushQuietTo.Hour, request.PushQuietTo.Minute);
        await db.SaveChangesAsync();
        return Ok(NotificationsOf(school));
    }

    private static SchoolNotificationSettingsDto NotificationsOf(School school) =>
        new(school.DigestTime, school.ForcedEmailNotifications, school.PushFullText, school.PushQuietFrom, school.PushQuietTo);

    private static VisibilityRulesDto ToDto(SchoolVisibility visibility, School school) =>
        new(Visibility.Keys
            .Select(visibility.Rule)
            .Select(r => new VisibilityRuleDto(r.Owner, r.Field, r.Audience, r.Min, r.Default, r.Max))
            .ToList(),
            school.StudentPhotos, school.ParentPhotos);
}

public record SetSchoolBankAccountRequest(string? BankAccountNumber);

/// <param name="DigestTime">See <see cref="School.DigestTime"/>.</param>
/// <param name="ForcedEmail">See <see cref="School.ForcedEmailNotifications"/>.</param>
/// <param name="PushFullText">See <see cref="School.PushFullText"/>.</param>
/// <param name="PushQuietFrom">See <see cref="School.PushQuietFrom"/>.</param>
public record SchoolNotificationSettingsDto(
    TimeOnly DigestTime, IReadOnlyList<Notifications.NotificationType> ForcedEmail,
    bool PushFullText, TimeOnly PushQuietFrom, TimeOnly PushQuietTo);

/// <param name="Rules">One per <see cref="Visibility.Keys"/> entry.</param>
/// <param name="StudentPhotos">See <see cref="School.StudentPhotos"/>.</param>
/// <param name="ParentPhotos">See <see cref="School.ParentPhotos"/>.</param>
public record VisibilityRulesDto(IReadOnlyList<VisibilityRuleDto> Rules, bool StudentPhotos = false, bool ParentPhotos = false);

public record VisibilityRuleDto(
    VisibilityOwner Owner, VisibilityField Field, VisibilityAudience Audience,
    VisibilityScope Min, VisibilityScope Default, VisibilityScope Max);
