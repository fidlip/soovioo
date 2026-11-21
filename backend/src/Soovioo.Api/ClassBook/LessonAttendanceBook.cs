using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.ClassBook;

/// <summary>Who is in a lesson, who probably isn't, and what taking the
/// attendance does to Docházka: an absent pupil's absence of the day gets
/// the lesson's period, a pupil there loses it.</summary>
public class LessonAttendanceBook(AppDbContext db, Notifier notifier, TimeProvider clock, MessageAttachmentStorageService storage)
{
    /// <summary>The class's pupils, or the group's members; by name.</summary>
    public async Task<List<StudentProfile>> PupilsAsync(LessonKey key)
    {
        var pupils = key.GroupId is { } groupId
            ? db.Students.Where(s => db.StudentGroupMembers.Any(m => m.StudentGroupId == groupId && m.StudentId == s.Id))
            : db.Students.Where(s => s.SchoolClassId == key.ClassId);
        return await pupils.Include(s => s.Person)
            .OrderBy(s => s.Person!.LastName).ThenBy(s => s.Person!.FirstName)
            .ToListAsync();
    }

    /// <summary>Who is likely away, with why: an absence of the day already
    /// covering the lesson (an omluvenka in advance, Docházka), or the
    /// pupil's latest earlier lesson that day missed or left.</summary>
    public async Task<Dictionary<int, string>> SuggestedAbsentAsync(
        LessonKey key, IReadOnlyCollection<int> studentIds, bool excusesOnly = false)
    {
        var absences = await db.Absences.Include(a => a.Excuse)
            .Where(a => a.Date == key.Date && studentIds.Contains(a.StudentId) && (!excusesOnly || a.ExcuseId != null))
            .ToListAsync();
        var earlier = (await db.LessonAttendances
                .Where(a => a.LessonRecord!.Date == key.Date && a.LessonRecord.PeriodNumber < key.Period
                    && a.LessonRecord.AttendanceTakenAt != null && studentIds.Contains(a.StudentId))
                .Select(a => new { a.StudentId, a.Status, a.LessonRecord!.PeriodNumber })
                .ToListAsync())
            .ToLookup(a => a.StudentId);
        var result = new Dictionary<int, string>();
        foreach (var absence in absences)
        {
            if (absence.Periods.Count == 0 || absence.Periods.Contains(key.Period))
            {
                result[absence.StudentId] = absence.Excuse is { } excuse ? $"Omluvenka: {excuse.Reason}" : "Zapsaná absence";
            }
            else if (absence.Periods.Where(p => p < key.Period).DefaultIfEmpty(-1).Max() is var missed and >= 0)
            {
                result[absence.StudentId] = $"Chyběl(a) v {missed}. hodině";
            }
        }
        foreach (var studentId in studentIds.Where(id => !excusesOnly && !result.ContainsKey(id)))
        {
            if (earlier[studentId].MaxBy(a => a.PeriodNumber) is { Status: LessonAttendanceStatus.LeftEarly } left)
            {
                result[studentId] = $"Odešel(a) v {left.PeriodNumber}. hodině";
            }
        }
        return result;
    }

    /// <summary>Docházka after the lesson's attendance: the absent pupils'
    /// absence of the day has the period, the others' doesn't — an absence
    /// left without a period goes. Saves.</summary>
    public async Task SyncAbsencesAsync(LessonKey key, IReadOnlyCollection<StudentProfile> pupils, IReadOnlySet<int> absent, int userId)
    {
        var ids = pupils.Select(p => p.Id).ToList();
        var absences = await db.Absences.Where(a => a.Date == key.Date && ids.Contains(a.StudentId)).ToListAsync();
        var created = new List<(Absence, StudentProfile)>();
        var changed = new List<(Absence, StudentProfile)>();
        var gone = new List<Absence>();
        var excuses = new ExcuseBook(db, notifier, clock);
        foreach (var pupil in pupils)
        {
            var absence = absences.FirstOrDefault(a => a.StudentId == pupil.Id);
            if (absent.Contains(pupil.Id))
            {
                if (absence is null)
                {
                    absence = new Absence
                    {
                        SchoolId = pupil.SchoolId, StudentId = pupil.Id, Date = key.Date, Periods = [key.Period], CreatedById = userId,
                    };
                    db.Absences.Add(absence);
                    created.Add((absence, pupil));
                }
                else if (absence.Periods.Count > 0 && !absence.Periods.Contains(key.Period))
                {
                    absence.Periods = [.. absence.Periods.Append(key.Period).Order()];
                    changed.Add((absence, pupil));
                }
                continue;
            }
            if (absence is null)
            {
                continue;
            }
            List<int> periods;
            if (absence.Periods.Count == 0)
            {
                // The whole day without detail: all the pupil's lessons that day but this one.
                var timetable = await excuses.TimetablePeriodsAsync(pupil, key.Date, key.Date);
                periods = [.. timetable.GetValueOrDefault(key.Date, []).Where(p => p != key.Period)];
            }
            else if (absence.Periods.Contains(key.Period))
            {
                periods = [.. absence.Periods.Where(p => p != key.Period)];
            }
            else
            {
                continue;
            }
            if (periods.Count == 0)
            {
                db.Absences.Remove(absence);
                gone.Add(absence);
            }
            else
            {
                absence.Periods = periods;
                changed.Add((absence, pupil));
            }
        }
        await db.SaveChangesAsync();

        var notices = new AbsenceNotices(notifier);
        foreach (var (absence, pupil) in created)
        {
            await notices.NewAsync(absence, pupil);
        }
        foreach (var (absence, pupil) in changed)
        {
            await notices.ChangedAsync(absence, pupil);
        }
        await db.SaveChangesAsync();
        foreach (var excuseId in gone.Where(a => a.ExcuseId is not null).Select(a => a.ExcuseId!.Value).Distinct())
        {
            await excuses.DropIfEmptyAsync(excuseId, storage);
        }
    }
}
