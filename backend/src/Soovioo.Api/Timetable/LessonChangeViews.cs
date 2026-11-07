namespace Soovioo.Api.Timetable;

/// <summary>A changed lesson as one class, pupil or teacher sees it.</summary>
/// <param name="SchoolClassId">With <paramref name="StudentGroupId"/> which lesson of the timetable it is.</param>
/// <param name="Replaces">Shown instead of that lesson; otherwise besides the timetable's.</param>
/// <param name="Gone">The lesson isn't taught (to them) then.</param>
/// <param name="Label">"Supl", "Odpadá", …; empty when the values tell it all.</param>
/// <param name="WasSubject">The timetable's subject, when another is taught; likewise the teacher and the classroom.</param>
public record LessonChangeDto(
    DateOnly Date, DayOfWeek DayOfWeek, int PeriodNumber, int? SchoolClassId, int? StudentGroupId,
    bool Replaces, bool Gone, string Label,
    int SubjectId, string SubjectName, string? SubjectAbbreviation,
    int? TeacherId, string? TeacherName, int? ClassroomId, string? ClassroomName,
    string SchoolClassName, string? StudentGroupName,
    string? WasSubject = null, string? WasTeacher = null, string? WasClassroom = null);

/// <summary>A class (all its groups' lessons), a pupil (the class and their
/// <see cref="GroupIds"/>) or a teacher.</summary>
public record LessonScope(int? ClassId = null, int? TeacherId = null, IReadOnlySet<int>? GroupIds = null);

public static class LessonChangeViews
{
    private static readonly string[] Days = ["ne", "po", "út", "st", "čt", "pá", "so"];

    /// <summary>"út 10. 11."</summary>
    public static string Day(DateOnly date) => $"{Days[(int)date.DayOfWeek]} {date.Day}. {date.Month}.";

    private static string At(LessonKey to, DateOnly from) =>
        to.Date == from ? $"{to.Period}. h" : $"{Day(to.Date)}, {to.Period}. h";

    /// <summary>"Supl", "Odpadá", "Přesun na 3. h", "Spojeno s 5.B", "Navíc";
    /// a changed subject or classroom alone shows by the values.</summary>
    public static string Label(EffectiveLesson lesson, LessonNames names)
    {
        if (lesson.Change is { Cancelled: true })
        {
            return "Odpadá";
        }
        if (lesson.Gone)
        {
            return $"Přesun na {At(lesson.Change!.MovedTo!, lesson.Key.Date)}";
        }
        if (lesson.MergedInto is { } target)
        {
            return $"Spojeno s {names.Who(target.Key)}";
        }
        var parts = new List<string>();
        if (lesson.MovedIn)
        {
            parts.Add($"Přesun z {At(lesson.Original!.Key, lesson.Key.Date)}");
        }
        if (lesson.Original is null)
        {
            parts.Add("Navíc");
        }
        if (lesson.MergedFrom.Count > 0)
        {
            parts.Add($"Spojeno s {string.Join(", ", lesson.MergedFrom.Select(m => names.Who(m.Key)))}");
        }
        if (lesson.Original is { } original && lesson.TeacherId != original.TeacherId)
        {
            parts.Add("Supl");
        }
        return string.Join(", ", parts);
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
                        Label(lesson, names), names));
                }
                else if (original == teacher && !lesson.MovedIn)
                {
                    views.Add(Dto(lesson, true, true, Label(lesson, names), names, asOriginal: lesson.Gone));
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
        var original = lesson.Original;
        var subjectId = asOriginal ? original!.SubjectId : lesson.SubjectId;
        var teacherId = asOriginal ? original!.TeacherId : lesson.TeacherId;
        var classroomId = asOriginal ? original!.ClassroomId : lesson.ClassroomId;
        var subject = names.Subjects.GetValueOrDefault(subjectId);
        var shown = !asOriginal && original is not null && !lesson.Gone;
        return new LessonChangeDto(
            lesson.Key.Date, lesson.Key.Date.DayOfWeek, lesson.Key.Period, lesson.Key.ClassId ?? lesson.ClassIds.FirstOrDefault(),
            lesson.Key.GroupId, replaces, gone, label, subjectId, subject.Name ?? "", subject.Abbreviation,
            teacherId, teacherId is null ? null : names.Teacher(teacherId),
            classroomId, Classroom(classroomId, names),
            names.Who(lesson.Key), lesson.Key.GroupId is { } g && names.Groups.TryGetValue(g, out var group) ? group.Name : null,
            shown && original!.SubjectId != subjectId ? names.Subject(original.SubjectId) : null,
            shown && original!.TeacherId != teacherId && original.TeacherId is not null ? names.Teacher(original.TeacherId) : null,
            shown && original!.ClassroomId != classroomId ? Classroom(original.ClassroomId, names) : null);
    }

    private static string? Classroom(int? id, LessonNames names) =>
        id is { } room ? names.Classrooms.GetValueOrDefault(room) : null;
}
