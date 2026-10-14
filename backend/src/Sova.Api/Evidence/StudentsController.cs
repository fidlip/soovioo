using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/students")]
public class StudentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentDto>>> GetStudents()
    {
        var students = await db.Students
            .OrderBy(s => s.LastName)
            .Select(s => new StudentDto(s.Id, s.FirstName, s.LastName, s.SchoolClassId, s.SchoolClass!.Name))
            .ToListAsync();

        return Ok(students);
    }

    [HttpPost]
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
        };

        db.Students.Add(student);
        await db.SaveChangesAsync();

        var dto = new StudentDto(student.Id, student.FirstName, student.LastName, student.SchoolClassId, schoolClass.Name);
        return CreatedAtAction(nameof(GetStudents), new { id = student.Id }, dto);
    }
}

public record CreateStudentRequest(string FirstName, string LastName, int SchoolClassId);

public record StudentDto(int Id, string FirstName, string LastName, int SchoolClassId, string SchoolClassName);
