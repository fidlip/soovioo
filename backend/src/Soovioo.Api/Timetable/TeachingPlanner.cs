using Soovioo.Api.Common;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Who teaches what in a timetable version and what doesn't add up.
/// A class's učební plán is its grade's with the class's own changes; its
/// subjects are those of the plan, of its lessons and of its assignments. Where the school assigned nobody, a teacher is
/// chosen here — the same inputs give the same choice, so it only changes
/// when the school changes something.</summary>
public static class TeachingPlanner
{
    public record ClassInput(int Id, string Name, int? Grade, int? ClassTeacherId);

    public record SubjectInput(int Id, string Name, string? Abbreviation)
    {
        public string Short => Abbreviation ?? Name;
    }

    public record TeacherInput(
        int Id,
        string Name,
        bool Active,
        IReadOnlyList<TeacherQualification> Qualifications,
        string? Qualification,
        decimal? ContractedHours,
        IReadOnlyList<DayOfWeek> TeachingDays,
        bool HasProfile)
    {
        public bool Covers(int subjectId, int? grade) => Qualifications.Any(q => q.Covers(subjectId, grade));
    }

    public record LessonInput(int SchoolClassId, int SubjectId, int? TeacherId, DayOfWeek DayOfWeek, WeekParity WeekParity)
    {
        /// <summary>A lesson every other week is half an hour a week.</summary>
        public decimal Hours => WeekParity == WeekParity.Every ? 1m : 0.5m;
    }

    public record Input(
        IReadOnlyList<ClassInput> Classes,
        IReadOnlyList<SubjectInput> Subjects,
        IReadOnlyList<TeacherInput> Teachers,
        IReadOnlyList<CurriculumItemDto> Curriculum,
        IReadOnlyList<ClassCurriculumItemDto> ClassCurriculum,
        IReadOnlyList<(int SchoolClassId, int SubjectId, int TeacherId)> Assignments,
        IReadOnlyList<LessonInput> Lessons);

    private sealed class Row
    {
        public required ClassInput Class { get; init; }
        public required SubjectInput Subject { get; init; }
        public decimal? PlanHours { get; init; }
        public decimal? GradeHours { get; init; }
        public bool ClassPlan { get; init; }
        public decimal TimetableHours { get; init; }
        public int? TeacherId { get; set; }
        public bool Automatic { get; set; }

        /// <summary>What the teacher is given for it: the plan's hours, or
        /// the timetable's where the plan has none.</summary>
        public decimal Hours => PlanHours ?? TimetableHours;
    }

    public static (List<TeachingClassDto> Classes, List<TeachingTeacherDto> Teachers) Plan(Input input)
    {
        var subjects = input.Subjects.ToDictionary(s => s.Id);
        var teachers = input.Teachers.ToDictionary(t => t.Id);
        var plan = input.Curriculum.ToDictionary(c => (c.Grade, c.SubjectId), c => c.HoursPerWeek);
        var classPlan = input.ClassCurriculum.ToDictionary(c => (c.SchoolClassId, c.SubjectId), c => c.HoursPerWeek);
        var assigned = input.Assignments.ToDictionary(a => (a.SchoolClassId, a.SubjectId), a => a.TeacherId);
        var lessons = input.Lessons.ToLookup(l => (l.SchoolClassId, l.SubjectId));

        var rows = new List<Row>();
        foreach (var c in input.Classes)
        {
            var subjectIds = input.Curriculum.Where(p => p.Grade == c.Grade).Select(p => p.SubjectId)
                .Concat(input.ClassCurriculum.Where(p => p.SchoolClassId == c.Id).Select(p => p.SubjectId))
                .Concat(input.Lessons.Where(l => l.SchoolClassId == c.Id).Select(l => l.SubjectId))
                .Concat(input.Assignments.Where(a => a.SchoolClassId == c.Id).Select(a => a.SubjectId))
                .Distinct()
                .Where(subjects.ContainsKey);
            foreach (var subjectId in subjectIds)
            {
                decimal? gradeHours = c.Grade is { } grade && plan.TryGetValue((grade, subjectId), out var hours) ? hours : null;
                var own = classPlan.TryGetValue((c.Id, subjectId), out var ownHours);
                rows.Add(new Row
                {
                    Class = c,
                    Subject = subjects[subjectId],
                    PlanHours = own ? ownHours : gradeHours,
                    GradeHours = gradeHours,
                    ClassPlan = own,
                    TimetableHours = lessons[(c.Id, subjectId)].Sum(l => l.Hours),
                    TeacherId = assigned.TryGetValue((c.Id, subjectId), out var teacherId) ? teacherId : null,
                });
            }
        }

        var load = rows.Where(r => r.TeacherId is not null).GroupBy(r => r.TeacherId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Hours));
        List<TeacherInput> Candidates(Row r) => input.Teachers
            .Where(t => t.Active && t.TeachingDays.Count > 0 && t.Covers(r.Subject.Id, r.Class.Grade))
            .ToList();
        var open = rows.Where(r => r.TeacherId is null && r.Hours > 0)
            .Select(r => (Row: r, Candidates: Candidates(r)))
            .OrderBy(x => x.Candidates.Count).ThenByDescending(x => x.Row.Hours)
            .ThenBy(x => x.Row.Class.Name).ThenBy(x => x.Row.Subject.Name)
            .ToList();
        foreach (var (row, candidates) in open)
        {
            var teacher = candidates
                .OrderByDescending(t => lessons[(row.Class.Id, row.Subject.Id)].Where(l => l.TeacherId == t.Id).Sum(l => l.Hours))
                .ThenByDescending(t => t.ContractedHours is not { } contracted || load.GetValueOrDefault(t.Id) + row.Hours <= contracted)
                .ThenByDescending(t => row.Class.ClassTeacherId == t.Id)
                .ThenByDescending(t => rows.Any(o => o.Class == row.Class && o.TeacherId == t.Id))
                .ThenByDescending(t => (t.ContractedHours ?? 0) - load.GetValueOrDefault(t.Id))
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (teacher is null)
            {
                continue;
            }
            row.TeacherId = teacher.Id;
            row.Automatic = true;
            load[teacher.Id] = load.GetValueOrDefault(teacher.Id) + row.Hours;
        }

