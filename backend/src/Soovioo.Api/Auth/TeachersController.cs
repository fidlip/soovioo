using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Auth;

/// <summary>Minimal, name-only Teacher list for pickers (e.g. timetable entry
/// assignment) that any authenticated school member needs, unlike the full
/// <see cref="UsersController"/> (Admin-only — exposes phone/email).</summary>
[Authorize]
[ApiController]
[Route("api/teachers")]
public class TeachersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeacherDto>>> GetTeachers()
    {
        var teachers = await db.Users
            .Where(u => u.Roles.Any(r => r.BuiltIn == UserRole.Teacher) && u.DeactivatedAt == null)
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, Name = PersonNames.Display(u.Person, u.Username) })
            .ToListAsync();
        var ids = teachers.Select(t => t.Id).ToList();
        var qualifications = (await db.TeacherQualifications
                .Where(q => q.TeacherProfile!.Person!.UserId != null && ids.Contains(q.TeacherProfile.Person.UserId.Value))
                .Select(q => new { UserId = q.TeacherProfile!.Person!.UserId!.Value, q.SubjectId, q.GradeFrom, q.GradeTo })
                .ToListAsync())
            .ToLookup(q => q.UserId, q => new QualificationDto(q.SubjectId, q.GradeFrom, q.GradeTo));

        return Ok(teachers.Select(t => new TeacherDto(t.Id, t.Username, t.Name, qualifications[t.Id].ToList())));
    }
}

/// <param name="Name">Display name — what pickers show.</param>
/// <param name="Qualifications">Aprobace — pickers offer those who have it first.</param>
public record TeacherDto(int Id, string Username, string Name, IReadOnlyList<QualificationDto> Qualifications);
