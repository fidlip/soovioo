using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.People;

namespace Soovioo.Api.Retention;

public enum LeaveNoticeGroup
{
    /// <summary>The pupil's own account.</summary>
    Student,
    Parents,
    ClassTeacher,
}

/// <param name="WithReason">The message names the reason of the departure.</param>
public record LeaveNotice(LeaveNoticeGroup Group, bool WithReason = false);

public enum LeaveNoticeKind
{
    Set,
    DateChanged,
    Cancelled,
}

/// <summary>Tells the people concerned that a pupil's education at the
/// school ends (or that it changed): a message in Zprávy from the admin who
/// recorded it, tied to the pupil. Whoever gets the reason gets a
/// conversation of their own, so it never reaches the others.</summary>
public class LeaveNotifier(AppDbContext db, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<StudentLeaveReason, string> ReasonLabels =
        new Dictionary<StudentLeaveReason, string>
        {
            [StudentLeaveReason.Transfer] = "přestup na jinou školu",
            [StudentLeaveReason.Graduated] = "řádné ukončení vzdělávání",
            [StudentLeaveReason.Expelled] = "vyloučení ze školy",
            [StudentLeaveReason.Other] = "jiný",
        };

    /// <summary>Adds the conversations and returns them; the caller saves
    /// and then notifies (<see cref="Notifications.Notifier.MessagesAsync"/>).</summary>
    public async Task<List<Conversation>> NotifyAsync(
        Student student, LeaveNoticeKind kind, IReadOnlyCollection<LeaveNotice> notices, int senderId)
    {
        var added = new List<Conversation>();
        if (notices.Count == 0)
        {
            return added;
        }
        var schoolId = student.SchoolId;
        var school = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == schoolId);
        var sender = await db.Users.IgnoreQueryFilters().Include(u => u.Person).FirstAsync(u => u.Id == senderId);

        var withReason = new HashSet<int>();
        var withoutReason = new HashSet<int>();
        foreach (var notice in notices.DistinctBy(n => n.Group))
        {
            foreach (var userId in await RecipientsAsync(student, notice.Group))
            {
                (notice.WithReason ? withReason : withoutReason).Add(userId);
            }
        }
        withoutReason.ExceptWith(withReason);
        withReason.Remove(senderId);
        withoutReason.Remove(senderId);

        var grace = (await RetentionJob.PolicyOfAsync(db, schoolId)).AccountGraceDays;
        var now = clock.GetUtcNow();
        var subject = StudentTerm.Localize($"Ukončení vzdělávání — {student.FirstName} {student.LastName}", school.Type);
        foreach (var (recipients, reason) in new[] { (withReason, true), (withoutReason, false) })
        {
            if (recipients.Count == 0)
            {
                continue;
            }
            var body = StudentTerm.Localize(Body(student, school.Name, kind, reason, grace), school.Type);
            var conversation = ConversationsController.NewConversation(
                subject, student.Id, senderId, PersonNames.Display(sender), recipients, body, schoolId, now);
            db.Conversations.Add(conversation);
            added.Add(conversation);
        }
        return added;
    }

    private async Task<List<int>> RecipientsAsync(Student student, LeaveNoticeGroup group)
    {
        var schoolId = student.SchoolId;
        var ids = group switch
        {
            LeaveNoticeGroup.Student => student.UserId is { } own ? [own] : [],
            LeaveNoticeGroup.Parents => await db.ParentStudents.IgnoreQueryFilters()
                .Where(ps => ps.SchoolId == schoolId && ps.StudentId == student.Id && ps.ParentProfile!.Person!.UserId != null)
                .Select(ps => ps.ParentProfile!.Person!.UserId!.Value)
                .ToListAsync(),
            LeaveNoticeGroup.ClassTeacher => await db.SchoolClasses.IgnoreQueryFilters()
                .Where(c => c.SchoolId == schoolId && c.Id == student.SchoolClassId && c.ClassTeacherId != null)
                .Select(c => c.ClassTeacherId!.Value)
                .ToListAsync(),
            _ => [],
        };
        // Only accounts that still work.
        return await db.Users.IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id) && u.DeactivatedAt == null)
            .Select(u => u.Id)
            .ToListAsync();
    }

    public static string Body(Student student, string schoolName, LeaveNoticeKind kind, bool withReason, int graceDays)
    {
        var name = $"{student.FirstName} {student.LastName}";
        if (kind == LeaveNoticeKind.Cancelled)
        {
            return $"Ukončení vzdělávání žáka {name} ve škole {schoolName} bylo zrušeno, žák ve škole pokračuje.";
        }

        var lastDay = student.LeftAt!.Value;
        var lines = new List<string>
        {
            kind == LeaveNoticeKind.Set
                ? $"Vzdělávání žáka {name} ve škole {schoolName} končí. Poslední den ve škole: {Format(lastDay)}."
                : $"Mění se datum ukončení vzdělávání žáka {name} ve škole {schoolName}. Poslední den ve škole je nově {Format(lastDay)}.",
        };
        if (withReason && student.LeaveReason is { } reason)
        {
            lines.Add(reason == StudentLeaveReason.Other && !string.IsNullOrWhiteSpace(student.LeaveReasonDetail)
                ? $"Důvod: {student.LeaveReasonDetail}"
                : $"Důvod: {ReasonLabels[reason]}.");
        }
        lines.Add($"Účty žáka a jeho rodičů budou fungovat do {Format(lastDay.AddDays(graceDays))}.");
        return string.Join("\n\n", lines);
    }

    private static string Format(DateOnly date) => $"{date.Day}. {date.Month}. {date.Year}";
}
