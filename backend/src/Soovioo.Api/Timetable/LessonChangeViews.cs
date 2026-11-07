namespace Soovioo.Api.Timetable;

/// <summary>A changed lesson as one class, pupil or teacher sees it.</summary>
/// <param name="SchoolClassId">With <paramref name="StudentGroupId"/> which lesson of the timetable it is.</param>
/// <param name="Replaces">Shown instead of that lesson; otherwise besides the timetable's.</param>
/// <param name="Gone">The lesson isn't taught (to them) then.</param>
public record LessonChangeDto(
    DateOnly Date, DayOfWeek DayOfWeek, int PeriodNumber, int? SchoolClassId, int? StudentGroupId,
    bool Replaces, bool Gone, string Label,
    int SubjectId, string SubjectName, string? SubjectAbbreviation,
    int? TeacherId, string? TeacherName, int? ClassroomId, string? ClassroomName,
    string SchoolClassName, string? StudentGroupName);

/// <summary>A class (all its groups' lessons), a pupil (the class and their
/// <see cref="GroupIds"/>) or a teacher.</summary>
public record LessonScope(int? ClassId = null, int? TeacherId = null, IReadOnlySet<int>? GroupIds = null);

public static class LessonChangeViews
{
    private static readonly string[] Days = ["ne", "po", "út", "st", "čt", "pá", "so"];

    /// <summary>"út 10. 11."</summary>
    public static string Day(DateOnly date) => $"{Days[(int)date.DayOfWeek]} {date.Day}. {date.Month}.";

    private static string At(LessonKey to, DateOnly from) =>
        to.Date == from ? $"{to.Period}. hodinu" : $"{Day(to.Date)}, {to.Period}. hodinu";

    /// <param name="substituteFor">Of the teacher substituting.</param>
    public static string Label(EffectiveLesson lesson, LessonNames names, bool substituteFor = false)
    {
        if (lesson.Change is { Cancelled: true })
        {
            return "Odpadá";
        }
        if (lesson.Gone)
        {
            return $"Přesunuto na {At(lesson.Change!.MovedTo!, lesson.Key.Date)}";
        }
        if (lesson.MergedInto is { } target)
        {
            return $"Spojeno s {names.Who(target.Key)}";
        }
        var parts = new List<string>();
        if (lesson.MovedIn)
        {
            var from = lesson.Original!.Key;
            parts.Add(from.Date == lesson.Key.Date
                ? $"Přesunuto z {from.Period}. hodiny"
                : $"Přesunuto z {Day(from.Date)}, {from.Period}. hodiny");
        }
        if (lesson.Original is null)
        {
            parts.Add("Mimořádná hodina");
        }
        if (lesson.MergedFrom.Count > 0)
        {
            parts.Add($"Spojeno s {string.Join(", ", lesson.MergedFrom.Select(m => names.Who(m.Key)))}");
        }
        if (lesson.Original is { } original)
        {
            if (lesson.TeacherId != original.TeacherId)
            {
                parts.Add(substituteFor && original.TeacherId is not null ? $"Zástup, v rozvrhu {names.Teacher(original.TeacherId)}" : "Zástup");
            }
            if (lesson.SubjectId != original.SubjectId)
            {
                parts.Add("Jiný předmět");
            }
            if (lesson.ClassroomId != original.ClassroomId)
            {
                parts.Add("Jiná učebna");
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "Změna";
    }

    public static List<LessonChangeDto> For(IEnumerable<EffectiveLesson> lessons, LessonScope scope, LessonNames names)
    {
        var views = new List<LessonChangeDto>();
        foreach (var lesson in lessons.Where(l => l.Changed))
        {
            if (scope.TeacherId is { } teacher)
            {
                var original = lesson.Original?.TeacherId;
                if (lesson.MergedInto is { } target)
                {
                    if (original == teacher && lesson.TeacherId != teacher)
                    {
                        views.Add(Dto(lesson, true, true, $"Spojeno s {names.Who(target.Key)}", names, asOriginal: true));
                    }
                }
                else if (lesson.TeacherId == teacher)
                {
                    var own = lesson.Original is null || original == teacher;
                    views.Add(Dto(lesson, own && lesson.Original is not null && !lesson.MovedIn, lesson.Gone,
                        Label(lesson, names, substituteFor: !own), names));
                }
                else if (original == teacher && !lesson.MovedIn)
                {
                    views.Add(Dto(lesson, true, true,
                        lesson.Gone ? Label(lesson, names) : $"Zastupuje {names.Teacher(lesson.TeacherId)}", names, asOriginal: true));
                }
            }
            else if (scope.ClassId is { } classId && lesson.ClassIds.Contains(classId)
                && (scope.GroupIds is null || lesson.Key.GroupId is null || scope.GroupIds.Contains(lesson.Key.GroupId.Value)))
            {
                views.Add(Dto(lesson, lesson.Original is not null && !lesson.MovedIn, lesson.Gone, Label(lesson, names), names));
            }
        }
        return views;
    }

    private static LessonChangeDto Dto(
        EffectiveLesson lesson, bool replaces, bool gone, string label, LessonNames names, bool asOriginal = false)
    {
        var subjectId = asOriginal ? lesson.Original!.SubjectId : lesson.SubjectId;
        var teacherId = asOriginal ? lesson.Original!.TeacherId : lesson.TeacherId;
        var classroomId = asOriginal ? lesson.Original!.ClassroomId : lesson.ClassroomId;
        var subject = names.Subjects.GetValueOrDefault(subjectId);
        return new LessonChangeDto(
            lesson.Key.Date, lesson.Key.Date.DayOfWeek, lesson.Key.Period, lesson.Key.ClassId ?? lesson.ClassIds.FirstOrDefault(),
            lesson.Key.GroupId, replaces, gone, label, subjectId, subject.Name ?? "", subject.Abbreviation,
            teacherId, teacherId is null ? null : names.Teacher(teacherId),
            classroomId, classroomId is { } room ? names.Classrooms.GetValueOrDefault(room) : null,
            names.Who(lesson.Key), lesson.Key.GroupId is { } g && names.Groups.TryGetValue(g, out var group) ? group.Name : null);
    }
}
