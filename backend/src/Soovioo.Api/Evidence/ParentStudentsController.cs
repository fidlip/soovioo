using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

/// <summary>Admin manages every link in the school. A Teacher only gets in
/// here at all as a class teacher (třídní učitel) — restricted-Administrace
/// access (see ROADMAP "Administrace — redesign") — scoped to links
/// touching their own class's students, same restriction as
/// <see cref="StudentsController"/>.</summary>
[Authorize(Roles = "Teacher,Admin")]
[ApiController]
[Route("api/parent-student-links")]
public class ParentStudentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentStudentDto>>> GetLinks()
    {
        var query = db.ParentStudents.AsQueryable();
        if (!User.IsInRole(nameof(UserRole.Admin)))
        {
            var ownClassIds = await db.GetOwnClassIdsAsClassTeacherAsync(CurrentUserId());
            query = query.Where(ps => ownClassIds.Contains(ps.Student!.SchoolClassId));
        }

        var links = await query
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

        if (!User.IsInRole(nameof(UserRole.Admin)) && !await db.IsClassTeacherOfAsync(CurrentUserId(), student.SchoolClassId))
        {
            return Forbid();
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

        if (!User.IsInRole(nameof(UserRole.Admin)))
        {
            var student = await db.Students.FindAsync(studentId);
            if (student is null || !await db.IsClassTeacherOfAsync(CurrentUserId(), student.SchoolClassId))
            {
                return Forbid();
            }
        }

        db.ParentStudents.Remove(link);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record CreateParentStudentLinkRequest(int UserId, int StudentId);

public record ParentStudentDto(int UserId, string Username, int StudentId, string StudentName);
