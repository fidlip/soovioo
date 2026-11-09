using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Tells whom a published version concerns: pupils with their parents
/// and teachers whose lessons differ from the version it replaces, with the
/// lessons added and removed. The first version of a school year (or the first
/// with any lessons) has nothing to compare with — everyone with a lesson in
/// it hears about it. Sent at the day's end and compared when sending, so that
/// it reflects the versions as they are by then.</summary>
public class TimetableChangeNotice(AppDbContext db, TimeProvider clock)
{
    /// <summary>Of those added and of those removed, the rest is summed up.</summary>
    public const int MaxLessons = 8;

    private static readonly Dictionary<DayOfWeek, string> Days = new()
    {
        [DayOfWeek.Monday] = "Po", [DayOfWeek.Tuesday] = "Út", [DayOfWeek.Wednesday] = "St", [DayOfWeek.Thursday] = "Čt",
        [DayOfWeek.Friday] = "Pá", [DayOfWeek.Saturday] = "So", [DayOfWeek.Sunday] = "Ne",
    };

    private record Lesson(
        int ClassId, string ClassName, int? GroupId, string? GroupName, string Subject, DayOfWeek Day, int Period,
        WeekParity Parity, int? TeacherId, string? Teacher, string? Classroom);

    /// <summary>The day's end for one version, however it changed during the
    /// day: who heard of it and it's off now hears that, what's planned for
    /// today goes out, and so do the day's changes of pupils in its groups.
    /// Saves.</summary>
    public async Task SendDueAsync(TimetableVersion version)
    {
        var today = SchoolClock.Today(clock);
        var told = await db.Notifications
            .Where(n => n.Type == NotificationType.TimetableChange && n.TargetId == version.Id)
            .ToListAsync();
        var groupChanges = await db.TimetableGroupChanges.Include(c => c.StudentGroup)
            .Where(c => c.StudentGroup!.TimetableVersionId == version.Id)
            .ToListAsync();
        var published = version.Status == TimetableVersionStatus.Published;
        var noticeAhead = version.ChangeNoticeOn > today;
        var comparesPupils = version.ChangeNoticeOn is not null;

        if (told.Count > 0 && (!published || (noticeAhead && version.ChangeNoticeSentAt is not null)))
        {
            await TellItsOffAsync(version, told);
            told = [];
        }
        if (published && version.ChangeNoticeOn <= today)
        {
            await SendChangeAsync(version, told);
        }
        if (published)
        {
            await SendGroupChangesAsync(version, groupChanges, comparesPupils);
        }
        db.TimetableGroupChanges.RemoveRange(groupChanges);
        await db.SaveChangesAsync();
    }

