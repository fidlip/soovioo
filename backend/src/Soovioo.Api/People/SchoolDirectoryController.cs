using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.People;

public enum SchoolPersonKind
{
    Staff,
    Student,
    Parent,
}

/// <summary>Lidé → Škola for a parent or pupil: everyone at the school as
/// far as their "Viditelnost údajů" reaches the caller — staff always by
/// name (as in Zprávy's contacts), pupils and other parents only when they
/// share their name. The distance is <see cref="VisibilityScope.MyClasses"/>
/// for someone in a class of the caller's children / the pupil's own class
/// (teaching it, in it, having a child in it), otherwise
/// <see cref="VisibilityScope.School"/>. Like Třída, parents see other
/// parents, pupils don't.</summary>
[Authorize(Roles = "Parent,Student")]
[ApiController]
[Route("api/school-directory")]
public class SchoolDirectoryController(AppDbContext db, ICurrentSchoolContext currentSchool, PhotoAccess photoAccess) : ControllerBase
{
    public const int MaxPageSize = 100;

    /// <param name="search">Part of the name, regardless of case and diacritics.</param>
    /// <param name="classId">Staff teaching the class, pupils in it, parents
    /// of the pupils in it the caller sees.</param>
    [HttpGet]
    public async Task<ActionResult<SchoolDirectoryDto>> Get(
        string? search, SchoolPersonKind? kind, int? classId, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var visibility = await Visibility.LoadAsync(db, currentSchool.SchoolId!.Value);
        var isParent = User.IsInRole(nameof(UserRole.Parent));
        var audience = isParent ? VisibilityAudience.Parents : VisibilityAudience.Students;
        var accessible = await User.GetAccessibleStudentIdsAsync(db) ?? [];
        var myClassIds = (await db.Students.Where(s => accessible.Contains(s.Id)).Select(s => s.SchoolClassId).ToListAsync())
            .ToHashSet();
        var classes = await db.SchoolClasses.OrderBy(c => c.Name).Select(c => new DirectoryClassDto(c.Id, c.Name)).ToListAsync();
        var classNames = classes.ToDictionary(c => c.Id, c => c.Name);

        var people = new List<Entry>();
        if (kind is null or SchoolPersonKind.Staff)
        {
            people.AddRange(await StaffAsync(visibility, audience, myClassIds, classNames));
        }
        var pupils = kind is null or SchoolPersonKind.Student || (isParent && kind is SchoolPersonKind.Parent)
            ? await StudentsAsync(visibility, audience, accessible, myClassIds, classNames)
            : [];
        if (kind is null or SchoolPersonKind.Student)
        {
            people.AddRange(pupils);
        }
        if (isParent && kind is null or SchoolPersonKind.Parent)
        {
            people.AddRange(await ParentsAsync(visibility, myClassIds, pupils));
        }

        var folded = string.IsNullOrWhiteSpace(search) ? null : Fold(search.Trim());
        var matching = people
            .Where(p => folded is null || Fold(p.Dto.Name).Contains(folded))
            .Where(p => classId is null || p.ClassIds.Contains(classId.Value))
            .OrderBy(p => p.SortName, StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), true))
            .ToList();
        var items = matching.Skip((page - 1) * pageSize).Take(pageSize).Select(p => p.Dto).ToList();

        // Photo tokens only for the page shown — checking them costs queries.
        var userIds = items.Where(i => i.Kind != SchoolPersonKind.Student && i.UserId != null).Select(i => i.UserId!.Value).ToList();
        var userPhotos = await photoAccess.VisibleUserPhotosAsync(
            User, await db.Users.Include(u => u.Person).Where(u => userIds.Contains(u.Id)).ToListAsync());
        var studentIds = items.Where(i => i.Kind == SchoolPersonKind.Student).Select(i => i.StudentId!.Value).ToList();
        var studentPhotos = await photoAccess.VisibleStudentPhotosAsync(
            User, await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync());
        items = items.Select(i => i.Kind switch
        {
            SchoolPersonKind.Student => i with { Photo = studentPhotos.GetValueOrDefault(i.StudentId!.Value) },
            SchoolPersonKind.Staff => i with { Photo = userPhotos.GetValueOrDefault(i.UserId!.Value) },
            // A parent's account id goes out only with a photo it's needed for.
            _ => userPhotos.GetValueOrDefault(i.UserId ?? 0) is { } photo ? i with { Photo = photo } : i with { UserId = null },
        }).ToList();

        return Ok(new SchoolDirectoryDto(
            new PagedResult<SchoolPersonDto>(items, matching.Count, page, pageSize), classes, isParent));
    }

    private sealed record Entry(SchoolPersonDto Dto, string SortName, IReadOnlySet<int> ClassIds);

    private async Task<List<Entry>> StaffAsync(
        SchoolVisibility visibility, VisibilityAudience audience, HashSet<int> myClassIds, Dictionary<int, string> classNames)
    {
        var staff = await db.Users.Include(u => u.Person)
            .Where(u => u.DeactivatedAt == null && u.ErasedAt == null
                && (u.Roles.Contains(UserRole.Teacher) || u.Roles.Contains(UserRole.Admin)))
            .ToListAsync();
        var led = await db.SchoolClasses.Where(c => c.ClassTeacherId != null)
            .Select(c => new { TeacherId = c.ClassTeacherId!.Value, ClassId = c.Id }).ToListAsync();
        var taught = await db.TimetableEntries.Where(t => t.TeacherId != null)
            .Select(t => new { TeacherId = t.TeacherId!.Value, ClassId = t.SchoolClassId, Subject = t.Subject!.Name })
            .Distinct().ToListAsync();
        var choices = await ChoicesOfPersonsAsync(staff.Where(u => u.Person != null).Select(u => u.Person!.Id));

        return staff.Select(u =>
        {
            var classIds = led.Where(l => l.TeacherId == u.Id).Select(l => l.ClassId)
                .Concat(taught.Where(t => t.TeacherId == u.Id).Select(t => t.ClassId))
                .ToHashSet();
            var distance = classIds.Overlaps(myClassIds) ? VisibilityScope.MyClasses : VisibilityScope.School;
            var own = u.Person is null ? [] : choices[u.Person.Id];
            bool Shows(VisibilityField field) =>
                visibility.Shows(new(VisibilityOwner.Staff, field, audience), own, distance);
            var notes = led.Where(l => l.TeacherId == u.Id).Select(l => $"třídní {classNames.GetValueOrDefault(l.ClassId)}")
                .Concat(taught.Where(t => t.TeacherId == u.Id).Select(t => t.Subject).Distinct().Order())
                .ToList();
            if (notes.Count == 0 && u.Roles.Contains(UserRole.Admin))
            {
                notes.Add("vedení školy");
            }
            return new Entry(
                new SchoolPersonDto(
                    SchoolPersonKind.Staff, PersonNames.Display(u), string.Join(", ", notes), [],
                    Shows(VisibilityField.Phone) ? u.Person?.Phone : null,
                    Shows(VisibilityField.Email) ? u.Person?.Email : null,
                    u.Id, null, null),
                SortKey(u.Person?.LastName, u.Person?.FirstName, u.Username), classIds);
        }).ToList();
    }

    /// <summary>The pupils the caller sees — their own / their children
    /// always.</summary>
    private async Task<List<Entry>> StudentsAsync(
        SchoolVisibility visibility, VisibilityAudience audience, IReadOnlyCollection<int> accessible, HashSet<int> myClassIds,
        Dictionary<int, string> classNames)
    {
        var key = new VisibilityKey(VisibilityOwner.Student, VisibilityField.Name, audience);
        var pupils = await db.Students.ToListAsync();
        var personIds = pupils.Select(p => p.PersonId).ToList();
        var choices = (await db.VisibilityChoices.Where(c => personIds.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);
        return pupils
            .Where(p => accessible.Contains(p.Id) || visibility.Shows(
                key, choices[p.PersonId], myClassIds.Contains(p.SchoolClassId) ? VisibilityScope.MyClasses : VisibilityScope.School))
            .Select(p => new Entry(
                new SchoolPersonDto(
                    SchoolPersonKind.Student, p.Person!.FullName, classNames.GetValueOrDefault(p.SchoolClassId) ?? "",
                    [], null, null, null, p.Id, null),
                SortKey(p.Person!.LastName, p.Person!.FirstName, ""), new HashSet<int> { p.SchoolClassId }))
            .ToList();
    }

    /// <summary>Other parents who share their name with the caller. Their
    /// children (and the class filter) only from the pupils the caller sees
    /// — otherwise the parent list would leak them.</summary>
    private async Task<List<Entry>> ParentsAsync(SchoolVisibility visibility, HashSet<int> myClassIds, List<Entry> pupils)
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var links = await db.ParentStudents
            .Select(ps => new
            {
                ps.StudentId,
                ps.Student!.SchoolClassId,
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
        var visiblePupils = pupils.ToDictionary(p => p.Dto.StudentId!.Value, p => p.Dto);

        return links
            .GroupBy(l => l.PersonId)
            .Select(g =>
            {
                var p = g.First();
                var distance = g.Any(l => myClassIds.Contains(l.SchoolClassId)) ? VisibilityScope.MyClasses : VisibilityScope.School;
                bool Shows(VisibilityField field) =>
                    visibility.Shows(new(VisibilityOwner.Parent, field, VisibilityAudience.Parents), choices[p.PersonId], distance);
                if (p.UserId == me || !Shows(VisibilityField.Name))
                {
                    return null;
                }
                var children = g.Where(l => visiblePupils.ContainsKey(l.StudentId)).ToList();
                var name = $"{p.FirstName} {p.LastName}".Trim();
                return new Entry(
                    new SchoolPersonDto(
                        SchoolPersonKind.Parent, name.Length == 0 ? p.Username : name, "",
                        children.Select(c => $"{visiblePupils[c.StudentId].Name} ({visiblePupils[c.StudentId].Notes})").Order().ToList(),
                        Shows(VisibilityField.Phone) ? p.Phone : null,
                        Shows(VisibilityField.Email) ? p.Email : null,
                        p.UserId, null, null),
                    SortKey(p.LastName, p.FirstName, p.Username), children.Select(c => c.SchoolClassId).ToHashSet());
            })
            .OfType<Entry>()
            .ToList();
    }

    private async Task<ILookup<int, VisibilityChoice>> ChoicesOfPersonsAsync(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        return (await db.VisibilityChoices.Where(c => ids.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);
    }

    private static string SortKey(string? lastName, string? firstName, string fallback) =>
        string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName) ? fallback : $"{lastName} {firstName}";

    /// <summary>Lower case without diacritics — "Šalda" and "salda" match.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(char.ToLowerInvariant(c));
            }
        }
        return result.ToString();
    }
}

/// <param name="Notes">Staff: classes they lead and subjects; pupils: their class.</param>
/// <param name="Children">Parents: their children the caller sees, "Name (class)".</param>
/// <param name="Phone">Null when not shared with the caller.</param>
/// <param name="UserId">Staff always; parents only with <paramref name="Photo"/> — the photo URL needs it.</param>
/// <param name="Photo">Photo token, when the caller may see it (see <see cref="PhotoAccess"/>).</param>
public record SchoolPersonDto(
    SchoolPersonKind Kind, string Name, string Notes, IReadOnlyList<string> Children, string? Phone, string? Email,
    int? UserId, int? StudentId, string? Photo);

public record DirectoryClassDto(int Id, string Name);

/// <param name="Classes">For the class filter.</param>
/// <param name="ShowsParents">The caller is a parent — the list has other parents too.</param>
public record SchoolDirectoryDto(PagedResult<SchoolPersonDto> People, IReadOnlyList<DirectoryClassDto> Classes, bool ShowsParents);
