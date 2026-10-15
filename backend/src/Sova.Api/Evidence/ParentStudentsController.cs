using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/parent-student-links")]
public class ParentStudentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentStudentDto>>> GetLinks()
    {
        var links = await db.ParentStudents
            .OrderBy(ps => ps.User!.Username)
            .Select(ps => new ParentStudentDto(
                ps.UserId,
                ps.User!.Username,
                ps.StudentId,
                ps.Student!.FirstName + " " + ps.Student.LastName))
            .ToListAsync();

        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<ParentStudentDto>> AddLink(CreateParentStudentLinkRequest request)
    {
        var user = await db.Users.FindAsync(request.UserId);
        var student = await db.Students.FindAsync(request.StudentId);
        if (user is null || student is null)
        {
            return BadRequest("Unknown userId or studentId.");
        }

        if (await db.ParentStudents.AnyAsync(ps => ps.UserId == request.UserId && ps.StudentId == request.StudentId))
        {
            return BadRequest("Link already exists.");
        }

        var link = new ParentStudent
        {
            UserId = request.UserId,
            StudentId = request.StudentId,
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.ParentStudents.Add(link);
        await db.SaveChangesAsync();

        var dto = new ParentStudentDto(user.Id, user.Username, student.Id, $"{student.FirstName} {student.LastName}");
        return CreatedAtAction(nameof(GetLinks), dto);
    }

    [HttpDelete("{userId:int}/{studentId:int}")]
    public async Task<IActionResult> RemoveLink(int userId, int studentId)
    {
        var link = await db.ParentStudents.FindAsync(userId, studentId);
        if (link is null)
        {
            return NotFound();
        }

        db.ParentStudents.Remove(link);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateParentStudentLinkRequest(int UserId, int StudentId);

public record ParentStudentDto(int UserId, string Username, int StudentId, string StudentName);
