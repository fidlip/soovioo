using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>"Viditelnost údajů" — what the school shares with parents
    /// and pupils, see <see cref="Visibility"/>.</summary>
    [HttpGet("visibility")]
    public async Task<ActionResult<VisibilitySettingsDto>> GetVisibility()
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        return Ok(ToVisibilityDto(school));
    }

    [HttpPut("visibility")]
    public async Task<ActionResult<VisibilitySettingsDto>> SetVisibility(VisibilitySettingsDto request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        var shared = Visibility.FromNames(request.Shared);
        var optOut = Visibility.FromNames(request.OptOutAllowed);
        if (shared is null || optOut is null)
        {
            return BadRequest("Neznámá volba viditelnosti.");
        }

        school.SharedData = shared.Value;
        // Opting out of something the school doesn't share is moot, and a
        // pupil's photo hangs on consent, not on an opt-out.
        school.OptOutAllowed = optOut.Value & shared.Value & Visibility.OptOutCapable;
        await db.SaveChangesAsync();

        return Ok(ToVisibilityDto(school));
    }

    private static VisibilitySettingsDto ToVisibilityDto(School school) =>
        new(Visibility.ToNames(school.SharedData), Visibility.ToNames(school.OptOutAllowed));
}

public record SetSchoolBankAccountRequest(string? BankAccountNumber);

/// <param name="Shared"><see cref="VisibilityOption"/> names the school shares.</param>
/// <param name="OptOutAllowed">Those of them individuals may hide for themselves.</param>
public record VisibilitySettingsDto(IReadOnlyList<string> Shared, IReadOnlyList<string> OptOutAllowed);
