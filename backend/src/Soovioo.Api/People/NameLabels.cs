using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.People;

/// <summary>How a pupil or parent appears to one viewer when it isn't
/// simply their full name.</summary>
/// <param name="Primary">Shown prominently — a nickname, or a parent's
/// child's first name.</param>
/// <param name="Secondary">The full name, shown subdued next to it; null =
/// <paramref name="Primary"/> alone.</param>
public record NameLabel(string Primary, string? Secondary)
{
    /// <summary>"Kuba (Jakub Novák)" — for plain text: notifications, e-mails.</summary>
    public string Text => Secondary is null ? Primary : $"{Primary} ({Secondary})";

    public static string TextOf(NameLabel? label, string fullName) => label?.Text ?? fullName;
}

/// <param name="Kind">The role the viewer looks as — a teacher who is also a
/// parent sees their child one way as a teacher, another as a parent.</param>
/// <param name="IsAdmin">The school's management — always full names.</param>
/// <param name="ShowNicknames">The viewer's own <see cref="User.ShowStudentNicknames"/>
/// (or its default).</param>
public record NameViewer(int UserId, int SchoolId, RoleKind? Kind, bool IsAdmin, bool ShowNicknames);

/// <summary>Who sees which nickname, in one place for every list, message
/// and notification (never for official documents — those keep full
/// names):
///
/// - a teacher sees a pupil's approved nickname with the full name next to
///   it, as far as the parents let it reach (<see cref="VisibilityAudience.Teachers"/>)
///   and unless they turned nicknames off;
/// - a parent sees their own child by nickname, or by first name;
/// - a pupil sees their parent by nickname where the parent shares it with
///   their children (<see cref="VisibilityAudience.Students"/>);
/// - the school's management, other pupils and other parents: full names.
///
/// A missing entry in the returned maps means "the full name".</summary>
public class NameLabels(AppDbContext db)
{
    private static readonly VisibilityKey StudentNickname =
        new(VisibilityOwner.Student, VisibilityField.Nickname, VisibilityAudience.Teachers);

    private static readonly VisibilityKey ParentNickname =
        new(VisibilityOwner.Parent, VisibilityField.Nickname, VisibilityAudience.Students);

    private readonly Dictionary<int, SchoolVisibility> visibility = [];
    private readonly Dictionary<int, HashSet<int>> taught = [];
    private NameViewer? principalViewer;