    /// <summary>Whoever heard before and is left out now, the change no longer
    /// concerning them, is told so: what they know isn't true any more.</summary>
    private async Task SendChangeAsync(TimetableVersion version, List<Notification> told)
    {
        db.Notifications.RemoveRange(told);
        await db.SaveChangesAsync();

        var previous = await db.TimetableVersions
            .Where(v => v.SchoolId == version.SchoolId && v.Status == TimetableVersionStatus.Published && v.ValidFrom < version.ValidFrom)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync();
        var lessons = await LessonsAsync(version.Id);
        List<Lesson> before = previous is null ? [] : await LessonsAsync(previous.Id);
        var fresh = before.Count == 0
            || TimetableVersions.SchoolYearStart(previous!.ValidFrom) != TimetableVersions.SchoolYearStart(version.ValidFrom);

        var from = SchoolFormat.Date(version.ValidFrom);
        var title = fresh ? $"Nový rozvrh od {from}" : $"Změna rozvrhu od {from}";
        var freshBody = $"Škola zveřejnila rozvrh „{version.Name}“.";
        string? Body(List<string> now, List<string> then) =>
            fresh ? (now.Count > 0 ? freshBody : null) : Summary(now, then);

        var drafts = new List<NotificationDraft>();
        var teachers = (fresh ? lessons : lessons.Concat(before)).Where(l => l.TeacherId != null).Select(l => l.TeacherId!.Value).Distinct();
        foreach (var teacher in teachers)
        {
            var body = Body(
                [.. lessons.Where(l => l.TeacherId == teacher).Select(ForTeacher).Distinct()],
                [.. before.Where(l => l.TeacherId == teacher).Select(ForTeacher).Distinct()]);
            if (body is not null)
            {
                drafts.Add(new NotificationDraft(teacher, NotificationType.TimetableChange, title, body, "timetable", version.Id));
            }
        }

        var memberships = await MembershipsAsync(version.Id, previous?.Id);
        var pupils = await db.Students.Where(s => s.SchoolId == version.SchoolId)
            .Select(s => new { s.Id, s.SchoolClassId })
            .ToListAsync();
        var changed = new List<(int StudentId, string Body)>();
        foreach (var pupil in pupils)
        {
            var groups = memberships[pupil.Id].ToHashSet();
            bool Theirs(Lesson l) => l.GroupId is { } g ? groups.Contains(g) : l.ClassId == pupil.SchoolClassId;
            var body = Body(
                [.. lessons.Where(Theirs).Select(ForPupil).Distinct()],
                [.. before.Where(Theirs).Select(ForPupil).Distinct()]);
            if (body is not null)
            {
                changed.Add((pupil.Id, body));
            }
        }
        var notifier = new Notifier(db, clock);
        var families = await notifier.FamiliesAsync(changed.Select(c => c.StudentId));
        var named = await notifier.FamilyNamesAsync(changed.Select(c => c.StudentId));
        drafts.AddRange(changed.SelectMany(c => families.GetValueOrDefault(c.StudentId, []).Select(user => new NotificationDraft(
            user, NotificationType.TimetableChange, $"{title} — {named(c.StudentId, user)}", c.Body, "timetable", version.Id, c.StudentId))));

        var hearing = drafts.Select(d => (d.UserId, d.StudentId)).ToHashSet();
        drafts.AddRange(await WithoutTargetAsync(
            told.Select(n => (n.UserId, n.StudentId)).Where(n => !hearing.Contains(n)),
            $"Rozvrh od {from} se upravil",
            $"Škola upravila rozvrh „{version.Name}“. Proti dosavadnímu rozvrhu se vám nic nemění."));

        await notifier.AddAsync(version.SchoolId, drafts);
        version.ChangeNoticeOn = null;
        version.ChangeNoticeSentAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
    }

    private async Task TellItsOffAsync(TimetableVersion version, List<Notification> told)
    {
        db.Notifications.RemoveRange(told);
        await db.SaveChangesAsync();
        var from = SchoolFormat.Date(version.ValidFrom);
        await new Notifier(db, clock).AddAsync(version.SchoolId, await WithoutTargetAsync(
            told.Select(n => (n.UserId, n.StudentId)),
            $"Rozvrh od {from} se ruší",
            $"Škola zrušila rozvrh „{version.Name}“, který měl platit od {from}. Neřiďte se jím."));
        version.ChangeNoticeSentAt = null;
        await db.SaveChangesAsync();
    }

    /// <summary>Corrections have no target: the rows with one are who heard of
    /// the version last, for the next correction.</summary>
    private async Task<List<NotificationDraft>> WithoutTargetAsync(
        IEnumerable<(int UserId, int? StudentId)> recipients, string title, string body)
    {
        var distinct = recipients.Distinct().ToList();
        var pupilIds = distinct.Where(r => r.StudentId != null).Select(r => r.StudentId!.Value).Distinct().ToList();
        var names = await new Notifier(db, clock).FamilyNamesAsync(pupilIds);
        return [.. distinct.Select(r => new NotificationDraft(
            r.UserId, NotificationType.TimetableChange,
            r.StudentId is { } id && names(id, r.UserId) is { } name ? $"{title} — {name}" : title,
            body, "timetable", null, r.StudentId))];
    }

