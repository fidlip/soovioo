using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

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
            .Where(ps => ps.ParentProfile!.Person!.UserId != null)
            .Select(ps => new
            {
                UserId = ps.ParentProfile!.Person!.UserId!.Value,
                ps.ParentProfile.Person.User!.Username,
                ps.ParentProfile.Person.FirstName,
                ps.ParentProfile.Person.LastName,
                ps.StudentId,
                StudentName = ps.Student!.FirstName + " " + ps.Student.LastName,
            })
            .ToListAsync();

        return Ok(links
            .Select(l => new ParentStudentDto(
                l.UserId, l.Username, Display(l.FirstName, l.LastName, l.Username), l.StudentId, l.StudentName))
            .OrderBy(l => l.ParentName));
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

        if (await db.ChildIdsOf(request.UserId).AnyAsync(id => id == request.StudentId))
        {
            return BadRequest("Link already exists.");
        }

        await db.LinkChildAsync(request.UserId, request.StudentId, currentSchool.SchoolId!.Value);
        await db.SaveChangesAsync();

        var person = await db.EnsurePersonAsync(user);
        var dto = new ParentStudentDto(
            user.Id, user.Username, PersonNames.Display(person, user.Username), student.Id, $"{student.FirstName} {student.LastName}");
        return CreatedAtAction(nameof(GetLinks), dto);
    }

    [HttpDelete("{userId:int}/{studentId:int}")]
    public async Task<IActionResult> RemoveLink(int userId, int studentId)
    {
        var link = await db.ParentStudents
            .FirstOrDefaultAsync(ps => ps.ParentProfile!.Person!.UserId == userId && ps.StudentId == studentId);
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

    private static string Display(string firstName, string lastName, string username) =>
        $"{firstName} {lastName}".Trim() is { Length: > 0 } name ? name : username;

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public record CreateParentStudentLinkRequest(int UserId, int StudentId);

public record ParentStudentDto(int UserId, string Username, string ParentName, int StudentId, string StudentName);