    public async Task<NameViewer> ViewerAsync(ClaimsPrincipal principal)
    {
        var userId = principal.UserId();
        if (principalViewer?.UserId == userId)
        {
            return principalViewer;
        }
        var user = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SchoolId, u.ShowStudentNicknames, Default = u.School == null || u.School.StaffSeeStudentNicknames })
            .FirstOrDefaultAsync();
        var kind = principal.ActiveKind();
        principalViewer = new NameViewer(
            userId, user?.SchoolId ?? 0, kind,
            await principal.HasPermissionAsync(db, Permission.Administration),
            user?.ShowStudentNicknames ?? (kind != RoleKind.Staff || user?.Default != false));
        return principalViewer;
    }

    /// <summary>Someone who isn't the caller — the recipient of a
    /// notification, looking as <paramref name="kind"/>.</summary>
    public async Task<NameViewer?> ViewerAsync(int userId, RoleKind kind)
    {
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.ActiveRole)
            .Include(u => u.School)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.SchoolId is not { } schoolId)
        {
            return null;
        }
        var isAdmin = kind == RoleKind.Staff
            && (user.ActiveRole?.Kind == RoleKind.Staff ? user.ActiveRole : user.Roles.FirstOrDefault(r => r.Kind == RoleKind.Staff))
                ?.BuiltIn == UserRole.Admin;
        return new NameViewer(
            user.Id, schoolId, kind, isAdmin,
            user.ShowStudentNicknames ?? (kind != RoleKind.Staff || user.School?.StaffSeeStudentNicknames != false));
    }

    public async Task<Dictionary<int, NameLabel>> StudentsAsync(ClaimsPrincipal principal, IEnumerable<int> studentIds) =>
        await StudentsAsync(await ViewerAsync(principal), studentIds);

    public async Task<NameLabel?> StudentAsync(ClaimsPrincipal principal, int studentId) =>
        (await StudentsAsync(principal, [studentId])).GetValueOrDefault(studentId);

    public async Task<Dictionary<int, NameLabel>> UsersAsync(ClaimsPrincipal principal, IEnumerable<int> userIds) =>
        await UsersAsync(await ViewerAsync(principal), userIds);

    /// <summary>Labels of pupils, by student id.</summary>
    public async Task<Dictionary<int, NameLabel>> StudentsAsync(NameViewer viewer, IEnumerable<int> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var result = new Dictionary<int, NameLabel>();
        if (ids.Count == 0 || viewer.IsAdmin)
        {
            return result;
        }

        if (viewer.Kind == RoleKind.Parent)
        {
            var children = await db.ChildIdsOf(viewer.UserId).Where(id => ids.Contains(id)).ToListAsync();
            var people = await db.Students.IgnoreQueryFilters()
                .Where(s => children.Contains(s.Id))
                .Select(s => new { s.Id, s.Person!.FirstName, s.Person.LastName, s.Person.Nickname })
                .ToListAsync();
            foreach (var p in people)
            {
                var first = p.FirstName.Length > 0 ? p.FirstName : $"{p.FirstName} {p.LastName}".Trim();
                result[p.Id] = new NameLabel(viewer.ShowNicknames && p.Nickname is not null ? p.Nickname : first, null);
            }
        }
        else if (viewer.Kind == RoleKind.Staff && viewer.ShowNicknames)
        {
            var pupils = await db.Students.IgnoreQueryFilters()
                .Where(s => ids.Contains(s.Id) && s.SchoolId == viewer.SchoolId && s.Person!.Nickname != null)
                .Select(s => new { s.Id, s.PersonId, Nickname = s.Person!.Nickname!, s.Person.FirstName, s.Person.LastName })
                .ToListAsync();
            if (pupils.Count == 0)
            {
                return result;
            }
            var rules = await VisibilityAsync(viewer.SchoolId);
            if (!rules.CanShow(StudentNickname, VisibilityScope.MyClasses))
            {
                return result;
            }
            var choices = await ChoicesAsync(pupils.Select(p => p.PersonId));
            var mine = await TaughtAsync(viewer.UserId);
            foreach (var p in pupils)
            {
                var distance = mine.Contains(p.Id) ? VisibilityScope.MyClasses : VisibilityScope.School;
                if (rules.Shows(StudentNickname, choices[p.PersonId], distance))
                {
                    result[p.Id] = new NameLabel(p.Nickname, $"{p.FirstName} {p.LastName}".Trim());
                }
            }
        }
        return result;
    }

    /// <summary>Labels of people with an account (message participants and
    /// senders), by user id: pupils as in <see cref="StudentsAsync(NameViewer, IEnumerable{int})"/>,
    /// a pupil's own parents by their nickname.</summary>
    public async Task<Dictionary<int, NameLabel>> UsersAsync(NameViewer viewer, IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<int, NameLabel>();
        if (ids.Count == 0 || viewer.IsAdmin)
        {
            return result;
        }

        var pupils = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Person!.UserId != null && ids.Contains(s.Person.UserId.Value))
            .Select(s => new { s.Id, UserId = s.Person!.UserId!.Value })
            .ToListAsync();
        if (pupils.Count > 0)
        {
            var labels = await StudentsAsync(viewer, pupils.Select(p => p.Id));
            foreach (var p in pupils)
            {
                if (labels.TryGetValue(p.Id, out var label))
                {
                    result[p.UserId] = label;
                }
            }
        }

        if (viewer.Kind == RoleKind.Student)
        {
            var parents = await db.ParentStudents.IgnoreQueryFilters()
                .Where(ps => ps.Student!.Person!.UserId == viewer.UserId
                    && ps.ParentProfile!.Person!.UserId != null && ids.Contains(ps.ParentProfile.Person.UserId.Value)
                    && ps.ParentProfile.Person.Nickname != null)
                .Select(ps => new
                {
                    UserId = ps.ParentProfile!.Person!.UserId!.Value, PersonId = ps.ParentProfile.PersonId,
                    Nickname = ps.ParentProfile.Person.Nickname!,
                })
                .Distinct()
                .ToListAsync();
            if (parents.Count == 0)
            {
                return result;
            }
            var rules = await VisibilityAsync(viewer.SchoolId);
            var choices = await ChoicesAsync(parents.Select(p => p.PersonId));
            foreach (var p in parents.Where(p => rules.Shows(ParentNickname, choices[p.PersonId], VisibilityScope.MyClasses)))
            {
                result[p.UserId] = new NameLabel(p.Nickname, null);
            }
        }
        return result;
    }

    /// <summary>The pupil's name as each recipient of a notification reads
    /// it — their parents and teachers each by their own settings, anyone
    /// else (the pupil included) by the full name.</summary>
    public async Task<Dictionary<int, string>> StudentNameForAsync(int studentId, IEnumerable<int> recipientIds)
    {
        var student = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.Person!.FirstName, s.Person.LastName, s.Person.UserId })
            .FirstAsync();
        var fullName = $"{student.FirstName} {student.LastName}".Trim();
        var parents = (await db.ParentStudents.IgnoreQueryFilters().Where(ps => ps.StudentId == studentId)
            .ParentAccountLinks().Select(l => l.UserId).ToListAsync()).ToHashSet();
        var staff = (await db.Users.IgnoreQueryFilters()
            .Where(u => recipientIds.Contains(u.Id) && u.Roles.Any(r => r.Kind == RoleKind.Staff))
            .Select(u => u.Id)
            .ToListAsync()).ToHashSet();

        var result = new Dictionary<int, string>();
        foreach (var userId in recipientIds.Distinct())
        {
            RoleKind? kind = parents.Contains(userId) ? RoleKind.Parent
                : staff.Contains(userId) && userId != student.UserId ? RoleKind.Staff
                : null;
            var viewer = kind is { } k ? await ViewerAsync(userId, k) : null;
            var label = viewer is null ? null : (await StudentsAsync(viewer, [studentId])).GetValueOrDefault(studentId);
            result[userId] = NameLabel.TextOf(label, fullName);
        }
        return result;
    }

    /// <summary>Pupils' names as their families read them, for many pupils at
    /// once (a published timetable): (student id, recipient's user id) → the
    /// name — a parent by nickname or first name, the pupil themselves (or
    /// anyone else) by the full name; null for a pupil who's gone.</summary>
    public async Task<Func<int, int, string?>> FamilyNamesAsync(IEnumerable<int> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var pupils = await db.Students.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Person!.FirstName, s.Person.LastName, s.Person.Nickname })
            .ToDictionaryAsync(s => s.Id);
        var links = (await db.ParentStudents.IgnoreQueryFilters().Where(ps => ids.Contains(ps.StudentId))
            .ParentAccountLinks().ToListAsync()).Select(l => (l.StudentId, l.UserId)).ToHashSet();
        var parentIds = links.Select(l => l.UserId).Distinct().ToList();
        var show = await db.Users.IgnoreQueryFilters()
            .Where(u => parentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.ShowStudentNicknames ?? true);
        return (studentId, userId) =>
        {
            if (!pupils.TryGetValue(studentId, out var p))
            {
                return null;
            }
            var full = $"{p.FirstName} {p.LastName}".Trim();
            if (!links.Contains((studentId, userId)))
            {
                return full;
            }
            return show.GetValueOrDefault(userId, true) && p.Nickname is not null ? p.Nickname
                : p.FirstName.Length > 0 ? p.FirstName : full;
        };
    }

    /// <summary>Someone with an account (a message's sender) as each
    /// recipient reads them: a pupil as in <see cref="StudentNameForAsync"/>,
    /// a parent by nickname to their own children.</summary>
    public async Task<Dictionary<int, string>> UserNameForAsync(int userId, string fullName, IEnumerable<int> recipientIds)
    {
        var recipients = recipientIds.Distinct().ToList();
        var studentId = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Person!.UserId == userId).Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (studentId is { } id)
        {
            return await StudentNameForAsync(id, recipients);
        }
        var result = recipients.ToDictionary(r => r, _ => fullName);
        var pupils = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Person!.UserId != null && recipients.Contains(s.Person.UserId.Value))
            .Select(s => s.Person!.UserId!.Value)
            .ToListAsync();
        foreach (var pupil in pupils)
        {
            if (await ViewerAsync(pupil, RoleKind.Student) is { } viewer
                && (await UsersAsync(viewer, [userId])).TryGetValue(userId, out var label))
            {
                result[pupil] = label.Text;
            }
        }
        return result;
    }

    private async Task<SchoolVisibility> VisibilityAsync(int schoolId)
    {
        if (!visibility.TryGetValue(schoolId, out var rules))
        {
            rules = await Visibility.LoadAsync(db, schoolId);
            visibility[schoolId] = rules;
        }
        return rules;
    }

    private async Task<HashSet<int>> TaughtAsync(int teacherId)
    {
        if (!taught.TryGetValue(teacherId, out var ids))
        {
            ids = await db.GetTeacherStudentIdsAsync(teacherId);
            taught[teacherId] = ids;
        }
        return ids;
    }

    private async Task<ILookup<int, VisibilityChoice>> ChoicesAsync(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        return (await db.VisibilityChoices.IgnoreQueryFilters().Where(c => ids.Contains(c.PersonId)).ToListAsync())
            .ToLookup(c => c.PersonId);
    }
}

/// <summary>Labels for the caller, where a controller lists pupils or people.</summary>
public static class NameLabelQueries
{
    public static Task<Dictionary<int, NameLabel>> StudentLabelsAsync(
        this AppDbContext db, ClaimsPrincipal viewer, IEnumerable<int> studentIds) =>
        new NameLabels(db).StudentsAsync(viewer, studentIds);

    public static Task<Dictionary<int, NameLabel>> UserLabelsAsync(
        this AppDbContext db, ClaimsPrincipal viewer, IEnumerable<int> userIds) =>
        new NameLabels(db).UsersAsync(viewer, userIds);

    /// <summary><paramref name="items"/> with their pupil's label set by <paramref name="with"/>.</summary>
    public static async Task<List<T>> WithStudentLabelsAsync<T>(
        this AppDbContext db, ClaimsPrincipal viewer, IEnumerable<T> items, Func<T, int?> studentId, Func<T, NameLabel?, T> with)
    {
        var list = items.ToList();
        var labels = await db.StudentLabelsAsync(viewer, list.Select(studentId).OfType<int>());
        return list.Select(i => with(i, studentId(i) is { } id ? labels.GetValueOrDefault(id) : null)).ToList();
    }
}