    /// <summary>The version won't apply: nothing planned goes out. Who heard
    /// hears at the day's end, unless it's published again by then. Saves.</summary>
    public async Task WithdrawAsync(TimetableVersion version)
    {
        version.ChangeNoticeOn = null;
        db.TimetableGroupChanges.RemoveRange(await db.TimetableGroupChanges
            .Where(c => c.StudentGroup!.TimetableVersionId == version.Id)
            .ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>Remembered for the day's end; joining and leaving again the
    /// same day is no change at all. Saves.</summary>
    public async Task RecordGroupChangesAsync(
        TimetableVersion version, StudentGroup group, IReadOnlyList<int> joined, IReadOnlyList<int> left)
    {
        if (version.Status != TimetableVersionStatus.Published)
        {
            return;
        }
        var waiting = await db.TimetableGroupChanges.Where(c => c.StudentGroupId == group.Id).ToListAsync();
        foreach (var (studentId, joins) in joined.Select(id => (id, true)).Concat(left.Select(id => (id, false))))
        {
            if (waiting.FirstOrDefault(c => c.StudentId == studentId) is { } opposite)
            {
                db.TimetableGroupChanges.Remove(opposite);
            }
            else
            {
                db.TimetableGroupChanges.Add(new TimetableGroupChange
                {
                    SchoolId = version.SchoolId, StudentGroupId = group.Id, StudentId = studentId, Joined = joins,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>To the pupil's family, the lessons they gain or lose if the
    /// group has any. A notice comparing the pupils (<paramref name="comparesPupils"/>)
    /// has those lessons already. An unread notice about the version gets this
    /// added, as it's one per child.</summary>
    private async Task SendGroupChangesAsync(TimetableVersion version, List<TimetableGroupChange> changes, bool comparesPupils)
    {
        if (changes.Count == 0)
        {
            return;
        }
        var lessons = await LessonsAsync(version.Id);
        var byPupil = changes
            .Select(c =>
            {
                List<string> own = [.. lessons.Where(l => l.GroupId == c.StudentGroupId).Select(ForPupil).Distinct()];
                var said = c.Joined ? $"Zařazení do skupiny {c.StudentGroup!.Name}." : $"Vyřazení ze skupiny {c.StudentGroup!.Name}.";
                var summary = c.Joined ? Summary(own, []) : Summary([], own);
                return (c.StudentId, Covered: comparesPupils && own.Count > 0, Text: summary is null ? said : $"{said} {summary}");
            })
            .Where(c => !c.Covered)
            .GroupBy(c => c.StudentId, c => c.Text)
            .ToDictionary(g => g.Key, g => string.Join(" ", g));
        if (byPupil.Count == 0)
        {
            return;
        }

        var pupilIds = byPupil.Keys.ToList();
        var unread = await db.Notifications
            .Where(n => n.Type == NotificationType.TimetableChange && n.TargetId == version.Id && n.ReadAt == null && n.StudentId != null)
            .ToListAsync();
        var title = version.ValidFrom > SchoolClock.Today(clock)
            ? $"Změna rozvrhu od {SchoolFormat.Date(version.ValidFrom)}"
            : "Změna rozvrhu";
        var notifier = new Notifier(db, clock);
        var families = await notifier.FamiliesAsync(pupilIds);
        var names = await notifier.FamilyNamesAsync(pupilIds);
        await notifier.AddAsync(version.SchoolId, [.. byPupil.SelectMany(c => families.GetValueOrDefault(c.Key, []).Select(user =>
        {
            var earlier = unread.FirstOrDefault(n => n.UserId == user && n.StudentId == c.Key);
            return new NotificationDraft(
                user, NotificationType.TimetableChange, earlier?.Title ?? $"{title} — {names(c.Key, user)}",
                earlier?.Body is { } body ? $"{body} {c.Value}" : c.Value, "timetable", version.Id, c.Key);
        }))]);
    }

    private async Task<List<Lesson>> LessonsAsync(int versionId) =>
        (await db.TimetableEntries.Where(e => e.TimetableVersionId == versionId)
            .Select(e => new Lesson(
                e.SchoolClassId, e.SchoolClass!.Name, e.StudentGroupId, e.StudentGroup == null ? null : e.StudentGroup.Name,
                e.Subject!.Abbreviation ?? e.Subject.Name, e.DayOfWeek, e.PeriodNumber, e.WeekParity, e.TeacherId,
                e.Teacher == null ? null : PersonNames.Display(e.Teacher.Person, e.Teacher.Username),
                e.Classroom == null ? null : e.Classroom.ShortName ?? e.Classroom.Name))
            .ToListAsync())
        .OrderBy(l => l.Day).ThenBy(l => l.Period).ToList();

    private async Task<ILookup<int, int>> MembershipsAsync(int versionId, int? previousId) =>
        (await db.StudentGroups.Where(g => g.TimetableVersionId == versionId || g.TimetableVersionId == previousId)
            .SelectMany(g => g.Members.Select(m => new { m.StudentId, m.StudentGroupId }))
            .ToListAsync())
        .ToLookup(m => m.StudentId, m => m.StudentGroupId);

    private static string Slot(Lesson l)
    {
        var parity = l.Parity switch
        {
            WeekParity.Odd => " (lichý)",
            WeekParity.Even => " (sudý)",
            _ => "",
        };
        return $"{Days[l.Day]}{parity} {l.Period}. {l.Subject}";
    }

    private static string Details(params string?[] parts) =>
        string.Join(", ", parts.Where(p => p is not null)) is { Length: > 0 } details ? $" ({details})" : "";

    private static string ForPupil(Lesson l) =>
        Slot(l) + (l.GroupName is null ? "" : $" {l.GroupName}") + Details(l.Teacher, l.Classroom);

    private static string ForTeacher(Lesson l) =>
        $"{Slot(l)} {l.GroupName ?? l.ClassName}" + Details(l.Classroom);

    /// <summary>Null = no difference.</summary>
    public static string? Summary(IReadOnlyCollection<string> now, IReadOnlyCollection<string> then)
    {
        var parts = new[] { ("Přibylo", now.Except(then).ToList()), ("Ubylo", then.Except(now).ToList()) }
            .Where(p => p.Item2.Count > 0)
            .Select(p => $"{p.Item1}: {string.Join(", ", p.Item2.Take(MaxLessons))}"
                + (p.Item2.Count > MaxLessons ? $" a další ({p.Item2.Count - MaxLessons})" : "") + ".")
            .ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}

/// <summary>Hangfire job at the end of each day (registered in Program.cs):
/// everything about the timetable that day comes out as one notice, however
/// many times it changed. Each version in a context of its school, as with no
/// signed-in user there is none.</summary>
public class TimetableChangeNoticeJob(
    AppDbContext db, DbContextOptions<AppDbContext> options, TimeProvider clock, ILogger<TimetableChangeNoticeJob> logger)
{
    /// <summary>Prague time: before the default night quiet of pushes, so that
    /// a change starting tomorrow is still heard of tonight.</summary>
    public const string Schedule = "0 20 * * *";

    public async Task RunAsync()
    {
        var today = SchoolClock.Today(clock);
        var due = await db.TimetableVersions.IgnoreQueryFilters()
            .Where(v => (v.Status == TimetableVersionStatus.Published && v.ChangeNoticeOn != null && v.ChangeNoticeOn <= today)
                || db.TimetableGroupChanges.IgnoreQueryFilters().Any(c => c.StudentGroup!.TimetableVersionId == v.Id)
                || ((v.Status != TimetableVersionStatus.Published || (v.ChangeNoticeOn > today && v.ChangeNoticeSentAt != null))
                    && db.Notifications.IgnoreQueryFilters().Any(n => n.Type == NotificationType.TimetableChange && n.TargetId == v.Id)))
            .Select(v => new { v.Id, v.SchoolId })
            .ToListAsync();
        foreach (var item in due)
        {
            await using var schoolDb = new AppDbContext(options, new Auth.FixedSchoolContext(item.SchoolId)) { Today = today };
            try
            {
                await new TimetableChangeNotice(schoolDb, clock)
                    .SendDueAsync(await schoolDb.TimetableVersions.FirstAsync(v => v.Id == item.Id));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The notice about timetable version {VersionId} wasn't sent.", item.Id);
            }
        }
    }
}
