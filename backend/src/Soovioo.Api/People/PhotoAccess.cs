using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.People;

/// <summary>Who sees whose photo — one place for the photo endpoints and for
/// every DTO that carries a photo token (a token only ever goes out to
/// someone allowed to load the photo).
///
/// - staff photos: all staff; parents and pupils as far as the teacher's
///   "Viditelnost údajů" (<see cref="VisibilityField.Photo"/>) reaches them;
/// - parents' photos (school has <see cref="School.ParentPhotos"/> on):
///   admins and the teachers of their children; other parents per the
///   parent's visibility;
/// - pupils' photos (<see cref="School.StudentPhotos"/> on, only with
///   consent): staff, the pupil, their parents; once approved also
///   classmates and other parents per the visibility a parent set for the
///   pupil; a pending one only the pupil and those who may approve it
///   (parents, class teacher, admin);
/// - everyone sees their own.
///
/// "Moje třídy" for a parent or pupil looking at someone: a class of the
/// viewer's children / the pupil's own class that the other person teaches
/// (staff), has a child in (parents) or is in (pupils).</summary>
public class PhotoAccess(AppDbContext db, ICurrentSchoolContext currentSchool)
{
    private School? school;
    private SchoolVisibility? visibility;
    private HashSet<int>? viewerClassIds;

