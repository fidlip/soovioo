using Soovioo.Api.Common;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Attendance;

/// <summary>What the family hears of a recorded absence. Adds the
/// notifications; the caller saves (the absence must have its id).</summary>
public class AbsenceNotices(Notifier notifier)
{
    public const string NewTitle = "Zapsaná absence";

    public async Task NewAsync(Absence absence, StudentProfile student) =>
        await notifier.AddAsync(absence.SchoolId, await DraftsAsync(absence, student, NewTitle, await notifier.FamilyAsync(student.Id)));

    public async Task ChangedAsync(Absence absence, StudentProfile student)
    {
        var family = await notifier.FamilyAsync(student.Id);
        await notifier.ChangedAsync(absence.SchoolId, absence.Id, [NotificationType.Absence], family,
            await DraftsAsync(absence, student, "Změna absence", family), NewTitle);
    }

    private async Task<List<NotificationDraft>> DraftsAsync(
        Absence absence, StudentProfile student, string title, IEnumerable<int> family)
    {
        var periods = absence.Periods.Count > 0 ? $", hodiny {string.Join(", ", absence.Periods)}" : "";
        var names = await notifier.StudentNamesAsync(student.Id, family);
        return names
            .Select(n => new NotificationDraft(
                n.Key, NotificationType.Absence, title, $"{n.Value} — {SchoolFormat.Date(absence.Date)}{periods}",
                "absences", absence.Id, student.Id))
            .ToList();
    }
}
