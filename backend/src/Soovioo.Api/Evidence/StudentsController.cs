using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/students")]
public class StudentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentDto>>> GetStudents([FromServices] People.PhotoAccess? photoAccess = null)
    {
        var allowedStudentIds = await User.GetAccessibleStudentIdsAsync(db);

        var query = db.Students.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(s => allowedStudentIds.Contains(s.Id));
        }

        // Projected by hand (not via the ToDto helper below) so EF Core can
        // translate this straight to SQL instead of materializing every
        // column and mapping client-side.
        var students = await query
            .OrderBy(s => s.LastName)
            .Select(s => new StudentDto(
                s.Id, s.FirstName, s.LastName, s.SchoolClassId, s.SchoolClass!.Name,
                s.RodneCislo, s.DateOfBirth, s.Gender, s.CitizenshipQualifier, s.CountryCode,
                s.Country == null ? null : s.Country.Name, s.Street, s.MunicipalityCode,
                s.Municipality == null ? null : s.Municipality.Name, s.PostalCode,
                s.CatchmentArea, s.EnrollmentDate, s.ContactPhone, s.ContactEmail, s.DefaultVariableSymbol, s.LeftAt, s.LeaveReason, s.LeaveReasonDetail, s.LeaveNote))
            .ToListAsync();

        if (photoAccess is not null)
        {
            var withPhoto = await query.Where(s => s.PhotoKey != null).ToListAsync();
            var photos = await photoAccess.VisibleStudentPhotosAsync(User, withPhoto);
            students = students.Select(s => s with { Photo = photos.GetValueOrDefault(s.Id) }).ToList();
        }
        return Ok(students);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<StudentDto>> AddStudent(CreateStudentRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        if (!User.IsInRole(nameof(UserRole.Admin)) && !await db.IsClassTeacherOfAsync(CurrentUserId(), request.SchoolClassId))
        {
            return Forbid();
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var variableSymbol = string.IsNullOrWhiteSpace(request.DefaultVariableSymbol)
            ? await VariableSymbols.NextAsync(db, schoolId, DateTime.Today.Year)
            : request.DefaultVariableSymbol.Trim();
        if (await IsVariableSymbolTakenAsync(variableSymbol, exceptStudentId: null))
        {
            return this.FieldError("defaultVariableSymbol", "Tento variabilní symbol už má jiný žák.");
        }

        var student = new Student
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            SchoolClassId = request.SchoolClassId,
            SchoolId = currentSchool.SchoolId!.Value,
            RodneCislo = request.RodneCislo,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            CitizenshipQualifier = request.CitizenshipQualifier,
            CountryCode = request.CountryCode,
            Street = request.Street,
            MunicipalityCode = request.MunicipalityCode,
            PostalCode = request.PostalCode,
            CatchmentArea = request.CatchmentArea,
            EnrollmentDate = request.EnrollmentDate,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            DefaultVariableSymbol = variableSymbol,
        };

        db.Students.Add(student);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetStudents), new { id = student.Id }, await ToDtoAsync(student, schoolClass.Name));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<StudentDto>> UpdateStudent(int id, CreateStudentRequest request)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null)
        {
            return NotFound();
        }

        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Neznámá třída.");
        }

        if (!User.IsInRole(nameof(UserRole.Admin)))
        {
            var teacherId = CurrentUserId();
            // Both the student's current class and the (possibly different)
            // target class must be the teacher's own — otherwise either side
            // of the move could reach outside their class.
            if (!await db.IsClassTeacherOfAsync(teacherId, student.SchoolClassId)
                || !await db.IsClassTeacherOfAsync(teacherId, request.SchoolClassId))
            {
                return Forbid();
            }
        }

        student.FirstName = request.FirstName;
        student.LastName = request.LastName;
        student.SchoolClassId = request.SchoolClassId;
        student.RodneCislo = request.RodneCislo;
        student.DateOfBirth = request.DateOfBirth;
        student.Gender = request.Gender;
        student.CitizenshipQualifier = request.CitizenshipQualifier;
        student.CountryCode = request.CountryCode;
        student.Street = request.Street;
        student.MunicipalityCode = request.MunicipalityCode;
        student.PostalCode = request.PostalCode;
        student.CatchmentArea = request.CatchmentArea;
        student.EnrollmentDate = request.EnrollmentDate;
        student.ContactPhone = request.ContactPhone;
        student.ContactEmail = request.ContactEmail;
        var variableSymbol = string.IsNullOrWhiteSpace(request.DefaultVariableSymbol)
            ? student.DefaultVariableSymbol
                ?? await VariableSymbols.NextAsync(db, student.SchoolId, DateTime.Today.Year)
            : request.DefaultVariableSymbol.Trim();
        if (await IsVariableSymbolTakenAsync(variableSymbol, exceptStudentId: student.Id))
        {
            return this.FieldError("defaultVariableSymbol", "Tento variabilní symbol už má jiný žák.");
        }
        student.DefaultVariableSymbol = variableSymbol;
        await db.SaveChangesAsync();

        return Ok(await ToDtoAsync(student, schoolClass.Name));
    }

    /// <summary>Only a pupil without any history — one added by mistake.
    /// One who has grades, absences, messages… is not deleted but leaves the
    /// school (Ukončení vzdělávání, <see cref="Retention.RetentionController"/>),
    /// and their data goes per the school's retention rules. 409 names what's
    /// there. What a new pupil gets on their own — parent links, unpaid
    /// installments of school-wide payments, a photo — goes with them, and
    /// so does their own account.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteStudent(int id, [FromServices] People.PhotoStorage? photos = null)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null)
        {
            return NotFound();
        }

        if (!User.IsInRole(nameof(UserRole.Admin)) && !await db.IsClassTeacherOfAsync(CurrentUserId(), student.SchoolClassId))
        {
            return Forbid();
        }

        var history = await HistoryOfAsync(student);
        if (history.Count > 0)
        {
            var type = await db.Schools.Where(s => s.Id == student.SchoolId).Select(s => (SchoolType?)s.Type).FirstOrDefaultAsync()
                ?? SchoolType.Elementary;
            return Conflict(StudentTerm.Localize(
                $"Žáka nejde smazat, už má {string.Join(", ", history)}. Místo smazání ukončete jeho vzdělávání.", type));
        }

        await db.DeleteAsync(db.PaymentRecipients.Where(r => r.StudentId == id));
        await db.DeleteAsync(db.PaymentStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.HomeworkStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.EventStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.ParentStudents.Where(ps => ps.StudentId == id));
        await db.DeleteAsync(db.VisibilityChoices.Where(c => c.StudentId == id));
        // Their own login (role Student) goes too — it never wrote anything
        // (see HistoryOfAsync).
        if (student.UserId is { } linkedUserId && await db.Users.FindAsync(linkedUserId) is { } linkedUser)
        {
            db.Users.Remove(linkedUser);
        }
        db.Students.Remove(student);
        await db.SaveChangesAsync();
        photos?.Delete(student.PhotoKey);
        return NoContent();
    }

    /// <summary>What makes the pupil more than a mistaken entry, as
    /// "12 známek", "3 absence"… (empty = none).</summary>
    private async Task<List<string>> HistoryOfAsync(Student student)
    {
        var id = student.Id;
        var ownId = student.UserId;
        var counts = new (int Count, string One, string Few, string Many)[]
        {
            (await db.Grades.CountAsync(g => g.StudentId == id), "známku", "známky", "známek"),
            (await db.Absences.CountAsync(a => a.StudentId == id), "absenci", "absence", "absencí"),
            (await db.BehaviorRecords.CountAsync(r => r.StudentId == id)
                + await db.BehaviorClassifications.CountAsync(c => c.StudentId == id),
                "záznam chování", "záznamy chování", "záznamů chování"),
            (await db.HomeworkCompletions.CountAsync(c => c.StudentId == id), "splněný úkol", "splněné úkoly", "splněných úkolů"),
            (await db.PaymentRecipients.CountAsync(r => r.StudentId == id && r.Paid), "zaplacenou platbu", "zaplacené platby", "zaplacených plateb"),
            (await db.Conversations.CountAsync(c => c.StudentId == id)
                + (ownId is null ? 0 : await db.Messages.CountAsync(m => m.SenderUserId == ownId)),
                "zprávu", "zprávy", "zpráv"),
        };
        return counts
            .Where(c => c.Count > 0)
            .Select(c => $"{c.Count} {(c.Count == 1 ? c.One : c.Count < 5 ? c.Few : c.Many)}")
            .ToList();
    }

    // Country/Municipality names are looked up by code rather than via
    // navigation properties, since student.Country/student.Municipality
    // aren't populated after Add/Update without an extra Include — these
    // two single-row lookups are cheaper than that.
    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<StudentDto> ToDtoAsync(Student student, string schoolClassName)
    {
        var countryName = student.CountryCode is null
            ? null
            : await db.Countries.Where(c => c.Code == student.CountryCode).Select(c => c.Name).FirstOrDefaultAsync();
        var municipalityName = student.MunicipalityCode is null
            ? null
            : await db.Municipalities.Where(m => m.Code == student.MunicipalityCode).Select(m => m.Name).FirstOrDefaultAsync();

        return new StudentDto(
            student.Id, student.FirstName, student.LastName, student.SchoolClassId, schoolClassName,
            student.RodneCislo, student.DateOfBirth, student.Gender, student.CitizenshipQualifier,
            student.CountryCode, countryName, student.Street, student.MunicipalityCode, municipalityName,
            student.PostalCode, student.CatchmentArea, student.EnrollmentDate, student.ContactPhone,
            student.ContactEmail, student.DefaultVariableSymbol, student.LeftAt, student.LeaveReason, student.LeaveReasonDetail, student.LeaveNote);
    }

    private Task<bool> IsVariableSymbolTakenAsync(string? variableSymbol, int? exceptStudentId) =>
        variableSymbol is null
            ? Task.FromResult(false)
            : db.Students.AnyAsync(s => s.DefaultVariableSymbol == variableSymbol && s.Id != exceptStudentId);
}

public record CreateStudentRequest(
    string FirstName,
    string LastName,
    int SchoolClassId,
    string? RodneCislo = null,
    DateOnly? DateOfBirth = null,
    Gender? Gender = null,
    string? CitizenshipQualifier = null,
    string? CountryCode = null,
    string? Street = null,
    string? MunicipalityCode = null,
    string? PostalCode = null,
    bool? CatchmentArea = null,
    DateOnly? EnrollmentDate = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    string? DefaultVariableSymbol = null);

public record StudentDto(
    int Id,
    string FirstName,
    string LastName,
    int SchoolClassId,
    string SchoolClassName,
    string? RodneCislo,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? CitizenshipQualifier,
    string? CountryCode,
    string? CountryName,
    string? Street,
    string? MunicipalityCode,
    string? MunicipalityName,
    string? PostalCode,
    bool? CatchmentArea,
    DateOnly? EnrollmentDate,
    string? ContactPhone,
    string? ContactEmail,
    string? DefaultVariableSymbol,
    DateOnly? LeftAt = null,
    StudentLeaveReason? LeaveReason = null,
    string? LeaveReasonDetail = null,
    string? LeaveNote = null,
    string? Photo = null);
