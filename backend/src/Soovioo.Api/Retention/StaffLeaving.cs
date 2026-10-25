using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Retention;

public enum StaffLeaveReason
{
    /// <summary>Dohoda.</summary>
    Agreement,

    /// <summary>Výpověď.</summary>
    Notice,

    /// <summary>Konec smlouvy na dobu určitou.</summary>
    FixedTermEnd,

    /// <summary>Odchod do důchodu.</summary>
    Retirement,
    Other,
}

/// <summary>Who's told that a member of staff leaves — the school's default
/// in <see cref="RetentionPolicy.StaffLeaveNotices"/>, changeable per departure.</summary>
public enum StaffLeaveNoticeGroup
{
    /// <summary>Pupils and parents of the classes where they're the class teacher.</summary>
    LedClasses,

    /// <summary>Pupils and parents of every class they teach.</summary>
    TaughtClasses,

    /// <summary>The other staff.</summary>
    Colleagues,
}

public enum StaffHandoverKind
{
    /// <summary>Třídnictví of the class <see cref="StaffHandover.TargetId"/>.</summary>
    ClassTeacher,

    /// <summary>The leaver's lessons of the subject <see cref="StaffHandover.TargetId"/> in the timetable.</summary>
    Subject,
}

/// <summary>Who takes over what from a member of staff who leaves —
/// carried out the day after their last day (<see cref="StaffLeaving.ApplyDueAsync"/>).</summary>
public class StaffHandover
{
    public int Id { get; set; }
    public int SchoolId { get; set; }

    /// <summary>The one who leaves.</summary>
    public int UserId { get; set; }
    public User? User { get; set; }

    public StaffHandoverKind Kind { get; set; }
    public int TargetId { get; set; }

    public int SuccessorId { get; set; }
}

