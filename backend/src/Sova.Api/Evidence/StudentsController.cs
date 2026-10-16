using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/students")]
public class StudentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentDto>>> GetStudents()
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
                s.RodneCislo, s.DateOfBirth, s.Gender, s.Citizenship, s.Street, s.City, s.PostalCode,
                s.CatchmentArea, s.EnrollmentDate, s.ContactPhone, s.ContactEmail))
            .ToListAsync();

        return Ok(students);
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<StudentDto>> AddStudent(CreateStudentRequest request)
    {
        var schoolClass = await db.SchoolClasses.FindAsync(request.SchoolClassId);
        if (schoolClass is null)
        {
            return BadRequest("Unknown schoolClassId.");
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
            Citizenship = request.Citizenship,
            Street = request.Street,
            City = request.City,
            PostalCode = request.PostalCode,
            CatchmentArea = request.CatchmentArea,
            EnrollmentDate = request.EnrollmentDate,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
        };

        db.Students.Add(student);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetStudents), new { id = student.Id }, ToDto(student, schoolClass.Name));
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
            return BadRequest("Unknown schoolClassId.");
        }

        student.FirstName = request.FirstName;
        student.LastName = request.LastName;
        student.SchoolClassId = request.SchoolClassId;
        student.RodneCislo = request.RodneCislo;
        student.DateOfBirth = request.DateOfBirth;
        student.Gender = request.Gender;
        student.Citizenship = request.Citizenship;
        student.Street = request.Street;
        student.City = request.City;
        student.PostalCode = request.PostalCode;
        student.CatchmentArea = request.CatchmentArea;
        student.EnrollmentDate = request.EnrollmentDate;
        student.ContactPhone = request.ContactPhone;
        student.ContactEmail = request.ContactEmail;
        await db.SaveChangesAsync();

        return Ok(ToDto(student, schoolClass.Name));
    }

    /// <summary>Unlike the other evidence entities, a student leaving the
    /// school is a normal, expected event — so this actually removes their
    /// records (grades, absences, messages, parent links) rather than
    /// rejecting while any exist. There's no undo; the admin-page confirms
    /// before calling this.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<IActionResult> DeleteStudent(int id)
    {
        var student = await db.Students.FindAsync(id);
        if (student is null)
        {
            return NotFound();
        }

        db.Grades.RemoveRange(db.Grades.Where(g => g.StudentId == id));
        db.Absences.RemoveRange(db.Absences.Where(a => a.StudentId == id));
        db.Messages.RemoveRange(db.Messages.Where(m => m.StudentId == id));
        db.ParentStudents.RemoveRange(db.ParentStudents.Where(ps => ps.StudentId == id));
        db.Students.Remove(student);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static StudentDto ToDto(Student student, string schoolClassName) => new(
        student.Id, student.FirstName, student.LastName, student.SchoolClassId, schoolClassName,
        student.RodneCislo, student.DateOfBirth, student.Gender, student.Citizenship, student.Street,
        student.City, student.PostalCode, student.CatchmentArea, student.EnrollmentDate,
        student.ContactPhone, student.ContactEmail);
}

public record CreateStudentRequest(
    string FirstName,
    string LastName,
    int SchoolClassId,
    string? RodneCislo = null,
    DateOnly? DateOfBirth = null,
    Gender? Gender = null,
    string? Citizenship = null,
    string? Street = null,
    string? City = null,
    string? PostalCode = null,
    bool? CatchmentArea = null,
    DateOnly? EnrollmentDate = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public record StudentDto(
    int Id,
    string FirstName,
    string LastName,
    int SchoolClassId,
    string SchoolClassName,
    string? RodneCislo,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? Citizenship,
    string? Street,
    string? City,
    string? PostalCode,
    bool? CatchmentArea,
    DateOnly? EnrollmentDate,
    string? ContactPhone,
    string? ContactEmail);
