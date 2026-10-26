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
            .OrderBy(s => s.Person!.LastName)
            .Select(s => new StudentDto(
                s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, s.SchoolClass!.Name,
                s.RodneCislo, s.DateOfBirth, s.Gender, s.CitizenshipQualifier, s.CountryCode,
                s.Country == null ? null : s.Country.Name, s.Street, s.MunicipalityCode,
                s.Municipality == null ? null : s.Municipality.Name, s.PostalCode,
                s.CatchmentArea, s.EnrollmentDate, s.ContactPhone, s.ContactEmail, s.DefaultVariableSymbol, s.LeftAt, s.LeaveReason, s.LeaveReasonDetail, s.LeaveNote))
            .ToListAsync();

        if (photoAccess is not null)
        {
            var withPhoto = await query.Where(s => s.Person!.PhotoKey != null).ToListAsync();
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
        var student = new StudentProfile
        {
            Person = new People.Person { FirstName = request.FirstName, LastName = request.LastName, SchoolId = schoolId },
            SchoolId = schoolId,
        };
        if (await StudentRecords.ApplyAsync(db, student, request.Matrika) is { } error)
        {
            return this.FieldError("defaultVariableSymbol", error);
        }

        db.Students.Add(student);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetStudents), new { id = student.Id }, await StudentRecords.ToDtoAsync(db, student));
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

        student.Person!.FirstName = request.FirstName;
        student.Person.LastName = request.LastName;
        if (await StudentRecords.ApplyAsync(db, student, request.Matrika) is { } error)
        {
            return this.FieldError("defaultVariableSymbol", error);
        }
        await db.SaveChangesAsync();

        return Ok(await StudentRecords.ToDtoAsync(db, student));
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

        var history = await StudentRecords.HistoryOfAsync(db, student);
        if (history.Count > 0)
        {
            return Conflict(await StudentRecords.RefusalAsync(db, student, history));
        }

        await StudentRecords.DeleteAsync(db, student, photos);
        return NoContent();
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
    string? DefaultVariableSymbol = null)
{
    public StudentMatrika Matrika => new(
        SchoolClassId, RodneCislo, DateOfBirth, Gender, CitizenshipQualifier, CountryCode, Street,
        MunicipalityCode, PostalCode, CatchmentArea, EnrollmentDate, ContactPhone, ContactEmail, DefaultVariableSymbol);
}

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