        string NameOf(int? id) => id is { } i && teachers.TryGetValue(i, out var t) ? t.Name : "neznámý učitel";

        var classes = input.Classes.Select(c =>
        {
            var classRows = rows.Where(r => r.Class == c).OrderBy(r => r.Subject.Name).Select(r =>
            {
                var problems = new List<string>();
                var inGrade = c.Grade is { } g ? $" v {g}. ročníku" : "";
                if (r.PlanHours is { } planned && r.TimetableHours < planned)
                {
                    problems.Add($"V rozvrhu chybí {FormatHours(planned - r.TimetableHours)}.");
                }
                else if (r.PlanHours is { } plannedLess && r.TimetableHours > plannedLess)
                {
                    problems.Add($"V rozvrhu je o {FormatHours(r.TimetableHours - plannedLess)} víc než v plánu.");
                }
                else if (r.PlanHours is null)
                {
                    problems.Add("Předmět není v učebním plánu třídy.");
                }
                if (r.TeacherId is null && r.Hours > 0)
                {
                    problems.Add($"Nikdo nemá aprobaci pro {r.Subject.Short}{inGrade}.");
                }
                if (r.TeacherId is { } teacherId)
                {
                    if (!r.Automatic && teachers.TryGetValue(teacherId, out var t) && !t.Covers(r.Subject.Id, c.Grade))
                    {
                        problems.Add($"{t.Name} nemá aprobaci pro {r.Subject.Short}{inGrade}.");
                    }
                    var others = lessons[(c.Id, r.Subject.Id)]
                        .Where(l => l.TeacherId is not null && l.TeacherId != teacherId)
                        .Select(l => NameOf(l.TeacherId)).Distinct().Order().ToList();
                    if (others.Count > 0)
                    {
                        problems.Add($"V rozvrhu učí {string.Join(", ", others)}.");
                    }
                }
                var untaught = lessons[(c.Id, r.Subject.Id)].Where(l => l.TeacherId is null).Sum(l => l.Hours);
                if (untaught > 0)
                {
                    problems.Add($"{FormatHours(untaught)} v rozvrhu bez učitele.");
                }
                return new TeachingRowDto(
                    r.Subject.Id, r.Subject.Name, r.Subject.Abbreviation, r.PlanHours, r.GradeHours, r.ClassPlan, r.TimetableHours,
                    r.TeacherId, r.TeacherId is null ? null : NameOf(r.TeacherId), r.Automatic, problems);
            }).ToList();
            IReadOnlyList<string> classProblems = c.Grade is null
                ? ["Třída nemá zadaný ročník — učební plán ročníku se na ni nevztahuje, jen její vlastní."]
                : [];
            return new TeachingClassDto(c.Id, c.Name, c.Grade, classProblems, classRows);
        }).ToList();

