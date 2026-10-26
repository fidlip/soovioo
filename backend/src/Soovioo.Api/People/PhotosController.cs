using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.People;

/// <summary>Profile photos: people's (staff, parents) and pupils' (with a
/// guardian's consent and approval of what the pupil uploads). Who may see
/// which is <see cref="PhotoAccess"/>; the files themselves are
/// <see cref="PhotoStorage"/>. Photos are only displayed — no face
/// recognition or anything else biometric.</summary>
[Authorize]
[ApiController]
[Route("api/photos")]
public class PhotosController(
    AppDbContext db,
    ICurrentSchoolContext currentSchool,
    PhotoAccess access,
    PhotoStorage photos,
    IEmailSender emailSender,
    IOptions<AppLinkOptions> appLinkOptions,
    Notifier notifier,
    ILogger<PhotosController> logger) : ControllerBase
{
    /// <summary>The token is the photo's version (see
    /// <see cref="PhotoStorage.Token"/>) — a URL with a new one is a new
    /// photo, so the old one may stay cached.</summary>
    private const string CacheControl = "private, max-age=604800";

    // ---- People (staff, parents) ----

    [HttpGet("users/{userId:int}/{size}")]
    public async Task<IActionResult> GetUserPhoto(int userId, PhotoSize size)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.Person?.PhotoKey is null || await access.VisibleUserPhotoAsync(User, user) is null)
        {
            return NotFound();
        }
        return PhotoFile(user.Person.PhotoKey, size);
    }

    /// <summary>Own photo — staff any time, parents where the school has
    /// parents' photos on. An admin may also set a staff member's.</summary>
    [HttpPut("users/{userId:int}")]
    [RequestSizeLimit(PhotoStorage.MaxUploadBytes + 64 * 1024)]
    public async Task<ActionResult<PhotoDto>> UploadUserPhoto(int userId, IFormFile? file)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }
        if (await CanManageUserPhotoAsync(user) is { } refusal)
        {
            return refusal;
        }
        if (await ValidateUploadAsync(file) is { } invalid)
        {
            return invalid;
        }

        var key = await photos.SaveAsync(currentSchool.SchoolId!.Value, file!);
        if (key is null)
        {
            return BadRequest("Soubor se nepodařilo přečíst jako obrázek.");
        }
        var person = await db.EnsurePersonAsync(user);
        photos.Delete(person.PhotoKey);
        person.PhotoKey = key;
        await db.SaveChangesAsync();
        return Ok(new PhotoDto(PhotoStorage.Token(key)));
    }

    [HttpDelete("users/{userId:int}")]
    public async Task<IActionResult> DeleteUserPhoto(int userId)
    {
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return NotFound();
        }
        if (userId != PhotoAccess.ViewerId(User)
            && !(await User.HasPermissionAsync(db, Permission.Administration) && PhotoAccess.IsStaff(user)))
        {
            return Forbid();
        }
        if (user.Person?.PhotoKey is { } key)
        {
            photos.Delete(key);
            user.Person.PhotoKey = null;
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    // ---- Pupils ----

    [HttpGet("students/{studentId:int}/{size}")]
    public async Task<IActionResult> GetStudentPhoto(int studentId, PhotoSize size)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student?.Person!.PhotoKey is null || !await access.CanSeeStudentPhotoAsync(User, student))
        {
            return NotFound();
        }
        return PhotoFile(student.Person!.PhotoKey, size);
    }

    /// <summary>The pupils whose photo the caller decides about — a parent's
    /// children, a pupil themselves — with what they may do.</summary>
    [HttpGet("students")]
    public async Task<ActionResult<IEnumerable<StudentPhotoStateDto>>> GetOwnStudents()
    {
        var ids = await User.GetAccessibleStudentIdsAsync(db);
        if (ids is null)
        {
            return Ok(Array.Empty<StudentPhotoStateDto>());
        }
        var students = await db.Students.Where(s => ids.Contains(s.Id)).OrderBy(s => s.Person!.FirstName).ToListAsync();
        var states = new List<StudentPhotoStateDto>();
        foreach (var student in students)
        {
            states.Add(await StateAsync(student));
        }
        return Ok(states);
    }

    [HttpGet("students/{studentId:int}")]
    public async Task<ActionResult<StudentPhotoStateDto>> GetStudentState(int studentId)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null || !await User.CanAccessStudentAsync(db, studentId))
        {
            return NotFound();
        }
        return Ok(await StateAsync(student));
    }

    /// <summary>The school (admin, class teacher) or a parent uploads an
    /// approved photo, the pupil one waiting for approval — all only with a
    /// consent.</summary>
    [HttpPut("students/{studentId:int}")]
    [RequestSizeLimit(PhotoStorage.MaxUploadBytes + 64 * 1024)]
    public async Task<ActionResult<StudentPhotoStateDto>> UploadStudentPhoto(int studentId, IFormFile? file)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return NotFound();
        }
        var schoolSide = await access.IsSchoolSideAsync(User, student);
        var self = PhotoAccess.IsSelf(User, student);
        var parent = await IsParentOfAsync(student);
        if (!schoolSide && !self && !parent)
        {
            return Forbid();
        }
        if (!(await access.SchoolAsync()).StudentPhotos)
        {
            return BadRequest("Škola fotky žáků nemá zapnuté.");
        }
        if (student.PhotoConsentAt is null)
        {
            return BadRequest("Bez souhlasu zákonného zástupce nelze fotku nahrát.");
        }
        if (await ValidateUploadAsync(file) is { } invalid)
        {
            return invalid;
        }

        var key = await photos.SaveAsync(currentSchool.SchoolId!.Value, file!);
        if (key is null)
        {
            return BadRequest("Soubor se nepodařilo přečíst jako obrázek.");
        }
        photos.Delete(student.Person!.PhotoKey);
        student.Person!.PhotoKey = key;
        // A pupil — even an adult one — never approves their own photo.
        student.Person!.PhotoStatus = schoolSide || parent ? PhotoStatus.Approved : PhotoStatus.Pending;
        await db.SaveChangesAsync();

        if (student.Person!.PhotoStatus == PhotoStatus.Pending)
        {
            await NotifyParentsAsync(student);
        }
        return Ok(await StateAsync(student));
    }

    [HttpPost("students/{studentId:int}/approve")]
    public async Task<ActionResult<StudentPhotoStateDto>> ApproveStudentPhoto(int studentId)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student?.Person!.PhotoKey is null)
        {
            return NotFound();
        }
        if (!await access.CanApproveStudentPhotoAsync(User, student))
        {
            return Forbid();
        }
        student.Person!.PhotoStatus = PhotoStatus.Approved;
        await db.SaveChangesAsync();
        return Ok(await StateAsync(student));
    }

    /// <summary>Rejected = deleted right away.</summary>
    [HttpPost("students/{studentId:int}/reject")]
    public async Task<ActionResult<StudentPhotoStateDto>> RejectStudentPhoto(int studentId)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student?.Person!.PhotoKey is null)
        {
            return NotFound();
        }
        if (!await access.CanApproveStudentPhotoAsync(User, student))
        {
            return Forbid();
        }
        RemovePhoto(student);
        await db.SaveChangesAsync();
        return Ok(await StateAsync(student));
    }

    /// <summary>Whoever may approve (the school even after a parent
    /// approved, e.g. for unsuitable content), and the pupil their own.</summary>
    [HttpDelete("students/{studentId:int}")]
    public async Task<ActionResult<StudentPhotoStateDto>> DeleteStudentPhoto(int studentId)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return NotFound();
        }
        if (!PhotoAccess.IsSelf(User, student) && !await access.CanApproveStudentPhotoAsync(User, student))
        {
            return Forbid();
        }
        RemovePhoto(student);
        await db.SaveChangesAsync();
        return Ok(await StateAsync(student));
    }

    /// <summary>A parent gives the consent themselves; the school records
    /// one given on paper, with the guardian's name.</summary>
    [HttpPut("students/{studentId:int}/consent")]
    public async Task<ActionResult<StudentPhotoStateDto>> GiveConsent(int studentId, PhotoConsentRequest request)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return NotFound();
        }
        string givenBy;
        if (await IsParentOfAsync(student))
        {
            var me = await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == PhotoAccess.ViewerId(User));
            givenBy = PersonNames.Display(me);
        }
        else if (await access.IsSchoolSideAsync(User, student))
        {
            if (string.IsNullOrWhiteSpace(request.GivenBy))
            {
                return this.FieldError("givenBy", "Vyplňte, který zákonný zástupce souhlas dal.");
            }
            givenBy = request.GivenBy.Trim();
        }
        else
        {
            return Forbid();
        }

        student.PhotoConsentGivenBy = givenBy;
        student.PhotoConsentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await StateAsync(student));
    }

    /// <summary>Revoking deletes the photo, not just hides it.</summary>
    [HttpDelete("students/{studentId:int}/consent")]
    public async Task<ActionResult<StudentPhotoStateDto>> RevokeConsent(int studentId)
    {
        var student = await db.Students.FindAsync(studentId);
        if (student is null)
        {
            return NotFound();
        }
        if (!await IsParentOfAsync(student) && !await access.IsSchoolSideAsync(User, student))
        {
            return Forbid();
        }
        RemovePhoto(student);
        student.PhotoConsentGivenBy = null;
        student.PhotoConsentAt = null;
        await db.SaveChangesAsync();
        return Ok(await StateAsync(student));
    }

    private void RemovePhoto(StudentProfile student)
    {
        photos.Delete(student.Person!.PhotoKey);
        student.Person!.PhotoKey = null;
        student.Person!.PhotoStatus = null;
    }

    private async Task<StudentPhotoStateDto> StateAsync(StudentProfile student)
    {
        var school = await access.SchoolAsync();
        var enabled = school.StudentPhotos;
        var schoolSide = await access.IsSchoolSideAsync(User, student);
        var self = PhotoAccess.IsSelf(User, student);
        var parent = await IsParentOfAsync(student);
        var canApprove = await access.CanApproveStudentPhotoAsync(User, student);
        var visible = await access.CanSeeStudentPhotoAsync(User, student);
        return new StudentPhotoStateDto(
            student.Id,
            student.Person!.FullName,
            visible ? PhotoStorage.Token(student.Person!.PhotoKey) : null,
            visible ? student.Person!.PhotoStatus?.ToString() : null,
            student.PhotoConsentGivenBy,
            student.PhotoConsentAt,
            enabled,
            CanUpload: enabled && student.PhotoConsentAt is not null && (schoolSide || self || parent),
            CanApprove: visible && canApprove && student.Person!.PhotoStatus == PhotoStatus.Pending,
            CanDelete: visible && (canApprove || self),
            CanManageConsent: enabled && (schoolSide || parent),
            ConsentNeedsName: schoolSide && !parent);
    }

    private async Task<bool> IsParentOfAsync(StudentProfile student) =>
        User.ActiveKind() == RoleKind.Parent
        && await db.ChildIdsOf(PhotoAccess.ViewerId(User)).AnyAsync(id => id == student.Id);

    private async Task<ActionResult?> CanManageUserPhotoAsync(User user)
    {
        if (user.Id != PhotoAccess.ViewerId(User))
        {
            return await User.HasPermissionAsync(db, Permission.Administration) && PhotoAccess.IsStaff(user) ? null : Forbid();
        }
        if (PhotoAccess.IsStaff(user))
        {
            return null;
        }
        return user.HasKind(RoleKind.Parent)
               && (await access.SchoolAsync()).ParentPhotos
            ? null
            : BadRequest("Škola fotky rodičů nemá zapnuté.");
    }

    private async Task<ActionResult?> ValidateUploadAsync(IFormFile? file)
    {
        var school = await access.SchoolAsync();
        var max = school.IsDemo ? MessageAttachmentRules.DemoMaxSizeBytes : PhotoStorage.MaxUploadBytes;
        return PhotoStorage.Validate(file, max) is { } error ? BadRequest(error) : null;
    }

    private IActionResult PhotoFile(string key, PhotoSize size)
    {
        var path = photos.FullPath(key, size);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }
        Response.Headers.CacheControl = CacheControl;
        return PhysicalFile(path, "image/webp");
    }

    /// <summary>A notification and an e-mail to the pupil's parents that a
    /// photo waits for them. A failed send doesn't fail the upload — the
    /// pending photo also shows in Mé děti.</summary>
    private async Task NotifyParentsAsync(StudentProfile student)
    {
        var parentIds = await db.ParentStudents.Where(ps => ps.StudentId == student.Id)
            .ParentAccountLinks().Select(l => l.UserId).ToListAsync();
        await notifier.NotifyAsync(
            student.SchoolId, parentIds, NotificationType.Photo, "Fotka ke schválení",
            $"{student.Person!.FullName} si nahrál(a) novou profilovou fotku.",
            "my-children", student.Id, student.Id);
        await db.SaveChangesAsync();

        var emails = await db.ParentStudents
            .Where(ps => ps.StudentId == student.Id && ps.ParentProfile!.Person!.Email != null)
            .Select(ps => ps.ParentProfile!.Person!.Email!)
            .Distinct()
            .ToListAsync();
        if (emails.Count == 0)
        {
            return;
        }
        var school = await access.SchoolAsync();
        var origin = appLinkOptions.Value.SchoolOrigin(school.Slug);
        var link = origin is null ? "" : $"""<p><a href="{origin}/my-children">{origin}/my-children</a></p>""";
        var name = System.Net.WebUtility.HtmlEncode(student.Person!.FullName);
        var body = $"""
            <p>Dobrý den,</p>
            <p>{name} si v Soovioo nahrál(a) novou profilovou fotku. Než ji uvidí učitelé,
            musíte ji schválit (nebo zamítnout) v sekci Mé děti.</p>
            {link}
            """;
        foreach (var email in emails)
        {
            try
            {
                await emailSender.SendAsync(email, $"Fotka ke schválení — {name}", body);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Photo approval e-mail for student {StudentId} wasn't sent.", student.Id);
            }
        }
    }
}

/// <param name="Photo">Token for the photo URL, null = no photo.</param>
public record PhotoDto(string? Photo);

/// <param name="GivenBy">Only when the school records a consent given on
/// paper — a parent consents as themselves.</param>
public record PhotoConsentRequest(string? GivenBy);

/// <param name="Photo">Token for the photo URL — only when the caller may see it.</param>
/// <param name="Status">"Pending" / "Approved", with <paramref name="Photo"/>.</param>
/// <param name="PhotosEnabled">The school has pupils' photos on.</param>
/// <param name="ConsentNeedsName">The caller records the consent for
/// someone else (school side) and has to name the guardian.</param>
public record StudentPhotoStateDto(
    int StudentId, string Name, string? Photo, string? Status, string? ConsentGivenBy, DateTimeOffset? ConsentAt,
    bool PhotosEnabled, bool CanUpload, bool CanApprove, bool CanDelete, bool CanManageConsent, bool ConsentNeedsName);
