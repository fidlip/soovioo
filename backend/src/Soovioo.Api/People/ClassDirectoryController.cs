using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.People;

/// <summary>"Třída" for a parent or pupil: who teaches the class, and —
/// as far as the school's "Viditelnost údajů" shares them — the teachers'
/// contacts, the class list and the other parents. What isn't shared never
/// leaves the API (null section / null contact), the clients only render
/// what they get.</summary>
[Authorize(Roles = "Parent,Student")]
[ApiController]
[Route("api/class-directory")]
public class ClassDirectoryController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <param name="studentId">One of the caller's children (or the pupil
    /// themselves); omitted = the first of them.</param>
    [HttpGet]
    public async Task<ActionResult<ClassDirectoryDto>> Get(int? studentId)
    {
        var accessible = await User.GetAccessibleStudentIdsAsync(db) ?? [];
        var id = studentId ?? (accessible.Count > 0 ? accessible.Min() : 0);
        if (!accessible.Contains(id))
        {
            return NotFound();
        }

        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId);
        var student = await db.Students.Include(s => s.SchoolClass).FirstAsync(s => s.Id == id);
        var classId = student.SchoolClassId;
        var isParent = User.IsInRole(nameof(UserRole.Parent));

        var teachers = await TeachersAsync(school, classId, student.SchoolClass!.ClassTeacherId, isParent);

        List<ClassmateDto>? classmates = null;
        if (Visibility.Shows(school, VisibilityOption.Classmates))
        {
            var optOut = school.OptOutAllowed.HasFlag(VisibilityOption.Classmates);
            classmates = await db.Students
                .Where(s => s.SchoolClassId == classId
                            && (!optOut || !s.HiddenFromClassmates || accessible.Contains(s.Id)))
                .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .Select(s => new ClassmateDto(s.Id, s.FirstName + " " + s.LastName))
                .ToListAsync();
        }

        List<DirectoryParentDto>? parents = null;
        if (isParent && Visibility.Shows(school, VisibilityOption.ParentContacts))
        {
            parents = await ParentsAsync(school, classId, classmates);
        }

        return Ok(new ClassDirectoryDto(
            student.Id, $"{student.FirstName} {student.LastName}", student.SchoolClass.Name,
            teachers, classmates, parents));
    }

    /// <summary>Class teacher and everyone with a lesson in the class —
    /// names always (the same people are in Zprávy's contacts), phone and
    /// e-mail only for parents and only where shared.</summary>
    private async Task<List<DirectoryTeacherDto>> TeachersAsync(
        Evidence.School school, int classId, int? classTeacherId, bool isParent)
    {
        var teaching = await db.TimetableEntries
            .Where(t => t.SchoolClassId == classId && t.TeacherId != null)
            .Select(t => new { TeacherId = t.TeacherId!.Value, t.Subject!.Name })
            .Distinct()
            .ToListAsync();
        var ids = teaching.Select(t => t.TeacherId).ToHashSet();
        if (classTeacherId is { } ct)
        {
            ids.Add(ct);
        }
        var users = await db.Users.Include(u => u.Person).Where(u => ids.Contains(u.Id)).ToListAsync();

        return users
            .Select(u =>
            {
                var notes = new List<string>();
                if (u.Id == classTeacherId)
                {
                    notes.Add("třídní učitel");
                }
                notes.AddRange(teaching.Where(t => t.TeacherId == u.Id).Select(t => t.Name).Order());
                var contacts = isParent
                    && Visibility.Shows(school, VisibilityOption.TeacherContacts, u.Person?.HiddenData ?? VisibilityOption.None);
                return new DirectoryTeacherDto(
                    u.Id, PersonNames.Display(u), string.Join(", ", notes),
                    contacts ? u.Person?.Phone : null, contacts ? u.Person?.Email : null);
            })
            .OrderBy(t => t.UserId == classTeacherId ? 0 : 1)
            .ThenBy(t => t.Name)
            .ToList();
    }

    /// <summary>Parents of the class's pupils except the caller. Their
    /// children are named only when the class list is shared (and the child
    /// is on it) — otherwise the parent list would leak it.</summary>
    private async Task<List<DirectoryParentDto>> ParentsAsync(
        Evidence.School school, int classId, List<ClassmateDto>? classmates)
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var optOut = school.OptOutAllowed.HasFlag(VisibilityOption.ParentContacts);
        var links = await db.ParentStudents
            .Where(ps => ps.Student!.SchoolClassId == classId)
            .Select(ps => new
            {
                ps.StudentId,
                PersonId = ps.ParentProfile!.Person!.Id,
                ps.ParentProfile.Person.UserId,
                ps.ParentProfile.Person.FirstName,
                ps.ParentProfile.Person.LastName,
                Username = ps.ParentProfile.Person.User == null ? "" : ps.ParentProfile.Person.User.Username,
                ps.ParentProfile.Person.Phone,
                ps.ParentProfile.Person.Email,
                ps.ParentProfile.Person.HiddenData,
            })
            .ToListAsync();
        var childNames = classmates?.ToDictionary(c => c.StudentId, c => c.Name) ?? [];

        return links
            .GroupBy(l => l.PersonId)
            .Select(g => g.First())
            .Where(p => p.UserId != me && !(optOut && p.HiddenData.HasFlag(VisibilityOption.ParentContacts)))
            .Select(p =>
            {
                var name = $"{p.FirstName} {p.LastName}".Trim();
                var children = links
                    .Where(l => l.PersonId == p.PersonId && childNames.ContainsKey(l.StudentId))
                    .Select(l => childNames[l.StudentId])
                    .Order()
                    .ToList();
                return new DirectoryParentDto(name.Length == 0 ? p.Username : name, children, p.Phone, p.Email);
            })
            .OrderBy(p => p.Name)
            .ToList();
    }
}

/// <param name="Classmates">Null when the school doesn't share the class list.</param>
/// <param name="Parents">Null unless the caller is a parent and the school
/// shares the other parents.</param>
public record ClassDirectoryDto(
    int StudentId, string StudentName, string ClassName, IReadOnlyList<DirectoryTeacherDto> Teachers,
    IReadOnlyList<ClassmateDto>? Classmates, IReadOnlyList<DirectoryParentDto>? Parents);

/// <param name="Notes">"třídní učitel, Matematika, …".</param>
/// <param name="Phone">Null when not shared (or hidden by the teacher).</param>
public record DirectoryTeacherDto(int UserId, string Name, string Notes, string? Phone, string? Email);

public record ClassmateDto(int StudentId, string Name);

/// <param name="Children">Their children in the class — empty unless the
/// class list is shared.</param>
public record DirectoryParentDto(string Name, IReadOnlyList<string> Children, string? Phone, string? Email);