        var listed = input.Teachers.Where(t => t.Active || rows.Any(r => r.TeacherId == t.Id)).OrderBy(t => t.Name, StringComparer.Ordinal);
        var teacherDtos = listed.Select(t =>
        {
            var assignedHours = rows.Where(r => r.TeacherId == t.Id).Sum(r => r.Hours);
            var own = input.Lessons.Where(l => l.TeacherId == t.Id).ToList();
            var problems = new List<string>();
            if (!t.Active)
            {
                problems.Add("Účet už neučí (je deaktivovaný nebo nemá roli Učitel).");
            }
            if (t.Active && t.Qualifications.Count == 0)
            {
                problems.Add("Nemá zadanou aprobaci.");
            }
            if (t.ContractedHours is { } contracted && assignedHours > contracted)
            {
                problems.Add($"Přiděleno o {FormatHours(assignedHours - contracted)} víc, než má nasmlouváno.");
            }
            else if (t.ContractedHours is { } contractedMore && assignedHours < contractedMore)
            {
                problems.Add($"Přiděleno o {FormatHours(contractedMore - assignedHours)} míň, než má nasmlouváno.");
            }
            else if (t.Active && t.ContractedHours is null)
            {
                problems.Add("Nemá zadané nasmlouvané hodiny.");
            }
            foreach (var day in own.Select(l => l.DayOfWeek).Distinct().Where(d => !t.TeachingDays.Contains(d)).Order())
            {
                problems.Add($"V rozvrhu učí {OnDay(day)}, kdy učit nemůže.");
            }
            var taught = rows.Where(r => r.TeacherId == t.Id && r.Hours > 0)
                .OrderBy(r => r.Class.Name).ThenBy(r => r.Subject.Name)
                .Select(r => new TeachingTaughtDto(
                    r.Class.Id, r.Class.Name, r.Subject.Id, r.Subject.Name, r.Subject.Abbreviation, r.Hours, r.Automatic))
                .ToList();
            return new TeachingTeacherDto(
                t.Id, t.Name, t.Qualification, t.ContractedHours, assignedHours, own.Sum(l => l.Hours), t.TeachingDays,
                taught, problems);
        }).ToList();

        return (classes, teacherDtos);
    }

    /// <summary>"v pondělí".</summary>
    public static string OnDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "v pondělí",
        DayOfWeek.Tuesday => "v úterý",
        DayOfWeek.Wednesday => "ve středu",
        DayOfWeek.Thursday => "ve čtvrtek",
        DayOfWeek.Friday => "v pátek",
        DayOfWeek.Saturday => "v sobotu",
        _ => "v neděli",
    };

    /// <summary>"1 h", "1,5 h".</summary>
    public static string FormatHours(decimal hours) =>
        $"{hours.ToString("0.#", SchoolFormat.Culture)} h";
}

public record CurriculumItemDto(int Grade, int SubjectId, decimal HoursPerWeek);

/// <param name="HoursPerWeek">0 = the class doesn't have the subject.</param>
public record ClassCurriculumItemDto(int SchoolClassId, int SubjectId, decimal HoursPerWeek);

/// <param name="Grade">Ročník; null = the class has none, the plan doesn't apply.</param>
public record TeachingClassDto(
    int Id, string Name, int? Grade, IReadOnlyList<string> Problems, IReadOnlyList<TeachingRowDto> Rows);

/// <param name="PlanHours">Hours a week by the class's učební plán; null = not in it.</param>
/// <param name="GradeHours">What the grade's plan says; null = nothing.</param>
/// <param name="ClassPlan">The class has its own hours for the subject, not the grade's.</param>
/// <param name="TimetableHours">Hours a week the version's timetable has.</param>
/// <param name="Automatic">Chosen by the system, not by the school.</param>
public record TeachingRowDto(
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    decimal? PlanHours,
    decimal? GradeHours,
    bool ClassPlan,
    decimal TimetableHours,
    int? TeacherId,
    string? TeacherName,
    bool Automatic,
    IReadOnlyList<string> Problems);

/// <param name="AssignedHours">Hours of what the teacher is assigned (or
/// chosen for automatically).</param>
public record TeachingTeacherDto(
    int Id,
    string Name,
    string? Qualification,
    decimal? ContractedHours,
    decimal AssignedHours,
    decimal TimetableHours,
    IReadOnlyList<DayOfWeek> TeachingDays,
    IReadOnlyList<TeachingTaughtDto> Taught,
    IReadOnlyList<string> Problems);

/// <summary>A class and subject a teacher is given (or chosen for automatically).</summary>
public record TeachingTaughtDto(
    int SchoolClassId,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    decimal Hours,
    bool Automatic);
