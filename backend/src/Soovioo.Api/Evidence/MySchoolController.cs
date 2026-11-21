using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.People;
using Soovioo.Api.Timetable.Rvp;

namespace Soovioo.Api.Evidence;

/// <summary>Self-service settings for the Admin's own school — currently just
/// the IZO/RED_IZO the matrika export needs. Separate from the SuperAdmin-only
/// <see cref="SchoolsController"/> so an Admin can fill these in without
/// needing SuperAdmin involvement.</summary>
[Authorize]
[RequirePermission(Permission.Administration)]
[ApiController]
[Route("api/my-school")]
[AuditArea("Údaje o škole")]
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
    [AuditAction("Nastaveny identifikátory školy (IZO, RED_IZO)")]
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
    [AuditAction("Změněn typ školy")]
    public async Task<ActionResult<SchoolDto>> SetType(SetSchoolTypeRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        await ApplyTypeAsync(school, request.Type);
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    private async Task ApplyTypeAsync(School school, SchoolType type)
    {
        // The pupils' role follows "žák"/"student" unless the school renamed it.
        var pupils = await db.Roles.FirstOrDefaultAsync(r => r.BuiltIn == UserRole.Student);
        if (pupils is not null && pupils.Name == PermissionCatalog.DefaultName(UserRole.Student, school.Type)
            && !await db.Roles.AnyAsync(r => r.Name == PermissionCatalog.DefaultName(UserRole.Student, type)))
        {
            pupils.Name = PermissionCatalog.DefaultName(UserRole.Student, type);
        }
        school.Type = type;
        // A program of the other type isn't on offer any more.
        foreach (var schoolClass in await db.SchoolClasses.Where(c => c.RvpProgramCode != null).ToListAsync())
        {
            if (RvpCatalog.Find(schoolClass.RvpProgramCode)?.SchoolType != type)
            {
                schoolClass.RvpProgramCode = null;
            }
        }
    }

    /// <summary>"Nastavení školy", the wizard a new school's admin goes
    /// through first: everything at once, then the school counts as set
    /// up.</summary>
    [HttpPut("setup")]
    [AuditAction("Dokončeno nastavení školy")]
    public async Task<ActionResult<SchoolDto>> CompleteSetup(SchoolSetupRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (NameError(name) is { } nameError)
        {
            return nameError;
        }
        if (!Enum.IsDefined(request.Type))
        {
            return this.FieldError("type", "Neznámý typ školy.");
        }
        if (!Enum.IsDefined(request.ReportCardMarkFormat))
        {
            return this.FieldError("reportCardMarkFormat", "Neznámý způsob klasifikace.");
        }
        if (request.District is { } district && !SchoolYears.Districts.IsKnown(district))
        {
            return this.FieldError("district", "Takový okres neznáme.");
        }
        if (!ContactFormats.TryEmail(request.Email, out var email))
        {
            return this.FieldError("email", ContactFormats.InvalidEmail);
        }
        if (!ContactFormats.TryPhone(request.Phone, out var phone))
        {
            return this.FieldError("phone", ContactFormats.InvalidPhone);
        }
        string? iban = null;
        if (!string.IsNullOrWhiteSpace(request.BankAccountNumber))
        {
            (iban, var error) = BankAccounts.ToIban(request.BankAccountNumber);
            if (error is not null)
            {
                return this.FieldError("bankAccountNumber", error);
            }
        }

        await ApplyTypeAsync(school, request.Type);
        school.Name = name;
        school.ReportCardMarkFormat = request.ReportCardMarkFormat;
        school.Street = Trimmed(request.Street);
        school.City = Trimmed(request.City);
        school.PostalCode = Trimmed(request.PostalCode);
        school.District = request.District;
        school.DirectorName = Trimmed(request.DirectorName);
        school.Email = email;
        school.Phone = phone;
        school.Izo = Trimmed(request.Izo);
        school.RedIzo = Trimmed(request.RedIzo);
        school.BankAccountNumber = iban;
        school.SetupCompletedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    private const int MaxNameLength = 200;

    private ActionResult? NameError(string? name) =>
        string.IsNullOrEmpty(name) ? this.FieldError("name", "Vyplňte název školy.")
        : name.Length > MaxNameLength ? this.FieldError("name", $"Název školy může mít nejvýše {MaxNameLength} znaků.")
        : null;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Institutional address/contact/director — used on real
    /// documents, not transmitted to matrika itself.</summary>
    [HttpPut("details")]
    [AuditAction("Upraveny údaje o škole")]
    public async Task<ActionResult<SchoolDto>> SetDetails(SetSchoolDetailsRequest request)
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        if (school is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (NameError(name) is { } nameError)
        {
            return nameError;
        }
        if (request.District is { } district && !SchoolYears.Districts.IsKnown(district))
        {
            return this.FieldError("district", "Takový okres neznáme.");
        }
        if (!ContactFormats.TryEmail(request.Email, out var email))
        {
            return this.FieldError("email", ContactFormats.InvalidEmail);
        }
        if (!ContactFormats.TryPhone(request.Phone, out var phone))
        {
            return this.FieldError("phone", ContactFormats.InvalidPhone);
        }

        school.Name = name!;
        school.Street = request.Street;
        school.District = request.District;
        school.City = request.City;
        school.PostalCode = request.PostalCode;
        school.DirectorName = request.DirectorName;
        school.Email = email;
        school.Phone = phone;
        await db.SaveChangesAsync();

        return Ok(await SchoolsController.ToDtoAsync(db, school));
    }

    /// <summary>The account payments (Payments) get paid into — used to
    /// build the client-generated SPAYD QR string. Without it set, the
    /// frontend just skips rendering a QR code.</summary>
    [HttpPut("bank-account")]
    [AuditAction("Nastaven bankovní účet školy")]
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
    [AuditAction("Změněna viditelnost údajů")]
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
        if (request.Rules.Any(r => r.Max > Visibility.Widest(new VisibilityKey(r.Owner, r.Field, r.Audience))))
        {
            return BadRequest("Přezdívku rodiče mohou vidět jen jeho děti.");
        }

        var school = await db.Schools.FirstAsync(s => s.Id == schoolId);
        school.StudentPhotos = request.StudentPhotos;
        school.ParentPhotos = request.ParentPhotos;
        school.StaffSeeStudentNicknames = request.StaffSeeStudentNicknames;
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
    [AuditAction("Změněno nastavení upozornění pro celou školu")]
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

    [HttpGet("report-cards")]
    public async Task<ActionResult<ReportCardSettingsDto>> GetReportCards()
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        return Ok(new ReportCardSettingsDto(school.ReportCardMarkFormat));
    }

    [HttpPut("report-cards")]
    [AuditAction("Změněno nastavení vysvědčení")]
    public async Task<ActionResult<ReportCardSettingsDto>> SetReportCards(ReportCardSettingsDto request)
    {
        if (!Enum.IsDefined(request.MarkFormat))
        {
            return BadRequest("Neznámý způsob klasifikace.");
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.ReportCardMarkFormat = request.MarkFormat;
        await db.SaveChangesAsync();
        return Ok(new ReportCardSettingsDto(school.ReportCardMarkFormat));
    }

    [HttpGet("class-book")]
    public async Task<ActionResult<ClassBookSettingsDto>> GetClassBook()
    {
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        return Ok(new ClassBookSettingsDto(school.UnverifiedExcuseApproval));
    }

    [HttpPut("class-book")]
    [AuditAction("Změněno nastavení třídní knihy")]
    public async Task<ActionResult<ClassBookSettingsDto>> SetClassBook(ClassBookSettingsDto request)
    {
        if (!Enum.IsDefined(request.UnverifiedExcuseApproval))
        {
            return BadRequest("Neznámá volba.");
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.UnverifiedExcuseApproval = request.UnverifiedExcuseApproval;
        await db.SaveChangesAsync();
        return Ok(new ClassBookSettingsDto(school.UnverifiedExcuseApproval));
    }

    /// <summary>How ročníky, classes and single pupils are assessed on the
    /// report card otherwise than the school's default.</summary>
    [HttpGet("report-cards/assessment-rules")]
    public async Task<ActionResult<List<AssessmentModeRuleDto>>> GetAssessmentRules()
    {
        var rules = await db.AssessmentModeRules
            .Select(r => new AssessmentModeRuleDto(
                r.Id,
                r.Rocnik,
                r.SchoolClassId,
                r.SchoolClass == null ? null : r.SchoolClass.Name,
                r.StudentId,
                r.Student == null ? null : r.Student.Person!.FirstName + " " + r.Student.Person.LastName,
                r.SubjectId,
                r.Subject == null ? null : r.Subject.Name,
                r.Behavior,
                r.Mode,
                r.MarkFormat))
            .ToListAsync();
        var czech = StringComparer.Create(SchoolFormat.Culture, true);
        return Ok(rules
            .OrderBy(r => r.StudentId is not null ? 2 : r.SchoolClassId is not null ? 1 : 0)
            .ThenBy(r => r.Rocnik)
            .ThenBy(r => r.SchoolClassName ?? "", czech)
            .ThenBy(r => r.StudentName ?? "", czech)
            .ThenBy(r => r.Behavior ? 2 : r.SubjectId is null ? 0 : 1)
            .ThenBy(r => r.SubjectName ?? "", czech)
            .ToList());
    }

    /// <summary>Adds the rule, or changes the one for the same ročník, class
    /// or pupil and the same subjects.</summary>
    [AuditAction("Nastavena výjimka ze způsobu hodnocení")]
    [HttpPut("report-cards/assessment-rules")]
    public async Task<IActionResult> SetAssessmentRule(SetAssessmentModeRuleRequest request)
    {
        if (new object?[] { request.Rocnik, request.SchoolClassId, request.StudentId }.Count(x => x is not null) != 1)
        {
            return BadRequest("Vyberte ročník, třídu, nebo žáka.");
        }
        if (request.Rocnik is not (null or (>= 1 and <= 13)))
        {
            return BadRequest("Neplatný ročník.");
        }
        if (request.Behavior && request.SubjectId is not null)
        {
            return BadRequest("Chování nemá předmět.");
        }
        if (!Enum.IsDefined(request.Mode))
        {
            return BadRequest("Neznámý způsob hodnocení.");
        }
        if (request.MarkFormat is { } format && !Enum.IsDefined(format))
        {
            return BadRequest("Neznámý způsob klasifikace.");
        }
        if (request.MarkFormat is not null && request.Mode == Grades.AssessmentMode.Verbal)
        {
            return BadRequest("Slovní hodnocení se nezapisuje číslem ani slovem.");
        }
        if (request.SchoolClassId is { } classId && !await db.SchoolClasses.AnyAsync(c => c.Id == classId))
        {
            return BadRequest("Neznámá třída.");
        }
        if (request.StudentId is { } studentId && !await db.Students.AnyAsync(s => s.Id == studentId))
        {
            return BadRequest("Neznámý žák.");
        }
        if (request.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId))
        {
            return BadRequest("Neznámý předmět.");
        }

        var rule = await db.AssessmentModeRules.FirstOrDefaultAsync(r =>
            r.Rocnik == request.Rocnik && r.SchoolClassId == request.SchoolClassId && r.StudentId == request.StudentId
            && r.SubjectId == request.SubjectId && r.Behavior == request.Behavior);
        if (rule is null)
        {
            rule = new Grades.AssessmentModeRule
            {
                SchoolId = currentSchool.SchoolId!.Value,
                Rocnik = request.Rocnik,
                SchoolClassId = request.SchoolClassId,
                StudentId = request.StudentId,
                SubjectId = request.SubjectId,
                Behavior = request.Behavior,
            };
            db.AssessmentModeRules.Add(rule);
        }
        rule.Mode = request.Mode;
        rule.MarkFormat = request.MarkFormat;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [AuditAction("Smazána výjimka ze způsobu hodnocení")]
    [HttpDelete("report-cards/assessment-rules/{id}")]
    public async Task<IActionResult> DeleteAssessmentRule(int id)
    {
        var rule = await db.AssessmentModeRules.FindAsync(id);
        if (rule is null)
        {
            return NotFound();
        }
        db.AssessmentModeRules.Remove(rule);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static SchoolNotificationSettingsDto NotificationsOf(School school) =>
        new(school.DigestTime, school.ForcedEmailNotifications, school.PushFullText, school.PushQuietFrom, school.PushQuietTo);

    private static VisibilityRulesDto ToDto(SchoolVisibility visibility, School school) =>
        new(Visibility.Keys
            .Select(visibility.Rule)
            .Select(r => new VisibilityRuleDto(r.Owner, r.Field, r.Audience, r.Min, r.Default, r.Max))
            .ToList(),
            school.StudentPhotos, school.ParentPhotos, school.StaffSeeStudentNicknames);
}

public record SetSchoolBankAccountRequest(string? BankAccountNumber);

/// <param name="District">One of <see cref="SchoolYears.Districts.All"/>.</param>
/// <param name="BankAccountNumber">IBAN or a Czech account number, see <see cref="SetSchoolBankAccountRequest"/>.</param>
public record SchoolSetupRequest(
    string Name,
    SchoolType Type,
    Grades.ReportCardMarkFormat ReportCardMarkFormat,
    string? Street,
    string? City,
    string? PostalCode,
    string? District,
    string? DirectorName,
    string? Email,
    string? Phone,
    string? Izo,
    string? RedIzo,
    string? BankAccountNumber);

/// <param name="MarkFormat">See <see cref="School.ReportCardMarkFormat"/>.</param>
public record ReportCardSettingsDto(Grades.ReportCardMarkFormat MarkFormat);

/// <param name="UnverifiedExcuseApproval">See <see cref="School.UnverifiedExcuseApproval"/>.</param>
public record ClassBookSettingsDto(ClassBook.UnverifiedExcuseApproval UnverifiedExcuseApproval);

/// <param name="SubjectId">Null with <paramref name="Behavior"/> false = all
/// subjects.</param>
public record AssessmentModeRuleDto(
    int Id,
    int? Rocnik,
    int? SchoolClassId,
    string? SchoolClassName,
    int? StudentId,
    string? StudentName,
    int? SubjectId,
    string? SubjectName,
    bool Behavior,
    Grades.AssessmentMode Mode,
    Grades.ReportCardMarkFormat? MarkFormat);

/// <param name="Rocnik">Exactly one of it, <paramref name="SchoolClassId"/>
/// and <paramref name="StudentId"/>.</param>
/// <param name="MarkFormat">See <see cref="Grades.AssessmentModeRule.MarkFormat"/>.</param>
public record SetAssessmentModeRuleRequest(
    int? Rocnik, int? SchoolClassId, int? StudentId, int? SubjectId, bool Behavior, Grades.AssessmentMode Mode,
    Grades.ReportCardMarkFormat? MarkFormat = null);

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
/// <param name="StaffSeeStudentNicknames">See <see cref="School.StaffSeeStudentNicknames"/>.</param>
public record VisibilityRulesDto(
    IReadOnlyList<VisibilityRuleDto> Rules, bool StudentPhotos = false, bool ParentPhotos = false,
    bool StaffSeeStudentNicknames = true);

public record VisibilityRuleDto(
    VisibilityOwner Owner, VisibilityField Field, VisibilityAudience Audience,
    VisibilityScope Min, VisibilityScope Default, VisibilityScope Max);