/// <summary>Ukončení pracovního poměru: the last day, the reason and a note
/// for the school only, the handover of třídnictví and lessons, and the
/// notice to whom the school wants. The account works through the last day;
/// the day after <see cref="ApplyDueAsync"/> hands things over and
/// deactivates it.</summary>
public class StaffLeaving(AppDbContext db, AccountEraser accounts, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<StaffLeaveReason, string> ReasonLabels =
        new Dictionary<StaffLeaveReason, string>
        {
            [StaffLeaveReason.Agreement] = "dohoda",
            [StaffLeaveReason.Notice] = "výpověď",
            [StaffLeaveReason.FixedTermEnd] = "konec smlouvy na dobu určitou",
            [StaffLeaveReason.Retirement] = "odchod do důchodu",
            [StaffLeaveReason.Other] = "jiný",
        };

    public static bool IsStaff(User user) =>
        user.HasKind(RoleKind.Staff);

    public async Task<StaffLeavePreviewDto> PreviewAsync(User user)
    {
        var handovers = await db.StaffHandovers.Where(h => h.UserId == user.Id).ToListAsync();
        int? Successor(StaffHandoverKind kind, int targetId) =>
            handovers.FirstOrDefault(h => h.Kind == kind && h.TargetId == targetId)?.SuccessorId;

        var led = await db.SchoolClasses.Where(c => c.ClassTeacherId == user.Id).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        var lessons = await db.TimetableEntries.Where(t => t.TeacherId == user.Id)
            .Select(t => new { t.SubjectId, Subject = t.Subject!.Name, Class = t.SchoolClass!.Name })
            .ToListAsync();
        var staff = await db.Users.Include(u => u.Person)
            .Where(u => u.Id != user.Id && u.DeactivatedAt == null && u.ErasedAt == null
                && u.Roles.Any(r => r.Kind == RoleKind.Staff))
            .ToListAsync();
        var policy = await RetentionJob.PolicyOfAsync(db, user.SchoolId!.Value);

        return new StaffLeavePreviewDto(
            user.LeftAt, user.LeaveReason, user.LeaveNote,
            led.Select(c => new LedClassDto(c.Id, c.Name, Successor(StaffHandoverKind.ClassTeacher, c.Id))).ToList(),
            lessons.GroupBy(l => new { l.SubjectId, l.Subject })
                .OrderBy(g => g.Key.Subject)
                .Select(g => new TaughtSubjectDto(
                    g.Key.SubjectId, g.Key.Subject, g.Count(), g.Select(l => l.Class).Distinct().Order().ToList(),
                    Successor(StaffHandoverKind.Subject, g.Key.SubjectId)))
                .ToList(),
            staff.Select(u => new StaffOptionDto(u.Id, PersonNames.Display(u))).OrderBy(s => s.Name).ToList(),
            user.LeftAt is null ? policy.StaffLeaveNotices : null);
    }

    /// <summary>Lessons the substitutes would get at a time they already teach.</summary>
    public async Task<List<HandoverConflictDto>> ConflictsAsync(int userId, IReadOnlyList<SubjectSubstituteDto> substitutes)
    {
        var result = new List<HandoverConflictDto>();
        if (substitutes.Count == 0)
        {
            return result;
        }
        var subjectIds = substitutes.Select(s => s.SubjectId).ToList();
        var substituteIds = substitutes.Select(s => s.SubstituteId).Distinct().ToList();
        var taken = await db.TimetableEntries
            .Where(t => t.TeacherId == userId && subjectIds.Contains(t.SubjectId))
            .Select(t => new { t.SubjectId, t.DayOfWeek, t.PeriodNumber, t.WeekParity, Class = t.SchoolClass!.Name })
            .ToListAsync();
        var busy = await db.TimetableEntries
            .Where(t => t.TeacherId != null && substituteIds.Contains(t.TeacherId.Value))
            .Select(t => new { TeacherId = t.TeacherId!.Value, t.DayOfWeek, t.PeriodNumber, t.WeekParity, Class = t.SchoolClass!.Name })
            .ToListAsync();
        var names = await db.Users.Include(u => u.Person).Where(u => substituteIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => PersonNames.Display(u));

        foreach (var lesson in taken)
        {
            var substituteId = substitutes.First(s => s.SubjectId == lesson.SubjectId).SubstituteId;
            // The substitute's own lessons, and lessons of other subjects handed to them.
            var theirs = busy.Where(b => b.TeacherId == substituteId)
                .Select(b => (b.DayOfWeek, b.PeriodNumber, b.WeekParity, b.Class))
                .Concat(taken.Where(o => o != lesson && substitutes.First(s => s.SubjectId == o.SubjectId).SubstituteId == substituteId)
                    .Select(o => (o.DayOfWeek, o.PeriodNumber, o.WeekParity, o.Class)));
            foreach (var other in theirs)
            {
                if (other.DayOfWeek == lesson.DayOfWeek && other.PeriodNumber == lesson.PeriodNumber
                    && Overlaps(other.WeekParity, lesson.WeekParity))
                {
                    result.Add(new HandoverConflictDto(
                        lesson.SubjectId, substituteId, names.GetValueOrDefault(substituteId) ?? "",
                        lesson.DayOfWeek, lesson.PeriodNumber, lesson.Class, other.Class));
                }
            }
        }
        // A pair of the leaver's lessons both handed to one substitute shows up twice.
        return result
            .DistinctBy(c => (c.SubstituteId, c.DayOfWeek, c.PeriodNumber, string.CompareOrdinal(c.Class, c.OtherClass) < 0 ? c.Class + c.OtherClass : c.OtherClass + c.Class))
            .OrderBy(c => c.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)c.DayOfWeek).ThenBy(c => c.PeriodNumber)
            .ToList();
    }

    private static bool Overlaps(WeekParity a, WeekParity b) =>
        a == WeekParity.Every || b == WeekParity.Every || a == b;

    /// <summary>Records (or changes) the departure and the handover; the
    /// caller has checked the request. Returns the conversations of the
    /// notice — the caller saves, then lets Notifier announce them.</summary>
    public async Task<List<Conversation>> SetAsync(User user, StaffLeaveRequest request, int senderId)
    {
        var kind = user.LeftAt is null ? StaffLeaveNoticeKind.Set
            : user.LeftAt != request.LastDay ? StaffLeaveNoticeKind.DateChanged
            : (StaffLeaveNoticeKind?)null;
        user.LeftAt = request.LastDay;
        user.LeaveReason = request.Reason;
        user.LeaveNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        db.StaffHandovers.RemoveRange(await db.StaffHandovers.Where(h => h.UserId == user.Id).ToListAsync());
        var schoolId = user.SchoolId!.Value;
        db.StaffHandovers.AddRange(
            (request.ClassTeachers ?? []).Select(c => new StaffHandover
            {
                SchoolId = schoolId, UserId = user.Id, Kind = StaffHandoverKind.ClassTeacher, TargetId = c.ClassId, SuccessorId = c.SuccessorId,
            })
            .Concat((request.Substitutes ?? []).Select(s => new StaffHandover
            {
                SchoolId = schoolId, UserId = user.Id, Kind = StaffHandoverKind.Subject, TargetId = s.SubjectId, SuccessorId = s.SubstituteId,
            })));

        return kind is { } k
            ? await NotifyAsync(user, k, request.Notices ?? [], request.ClassTeachers ?? [], senderId)
            : [];
    }

    public async Task<List<Conversation>> CancelAsync(User user, IReadOnlyList<StaffLeaveNoticeGroup> notices, int senderId)
    {
        var sent = await NotifyAsync(user, StaffLeaveNoticeKind.Cancelled, notices, [], senderId);
        user.LeftAt = null;
        user.LeaveReason = null;
        user.LeaveNote = null;
        db.StaffHandovers.RemoveRange(await db.StaffHandovers.Where(h => h.UserId == user.Id).ToListAsync());
        return sent;
    }

    /// <summary>The daily job: staff whose last day is over hand over what
    /// was chosen (where it's still theirs) and their accounts stop working.
    /// Saves.</summary>
    public async Task ApplyDueAsync(int schoolId)
    {
        var today = SchoolClock.Today(clock);
        var leaving = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == schoolId && u.LeftAt < today && u.DeactivatedAt == null && u.ErasedAt == null)
            .ToListAsync();
        foreach (var user in leaving)
        {
            var handovers = await db.StaffHandovers.IgnoreQueryFilters().Where(h => h.UserId == user.Id).ToListAsync();
            foreach (var h in handovers.Where(h => h.Kind == StaffHandoverKind.ClassTeacher))
            {
                var schoolClass = await db.SchoolClasses.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == h.TargetId && c.ClassTeacherId == user.Id);
                if (schoolClass is not null)
                {
                    schoolClass.ClassTeacherId = h.SuccessorId;
                }
            }
            var subjects = handovers.Where(h => h.Kind == StaffHandoverKind.Subject).ToDictionary(h => h.TargetId, h => h.SuccessorId);
            foreach (var entry in await db.TimetableEntries.IgnoreQueryFilters()
                         .Where(t => t.SchoolId == schoolId && t.TeacherId == user.Id).ToListAsync())
            {
                if (subjects.TryGetValue(entry.SubjectId, out var substitute))
                {
                    entry.TeacherId = substitute;
                }
            }
            db.StaffHandovers.RemoveRange(handovers);
            await accounts.DeactivateAsync(user);
        }
        await db.SaveChangesAsync();
    }

    private async Task<List<Conversation>> NotifyAsync(
        User user, StaffLeaveNoticeKind kind, IReadOnlyCollection<StaffLeaveNoticeGroup> groups,
        IReadOnlyList<ClassSuccessorDto> successors, int senderId)
    {
        var added = new List<Conversation>();
        if (groups.Count == 0)
        {
            return added;
        }
        var schoolId = user.SchoolId!.Value;
        var school = await db.Schools.FirstAsync(s => s.Id == schoolId);
        var sender = await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == senderId);
        var person = await db.Users.Include(u => u.Person).Where(u => u.Id == user.Id).Select(u => u.Person).FirstOrDefaultAsync();
        var name = PersonNames.Display(person, user.Username);
        var subject = $"Odchod ze školy — {name}";
        var now = clock.GetUtcNow();
        var text = kind switch
        {
            StaffLeaveNoticeKind.Set => $"{name} ve škole {school.Name} končí. Poslední den ve škole: {SchoolFormat.Date(user.LeftAt!.Value)}.",
            StaffLeaveNoticeKind.DateChanged => $"Mění se poslední den, kdy {name} ve škole {school.Name} působí: nově {SchoolFormat.Date(user.LeftAt!.Value)}.",
            _ => $"Odchod {name} ze školy {school.Name} byl zrušen, ve škole dál působí.",
        };

        var ledClassIds = await db.SchoolClasses.Where(c => c.ClassTeacherId == user.Id).Select(c => c.Id).ToListAsync();
        var classIds = new HashSet<int>();
        if (groups.Contains(StaffLeaveNoticeGroup.LedClasses))
        {
            classIds.UnionWith(ledClassIds);
        }
        if (groups.Contains(StaffLeaveNoticeGroup.TaughtClasses))
        {
            classIds.UnionWith(ledClassIds);
            classIds.UnionWith(await db.TimetableEntries.Where(t => t.TeacherId == user.Id).Select(t => t.SchoolClassId).ToListAsync());
        }
        var successorNotes = new Dictionary<int, string>();
        if (kind != StaffLeaveNoticeKind.Cancelled)
        {
            foreach (var s in successors)
            {
                var className = await db.SchoolClasses.Where(c => c.Id == s.ClassId).Select(c => c.Name).FirstOrDefaultAsync();
                var successor = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == s.SuccessorId);
                if (className is not null && successor is not null)
                {
                    successorNotes[s.ClassId] = $"Třídním učitelem třídy {className} bude {PersonNames.Display(successor)}.";
                }
            }
        }

        // A conversation per family, like a message to a whole class — parents don't see each other.
        var students = await db.Students.Where(s => classIds.Contains(s.SchoolClassId))
            .Select(s => new { s.Id, s.SchoolClassId }).ToListAsync();
        foreach (var student in students)
        {
            var family = (await db.FamilyUserIdsAsync(student.Id)).Where(id => id != senderId).ToList();
            family = await db.Users.Where(u => family.Contains(u.Id) && u.DeactivatedAt == null).Select(u => u.Id).ToListAsync();
            if (family.Count == 0)
            {
                continue;
            }
            var body = successorNotes.TryGetValue(student.SchoolClassId, out var note) ? $"{text}\n\n{note}" : text;
            added.Add(ConversationsController.NewConversation(
                subject, student.Id, senderId, PersonNames.Display(sender), family, body, schoolId, now));
        }

        if (groups.Contains(StaffLeaveNoticeGroup.Colleagues))
        {
            var colleagues = await db.Users
                .Where(u => u.Id != user.Id && u.Id != senderId && u.DeactivatedAt == null && u.ErasedAt == null
                    && u.Roles.Any(r => r.Kind == RoleKind.Staff))
                .Select(u => u.Id)
                .ToListAsync();
            if (colleagues.Count > 0)
            {
                var body = successorNotes.Count > 0 ? $"{text}\n\n{string.Join("\n", successorNotes.Values)}" : text;
                added.Add(ConversationsController.NewConversation(
                    subject, null, senderId, PersonNames.Display(sender), colleagues, body, schoolId, now));
            }
        }
        db.Conversations.AddRange(added);
        return added;
    }
}

