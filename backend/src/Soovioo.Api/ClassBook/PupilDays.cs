using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Data;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.ClassBook;

/// <summary>How the třídní kniha has a pupil in a lesson of theirs.</summary>
public enum PupilLessonState
{
    NotRecorded,
    Present,
    Absent,
    Late,
    LeftEarly,
    Away,
}

public record PupilLessonDto(int PeriodNumber, string Subject, PupilLessonState State);

/// <summary>Whether the school will approve an omluvenka's day whose
/// lessons aren't all written in the třídní kniha.</summary>
public enum UnverifiedExcuseApproval
{
    /// <summary>The teacher is asked to confirm.</summary>
    Warn,
    Block,
}

/// <summary>A pupil's lessons on a day with what the třídní kniha says of
/// them — what backs an omluvenka's day. The day is verified once every
/// lesson has its attendance taken.</summary>
public static class PupilDays
{
    public static bool Verified(IReadOnlyCollection<PupilLessonDto> lessons) =>
        lessons.All(l => l.State != PupilLessonState.NotRecorded);

    public static async Task<Dictionary<(int StudentId, DateOnly Date), List<PupilLessonDto>>> LessonsAsync(
        AppDbContext db, IReadOnlyCollection<(int StudentId, DateOnly Date)> days)
    {
        var result = new Dictionary<(int, DateOnly), List<PupilLessonDto>>();
        if (days.Count == 0)
        {
            return result;
        }
        var from = days.Min(d => d.Date);
        var to = days.Max(d => d.Date);
        var dates = days.Select(d => d.Date).Distinct().ToList();
        var studentIds = days.Select(d => d.StudentId).Distinct().ToList();
        var lessons = (await LessonChanges.OnAsync(db, from, to)).Where(l => !l.Gone && dates.Contains(l.Key.Date)).ToList();
        var names = await LessonNames.LoadAsync(db, lessons);
        var classes = await db.Students.Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SchoolClassId);
        var groups = (await db.StudentGroupMembers.Where(m => studentIds.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.StudentGroupId }).ToListAsync())
            .ToLookup(m => m.StudentId, m => m.StudentGroupId);
        var records = await db.LessonRecords.Include(r => r.Attendance)
            .Where(r => dates.Contains(r.Date) && r.AttendanceTakenAt != null)
            .ToListAsync();
        foreach (var (studentId, date) in days.Distinct())
        {
            if (!classes.TryGetValue(studentId, out var classId))
            {
                continue;
            }
            var pupilGroups = groups[studentId].ToHashSet();
            result[(studentId, date)] = [.. lessons
                .Where(l => l.Key.Date == date
                    && (l.Key.GroupId is { } g ? pupilGroups.Contains(g) : l.Key.ClassId == classId))
                .OrderBy(l => l.Key.Period)
                .Select(l =>
                {
                    var record = records.FirstOrDefault(r => r.Key == l.Key);
                    var state = record is null ? PupilLessonState.NotRecorded
                        : record.Attendance.FirstOrDefault(a => a.StudentId == studentId)?.Status switch
                        {
                            null => PupilLessonState.Present,
                            LessonAttendanceStatus.Absent => PupilLessonState.Absent,
                            LessonAttendanceStatus.Late => PupilLessonState.Late,
                            LessonAttendanceStatus.LeftEarly => PupilLessonState.LeftEarly,
                            _ => PupilLessonState.Away,
                        };
                    return new PupilLessonDto(l.Key.Period, names.Subject(l.SubjectId), state);
                })];
        }
        return result;
    }

    /// <summary>Why approving these days is refused: a day whose lessons
    /// aren't all written, when the school blocks that; null otherwise.</summary>
    public static async Task<string?> BlockedAsync(AppDbContext db, IReadOnlyCollection<Absence> days)
    {
        if (days.Count == 0 || (await db.Schools.FirstAsync(s => s.Id == days.First().SchoolId)).UnverifiedExcuseApproval != UnverifiedExcuseApproval.Block)
        {
            return null;
        }
        var lessons = await LessonsAsync(db, [.. days.Select(d => (d.StudentId, d.Date))]);
        var unverified = days
            .Where(d => lessons.TryGetValue((d.StudentId, d.Date), out var l) && !Verified(l))
            .OrderBy(d => d.Date)
            .Select(d => Common.SchoolFormat.Date(d.Date))
            .ToList();
        return unverified.Count == 0 ? null
            : $"Schválit jde až po zapsání všech hodin v třídní knize. Bez zapsaných hodin: {string.Join(", ", unverified)}.";
    }
}
