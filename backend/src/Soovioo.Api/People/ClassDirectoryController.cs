using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.People;

/// <summary>"Třída" for a parent or pupil: who teaches the class, and —
/// as far as everyone's "Viditelnost údajů" shares them with the caller's
/// audience (parents / pupils) in the class — the teachers' contacts, the
/// class list and the other parents. What isn't shared never leaves the API
/// (null section / null contact), the clients only render what they get.</summary>
[Authorize(Roles = nameof(RoleKind.Parent) + "," + nameof(RoleKind.Student))]
[ApiController]
[Route("api/class-directory")]
public class ClassDirectoryController(AppDbContext db, ICurrentSchoolContext currentSchool, PhotoAccess photoAccess) : ControllerBase
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

        var visibility = await Visibility.LoadAsync(db, currentSchool.SchoolId!.Value);
        var student = await db.Students.Include(s => s.SchoolClass).FirstAsync(s => s.Id == id);
        var classId = student.SchoolClassId;
        var isParent = User.ActiveKind() == RoleKind.Parent;
        var audience = isParent ? VisibilityAudience.Parents : VisibilityAudience.Students;

        var teachers = await TeachersAsync(visibility, audience, student.Id, student.SchoolClass!.ClassTeacherId);

        List<ClassmateDto>? classmates = null;
        if (visibility.CanShow(new(VisibilityOwner.Student, VisibilityField.Name, audience), VisibilityScope.MyClasses))
        {
            var pupils = await db.Students
                .Where(s => s.SchoolClassId == classId)
                .OrderBy(s => s.Person!.LastName).ThenBy(s => s.Person!.FirstName)
                .ToListAsync();
            var personIds = pupils.Select(p => p.PersonId).ToList();
            var choices = (await db.VisibilityChoices.Where(c => personIds.Contains(c.PersonId)).ToListAsync())
                .ToLookup(c => c.PersonId);
            var listed = pupils
                .Where(p => accessible.Contains(p.Id) || visibility.Shows(
                    new(VisibilityOwner.Student, VisibilityField.Name, audience), choices[p.PersonId], VisibilityScope.MyClasses))
                .ToList();
            var photos = await photoAccess.VisibleStudentPhotosAsync(User, listed);
            var labels = await db.StudentLabelsAsync(User, listed.Select(p => p.Id));
            classmates = listed
                .Select(p => new ClassmateDto(p.Id, p.Person!.FullName, photos.GetValueOrDefault(p.Id), labels.GetValueOrDefault(p.Id)))
                .ToList();
        }

        List<DirectoryParentDto>? parents = null;
        if (isParent && visibility.CanShow(new(VisibilityOwner.Parent, VisibilityField.Name, audience), VisibilityScope.MyClasses))
        {
            parents = await ParentsAsync(visibility, classId, classmates);
        }

        return Ok(new ClassDirectoryDto(
            student.Id, student.Person!.FullName, student.SchoolClass.Name,
            teachers, classmates, parents,
            (await db.StudentLabelsAsync(User, [student.Id])).GetValueOrDefault(student.Id)));
    }

    /// <summary>Class teacher and everyone with a lesson of the pupil (the
    /// class's or their group's) — names always (the same people are in
    /// Zprávy's contacts), phone and e-mail where the teacher shares them with
    /// the caller's audience.</summary>
    private async Task<List<DirectoryTeacherDto>> TeachersAsync(
        SchoolVisibility visibility, VisibilityAudience audience, int studentId, int? classTeacherId)
    {
        var teaching = await db.TimetableEntriesOn().LessonsOf(db, studentId)
            .Where(t => t.TeacherId != null)
            .Select(t => new { TeacherId = t.TeacherId!.Value, t.Subject!.Name })
            .Distinct()
            .ToListAsync();
        var ids = teaching.Select(t => t.TeacherId).ToHashSet();
        if (classTeacherId is { } ct)
        {
            ids.Add(ct);
        }
        var users = await db.Users.Include(u => u.Person).Where(u => ids.Contains(u.Id)).ToListAsync();
        var choices = await ChoicesOfPersonsAsync(users.Where(u => u.Person != null).Select(u => u.Person!.Id));
        var userPhotos = await photoAccess.VisibleUserPhotosAsync(User, users);

        return users
            .Select(u =>
            {
                var notes = new List<string>();
                if (u.Id == classTeacherId)
                {
                    notes.Add("třídní učitel");
                }
                notes.AddRange(teaching.Where(t => t.TeacherId == u.Id).Select(t => t.Name).Order());
                var own = u.Person is null ? [] : choices[u.Person.Id];
                bool Shows(VisibilityField field) =>
                    visibility.Shows(new(VisibilityOwner.Staff, field, audience), own, VisibilityScope.MyClasses);
                return new DirectoryTeacherDto(
                    u.Id, PersonNames.Display(u), string.Join(", ", notes),
                    Shows(VisibilityField.Phone) ? u.Person?.Phone : null,
                    Shows(VisibilityField.Email) ? u.Person?.Email : null,
                    userPhotos.GetValueOrDefault(u.Id));
            })
            .OrderBy(t => t.UserId == classTeacherId ? 0 : 1)
            .ThenBy(t => t.Name)
            .ToList();
    }

    /// <summary>Parents of the class's pupils except the caller, those who
    /// share their name with the class's parents. Their children are named
    /// only when they're on the class list the caller got — otherwise the
    /// parent list would leak it.</summary>
    private async Task<List<DirectoryParentDto>> ParentsAsync(
        SchoolVisibility visibility, int classId, List<ClassmateDto>? classmates)
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
            })
            .ToListAsync();
        var choices = await ChoicesOfPersonsAsync(links.Select(l => l.PersonId));
        var userIds = links.Where(l => l.UserId != null).Select(l => l.UserId!.Value).Distinct().ToList();
        var photos = await photoAccess.VisibleUserPhotosAsync(
            User, await db.Users.Include(u => u.Person).Where(u => userIds.Contains(u.Id)).ToListAsync());
        var childNames = classmates?.ToDictionary(c => c.StudentId, c => c.Name) ?? [];
        bool Shows(int personId, VisibilityField field) =>
            visibility.Shows(new(VisibilityOwner.Parent, field, VisibilityAudience.Parents), choices[personId], VisibilityScope.MyClasses);

        return links
            .GroupBy(l => l.PersonId)
            .Select(g => g.First())
            .Where(p => p.UserId != me && Shows(p.PersonId, VisibilityField.Name))
            .Select(p =>
            {
                var name = $"{p.FirstName} {p.LastName}".Trim();
                var children = links
                    .Where(l => l.PersonId == p.PersonId && childNames.ContainsKey(l.StudentId))
                    .Select(l => childNames[l.StudentId])
                    .Order()
                    .ToList();
                var photo = p.UserId is { } userId ? photos.GetValueOrDefault(userId) : null;
                return new DirectoryParentDto(
                    name.Length == 0 ? p.Username : name, children,
                    Shows(p.PersonId, VisibilityField.Phone) ? p.Phone : null,
                    Shows(p.PersonId, VisibilityField.Email) ? p.Email : null,
                    photo is null ? null : p.UserId, photo);
            })
            .OrderBy(p => p.Name)
            .ToList();
    }

    private async Task<ILookup<int, VisibilityChoice>> ChoicesOfPersonsAsync(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        return (await db.VisibilityChoices.Where(c => ids.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);
    }
}

/// <param name="Classmates">Null when the school lets nobody share their
/// place on the class list with the caller's audience.</param>
/// <param name="Parents">Null unless the caller is a parent and the school
/// lets parents share their names with other parents.</param>
public record ClassDirectoryDto(
    int StudentId, string StudentName, string ClassName, IReadOnlyList<DirectoryTeacherDto> Teachers,
    IReadOnlyList<ClassmateDto>? Classmates, IReadOnlyList<DirectoryParentDto>? Parents, NameLabel? StudentLabel = null);

/// <param name="Notes">"třídní učitel, Matematika, …".</param>
/// <param name="Phone">Null when not shared with the caller.</param>
/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
public record DirectoryTeacherDto(int UserId, string Name, string Notes, string? Phone, string? Email, string? Photo);

/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
public record ClassmateDto(int StudentId, string Name, string? Photo, NameLabel? Label = null);

/// <param name="Children">Their children in the class — empty unless the
/// class list is shared.</param>
/// <param name="UserId">Set only with <paramref name="Photo"/> — the photo URL needs it.</param>
public record DirectoryParentDto(
    string Name, IReadOnlyList<string> Children, string? Phone, string? Email, int? UserId, string? Photo);