public enum StaffLeaveNoticeKind
{
    Set,
    DateChanged,
    Cancelled,
}

public record ClassSuccessorDto(int ClassId, int SuccessorId);

public record SubjectSubstituteDto(int SubjectId, int SubstituteId);

/// <param name="LastDay">The last day the account works.</param>
/// <param name="Note">For the school's management only.</param>
/// <param name="Notices">Who's told (of a new departure, a new last day).</param>
public record StaffLeaveRequest(
    DateOnly LastDay,
    StaffLeaveReason? Reason = null,
    string? Note = null,
    IReadOnlyList<ClassSuccessorDto>? ClassTeachers = null,
    IReadOnlyList<SubjectSubstituteDto>? Substitutes = null,
    IReadOnlyList<StaffLeaveNoticeGroup>? Notices = null);

public record CancelStaffLeaveRequest(IReadOnlyList<StaffLeaveNoticeGroup>? Notices = null);

public record HandoverCheckRequest(IReadOnlyList<SubjectSubstituteDto> Substitutes);

public record LedClassDto(int ClassId, string Name, int? SuccessorId);

/// <param name="Lessons">Their lessons of the subject in the timetable (a week).</param>
public record TaughtSubjectDto(int SubjectId, string Name, int Lessons, IReadOnlyList<string> Classes, int? SubstituteId);

public record StaffOptionDto(int UserId, string Name);

/// <param name="NoticeDefaults">The school's choice of whom to tell — for a new departure only.</param>
public record StaffLeavePreviewDto(
    DateOnly? LastDay, StaffLeaveReason? Reason, string? Note,
    IReadOnlyList<LedClassDto> LedClasses, IReadOnlyList<TaughtSubjectDto> Subjects,
    IReadOnlyList<StaffOptionDto> Staff, IReadOnlyList<StaffLeaveNoticeGroup>? NoticeDefaults);

/// <param name="Class">The leaver's lesson handed over.</param>
/// <param name="OtherClass">What the substitute teaches at that time.</param>
public record HandoverConflictDto(
    int SubjectId, int SubstituteId, string SubstituteName, DayOfWeek DayOfWeek, int PeriodNumber, string Class, string OtherClass);
