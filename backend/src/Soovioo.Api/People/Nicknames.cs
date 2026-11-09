using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.People;

/// <summary>Setting and approving nicknames. A parent sets their own, and
/// their child's, right away; a pupil proposes one — it applies once a
/// parent approves it, or the school (class teacher, admin: whoever may edit
/// the pupil) when the pupil has no parent in the app. Teachers can't remove
/// one: who minds it switches nicknames off for themselves.</summary>
public class Nicknames(AppDbContext db, Notifier notifier, NameLabels names)
{
    public const int MaxLength = 30;

    /// <summary>Trimmed, inner spaces collapsed; empty = no nickname.</summary>
    public static (string? Value, string? Error) Normalize(string? input)
    {
        var value = string.Join(' ', (input ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (value.Length == 0)
        {
            return (null, null);
        }
        if (value.Length > MaxLength)
        {
            return (null, $"Přezdívka může mít nejvýš {MaxLength} znaků.");
        }
        if (!value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-'))
        {
            return (null, "Přezdívka může obsahovat jen písmena, číslice, mezery a pomlčky.");
        }
        return (value, null);
    }

    /// <summary>A pupil's own change: removing applies right away, a new one
    /// waits for approval (the current one stays until then). Caller saves.</summary>
    public async Task ProposeAsync(StudentProfile student, string? value)
    {
        var person = student.Person!;
        if (value is null)
        {
            person.Nickname = null;
            person.NicknameProposal = null;
        }
        else if (value == person.Nickname)
        {
            person.NicknameProposal = null;
        }
        else
        {
            person.NicknameProposal = value;
            await NotifyApproversAsync(student, value);
        }
        if (person.NicknameProposal is null)
        {
            await DropRequestsAsync(student.Id);
        }
    }

    /// <summary>A parent's choice for their child — approved as it is. Caller saves.</summary>
    public async Task SetAsync(StudentProfile student, string? value)
    {
        student.Person!.Nickname = value;
        student.Person.NicknameProposal = null;
        await DropRequestsAsync(student.Id);
    }

    /// <summary>Caller saves.</summary>
    public async Task DecideAsync(StudentProfile student, bool approve)
    {
        var person = student.Person!;
        if (approve && person.NicknameProposal is not null)
        {
            person.Nickname = person.NicknameProposal;
        }
        person.NicknameProposal = null;
        await DropRequestsAsync(student.Id);
    }

    /// <summary>Approves the pupil's proposals: their parents (as parents),
    /// or — when no parent of theirs uses the app — the school.</summary>
    public async Task<bool> CanApproveAsync(ClaimsPrincipal viewer, StudentProfile student)
    {
        if (viewer.ActiveKind() == RoleKind.Parent)
        {
            return await db.ChildIdsOf(viewer.UserId()).AnyAsync(id => id == student.Id);
        }
        return await IsSchoolApproverAsync(viewer, student.Id);
    }

    public async Task<bool> IsSchoolApproverAsync(ClaimsPrincipal viewer, int studentId) =>
        viewer.IsStaff()
        && !await HasParentsAsync(studentId)
        && await viewer.ReachesStudentAsync(db, Permission.Students, studentId);

    public Task<bool> HasParentsAsync(int studentId) =>
        db.ParentStudents.Where(ps => ps.StudentId == studentId).ParentAccountLinks().AnyAsync();

    /// <summary>Of <paramref name="studentIds"/>, those whose proposals the
    /// school decides — no parent in the app.</summary>
    public static async Task<HashSet<int>> WithoutParentsAsync(AppDbContext db, IEnumerable<int> studentIds)
    {
        var ids = studentIds.Distinct().ToList();
        var withParents = await db.ParentStudents.Where(ps => ids.Contains(ps.StudentId))
            .ParentAccountLinks().Select(l => l.StudentId).Distinct().ToListAsync();
        return ids.Except(withParents).ToHashSet();
    }

    private async Task NotifyApproversAsync(StudentProfile student, string proposal)
    {
        var parents = await db.ParentStudents.Where(ps => ps.StudentId == student.Id)
            .ParentAccountLinks().Select(l => l.UserId).Distinct().ToListAsync();
        var drafts = new List<NotificationDraft>();
        if (parents.Count > 0)
        {
            var named = await names.StudentNameForAsync(student.Id, parents);
            drafts.AddRange(parents.Select(userId => new NotificationDraft(
                userId, NotificationType.Nickname, "Přezdívka ke schválení",
                $"{named[userId]} si chce nastavit přezdívku „{proposal}“.", "my-children", student.Id, student.Id)));
        }
        else
        {
            var classTeacher = await db.SchoolClasses.Where(c => c.Id == student.SchoolClassId)
                .Select(c => c.ClassTeacherId).FirstOrDefaultAsync();
            var (recipients, page) = classTeacher is { } teacher
                ? (new List<int> { teacher }, "admin?group=class&section=students")
                : (await db.Users.Where(u => u.DeactivatedAt == null && u.Roles.Any(r => r.BuiltIn == UserRole.Admin))
                    .Select(u => u.Id).ToListAsync(), "admin?group=school&section=people");
            drafts.AddRange(recipients.Select(userId => new NotificationDraft(
                userId, NotificationType.Nickname, "Přezdívka ke schválení",
                $"{student.Person!.FullName} si chce nastavit přezdívku „{proposal}“. Rodiče v aplikaci nemá, schvaluje škola.",
                page, student.Id, student.Id)));
        }
        await notifier.AddAsync(student.SchoolId, drafts);
    }

    private async Task DropRequestsAsync(int studentId) =>
        db.Notifications.RemoveRange(await db.Notifications
            .Where(n => n.Type == NotificationType.Nickname && n.TargetId == studentId)
            .ToListAsync());
}

public record SetNicknameRequest(string? Nickname);
