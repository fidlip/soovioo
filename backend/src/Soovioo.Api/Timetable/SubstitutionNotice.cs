using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;

namespace Soovioo.Api.Timetable;

/// <summary>Tells whom a published day concerns: teachers whose lessons, and
/// pupils with their parents whose class's or group's lessons differ from
/// what was published before (the timetable, the first time). Each hears all
/// the day's changes that concern them as they are now.</summary>
public class SubstitutionNotice(AppDbContext db, Notifier notifier)
{
    /// <summary>The day as yyyyMMdd — what a notification is about.</summary>
    public static int Target(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private static string Signature(EffectiveLesson l) =>
        $"{l.SubjectId}|{l.TeacherId}|{l.ClassroomId}|{l.Gone}|{l.MergedInto?.Key}|{string.Join(",", l.MergedFrom.Select(m => m.Key))}";

    /// <summary>Adds the notifications; the caller saves.</summary>
    public async Task AddAsync(int schoolId, DateOnly date, List<EffectiveLesson> before, List<EffectiveLesson> after)
    {
        static Dictionary<(LessonKey, bool), string> Signatures(List<EffectiveLesson> lessons) =>
            lessons.GroupBy(l => (l.Key, l.MovedIn)).ToDictionary(g => g.Key, g => string.Join(";", g.Select(Signature)));
        var then = Signatures(before);
        var now = Signatures(after);
        var differs = then.Keys.Union(now.Keys).Where(k => then.GetValueOrDefault(k) != now.GetValueOrDefault(k)).ToHashSet();
        var changed = before.Concat(after).Where(l => differs.Contains((l.Key, l.MovedIn))).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var names = await LessonNames.LoadAsync(db, before.Concat(after));
        var ofDay = after.Where(l => l.Key.Date == date || l.Change?.Date == date).ToList();
        var title = $"Změny v rozvrhu na {LessonChangeViews.Day(date)}";
        string Body(LessonScope scope, bool who)
        {
            var lines = LessonChangeViews.For(ofDay, scope, names)
                .OrderBy(v => v.Date).ThenBy(v => v.PeriodNumber)
                .Select(v => $"{(v.Date == date ? "" : $"{LessonChangeViews.Day(v.Date)} ")}{v.PeriodNumber}. h"
                    + $"{(who ? $" {v.SchoolClassName}" : v.StudentGroupName is null ? "" : $" {v.StudentGroupName}")}"
                    + $" {v.SubjectAbbreviation ?? v.SubjectName}{(v.WasSubject is null ? "" : $" místo {v.WasSubject}")}: {What(v)}")
                .ToList();
            return lines.Count > 0 ? string.Join("; ", lines) + "." : "Hodiny jsou zase podle rozvrhu.";
        }
        static string What(LessonChangeDto v) => string.Join(", ", new[]
        {
            v.Label,
            v.WasTeacher is null ? "" : $"{v.TeacherName} místo {v.WasTeacher}",
            v.WasClassroom is null ? "" : $"učebna {v.ClassroomName} místo {v.WasClassroom}",
        }.Where(p => p.Length > 0));

        var drafts = changed.SelectMany(l => new[] { l.TeacherId, l.Original?.TeacherId })
            .Where(t => t is not null).Select(t => t!.Value).Distinct()
            .Select(t => new NotificationDraft(t, NotificationType.Substitution, title, Body(new LessonScope(TeacherId: t), true),
                "timetable", Target(date)))
            .ToList();

        var classIds = changed.Where(l => l.Key.GroupId is null).SelectMany(l => l.ClassIds).Distinct().ToList();
        var groupIds = changed.Where(l => l.Key.GroupId is not null).Select(l => l.Key.GroupId!.Value).Distinct().ToList();
        var students = await db.Students
            .Where(s => classIds.Contains(s.SchoolClassId) || db.StudentGroupMembers.Any(m => m.StudentId == s.Id && groupIds.Contains(m.StudentGroupId)))
            .Select(s => new { s.Id, s.SchoolClassId, s.Person!.FirstName, s.Person.LastName })
            .ToListAsync();
        var ids = students.Select(s => s.Id).ToList();
        var memberships = (await db.StudentGroupMembers.Where(m => ids.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.StudentGroupId }).ToListAsync())
            .ToLookup(m => m.StudentId, m => m.StudentGroupId);
        var families = await notifier.FamiliesAsync(ids);
        foreach (var student in students)
        {
            var scope = new LessonScope(ClassId: student.SchoolClassId, GroupIds: memberships[student.Id].ToHashSet());
            if (!changed.Any(l => Concerns(l, scope)))
            {
                continue;
            }
            var body = Body(scope, false);
            drafts.AddRange(families.GetValueOrDefault(student.Id, []).Select(user => new NotificationDraft(
                user, NotificationType.Substitution, $"{title} — {student.FirstName} {student.LastName}", body,
                "timetable", Target(date), student.Id)));
        }
        await notifier.AddAsync(schoolId, drafts);
    }

    private static bool Concerns(EffectiveLesson lesson, LessonScope scope) =>
        lesson.ClassIds.Contains(scope.ClassId ?? 0)
        && (lesson.Key.GroupId is null || scope.GroupIds!.Contains(lesson.Key.GroupId.Value));
}