    public async Task<School> SchoolAsync() =>
        school ??= await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId);

    private async Task<SchoolVisibility> VisibilityAsync() =>
        visibility ??= await Visibility.LoadAsync(db, currentSchool.SchoolId!.Value);

    /// <summary>The photo tokens of <paramref name="users"/> (Person loaded)
    /// the caller may see; users without one are left out.</summary>
    public async Task<Dictionary<int, string>> VisibleUserPhotosAsync(ClaimsPrincipal viewer, IEnumerable<User> users)
    {
        var withPhoto = users.Where(u => u.Person?.PhotoKey is not null).DistinctBy(u => u.Id).ToList();
        var result = new Dictionary<int, string>();
        if (withPhoto.Count == 0)
        {
            return result;
        }

        var viewerId = ViewerId(viewer);
        var s = await SchoolAsync();
        var isAdmin = await viewer.HasPermissionAsync(db, Permission.Administration);
        var isStaff = viewer.IsStaff();
        HashSet<int>? teacherClassIds = null;
        var personIds = withPhoto.Select(u => u.Person!.Id).ToList();
        var choices = (await db.VisibilityChoices.Where(c => personIds.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);

        foreach (var user in withPhoto)
        {
            var own = choices[user.Person!.Id];
            bool visible;
            if (user.Id == viewerId)
            {
                visible = true;
            }
            else if (IsStaff(user))
            {
                visible = isStaff || await PeerSeesAsync(
                    viewer, VisibilityOwner.Staff, own, () => db.GetTeacherClassIdsAsync(user.Id));
            }
            else if (user.HasKind(RoleKind.Parent) && s.ParentPhotos)
            {
                if (isAdmin)
                {
                    visible = true;
                }
                else if (isStaff)
                {
                    teacherClassIds ??= (await db.GetTeacherClassIdsAsync(viewerId)).ToHashSet();
                    visible = (await ChildClassIdsAsync(user.Id)).Any(teacherClassIds.Contains);
                }
                else
                {
                    visible = await PeerSeesAsync(viewer, VisibilityOwner.Parent, own, () => ChildClassIdsAsync(user.Id));
                }
            }
            else
            {
                visible = false;
            }

            if (visible)
            {
                result[user.Id] = PhotoStorage.Token(user.Person!.PhotoKey)!;
            }
        }
        return result;
    }

    public async Task<string?> VisibleUserPhotoAsync(ClaimsPrincipal viewer, User user) =>
        (await VisibleUserPhotosAsync(viewer, [user])).GetValueOrDefault(user.Id);

    /// <summary>The photo tokens of <paramref name="students"/> the caller may see.</summary>
    public async Task<Dictionary<int, string>> VisibleStudentPhotosAsync(ClaimsPrincipal viewer, IEnumerable<StudentProfile> students)
    {
        var withPhoto = students.Where(st => st.Person!.PhotoKey is not null).ToList();
        var result = new Dictionary<int, string>();
        if (withPhoto.Count == 0 || !(await SchoolAsync()).StudentPhotos)
        {
            return result;
        }

        var personIds = withPhoto.Select(st => st.PersonId).ToList();
        var choices = (await db.VisibilityChoices.Where(c => personIds.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);
        foreach (var student in withPhoto)
        {
            if (await CanSeeStudentPhotoAsync(viewer, student, choices[student.PersonId]))
            {
                result[student.Id] = PhotoStorage.Token(student.Person!.PhotoKey)!;
            }
        }
        return result;
    }

    public async Task<bool> CanSeeStudentPhotoAsync(ClaimsPrincipal viewer, StudentProfile student) =>
        await CanSeeStudentPhotoAsync(
            viewer, student, await db.VisibilityChoices.Where(c => c.PersonId == student.PersonId).ToListAsync());

    private async Task<bool> CanSeeStudentPhotoAsync(
        ClaimsPrincipal viewer, StudentProfile student, IEnumerable<VisibilityChoice> choices)
    {
        if (student.Person!.PhotoKey is null || student.PhotoConsentAt is null || !(await SchoolAsync()).StudentPhotos)
        {
            return false;
        }
        var approved = student.Person!.PhotoStatus == PhotoStatus.Approved;
        if (IsSelf(viewer, student) || (approved && viewer.IsStaff()))
        {
            return true;
        }
        if (await CanApproveStudentPhotoAsync(viewer, student))
        {
            return true;
        }
        return approved && await PeerSeesAsync(
            viewer, VisibilityOwner.Student, choices, () => Task.FromResult(new List<int> { student.SchoolClassId }));
    }

    /// <summary>Parents of the pupil and the school's side.</summary>
    public async Task<bool> CanApproveStudentPhotoAsync(ClaimsPrincipal viewer, StudentProfile student)
    {
        if (viewer.IsStaff())
        {
            return await IsSchoolSideAsync(viewer, student);
        }
        return viewer.ActiveKind() == RoleKind.Parent
               && await db.ChildIdsOf(ViewerId(viewer)).AnyAsync(id => id == student.Id);
    }

    /// <summary>Staff whose <see cref="Permission.StudentPhotos"/> reaches the
    /// pupil (by default Admin and the class teacher) — the school's side of a pupil's photo.</summary>
    public async Task<bool> IsSchoolSideAsync(ClaimsPrincipal viewer, StudentProfile student) =>
        viewer.IsStaff() && await viewer.ReachesClassAsync(db, Permission.StudentPhotos, student.SchoolClassId);

    /// <summary>Whether a parent or pupil viewer sees the photo of someone
    /// with <paramref name="choices"/>, whose classes
    /// <paramref name="ownerClassIds"/> gives (asked only when needed).</summary>
    private async Task<bool> PeerSeesAsync(
        ClaimsPrincipal viewer, VisibilityOwner owner, IEnumerable<VisibilityChoice> choices, Func<Task<List<int>>> ownerClassIds)
    {
        VisibilityAudience audience;
        if (viewer.ActiveKind() == RoleKind.Parent)
        {
            audience = VisibilityAudience.Parents;
        }
        else if (viewer.ActiveKind() == RoleKind.Student)
        {
            audience = VisibilityAudience.Students;
        }
        else
        {
            return false;
        }

        var key = new VisibilityKey(owner, VisibilityField.Photo, audience);
        var rules = await VisibilityAsync();
        if (!Visibility.Keys.Contains(key) || !rules.CanShow(key, VisibilityScope.MyClasses))
        {
            return false;
        }
        viewerClassIds ??= await ViewerClassIdsAsync(viewer);
        var distance = (await ownerClassIds()).Any(viewerClassIds.Contains) ? VisibilityScope.MyClasses : VisibilityScope.School;
        return rules.Shows(key, choices, distance);
    }

    private async Task<HashSet<int>> ViewerClassIdsAsync(ClaimsPrincipal viewer)
    {
        var studentIds = await viewer.GetAccessibleStudentIdsAsync(db) ?? [];
        return (await db.Students.Where(st => studentIds.Contains(st.Id)).Select(st => st.SchoolClassId).ToListAsync())
            .ToHashSet();
    }

    private Task<List<int>> ChildClassIdsAsync(int parentUserId) =>
        db.ChildIdsOf(parentUserId)
            .Join(db.Students, id => id, st => st.Id, (_, st) => st.SchoolClassId)
            .ToListAsync();

    public static bool IsSelf(ClaimsPrincipal viewer, StudentProfile student) =>
        viewer.ActiveKind() == RoleKind.Student && student.Person!.UserId == ViewerId(viewer);

    public static bool IsStaff(User user) => user.HasKind(RoleKind.Staff);

    public static int ViewerId(ClaimsPrincipal viewer) =>
        int.Parse(viewer.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
