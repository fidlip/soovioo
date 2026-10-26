using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Evidence;

/// <summary>Parent↔pupil links of the pupils the caller's
/// <see cref="Permission.Students"/> reaches (by default: Admin all, a
/// class teacher their class), same as <see cref="StudentsController"/>.</summary>
[Authorize]
[RequirePermission(Permission.Students)]
[ApiController]
[Route("api/parent-student-links")]
public class ParentStudentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ParentStudentDto>>> GetLinks()
    {
        var query = db.ParentStudents.AsQueryable();
        if (await User.ClassIdsAsync(db, Permission.Students) is { } classIds)
        {
            query = query.Where(ps => classIds.Contains(ps.Student!.SchoolClassId));
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
                StudentName = ps.Student!.Person!.FirstName + " " + ps.Student.Person!.LastName,
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
            return BadRequest("Neznámý uživatel nebo žák.");
        }

        if (!await User.ReachesClassAsync(db, Permission.Students, student.SchoolClassId))
        {
            return Forbid();
        }

        if (await db.ChildIdsOf(request.UserId).AnyAsync(id => id == request.StudentId))
        {
            return BadRequest("Vazba už existuje.");
        }

        await db.LinkChildAsync(request.UserId, request.StudentId, currentSchool.SchoolId!.Value);
        await db.SaveChangesAsync();

        var person = await db.EnsurePersonAsync(user);
        var dto = new ParentStudentDto(
            user.Id, user.Username, PersonNames.Display(person, user.Username), student.Id, student.Person!.FullName);
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

        var student = await db.Students.FindAsync(studentId);
        if (student is null || !await User.ReachesClassAsync(db, Permission.Students, student.SchoolClassId))
        {
            return Forbid();
        }

        db.ParentStudents.Remove(link);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string Display(string firstName, string lastName, string username) =>
        $"{firstName} {lastName}".Trim() is { Length: > 0 } name ? name : username;
}

public record CreateParentStudentLinkRequest(int UserId, int StudentId);

public record ParentStudentDto(int UserId, string Username, string ParentName, int StudentId, string StudentName);
