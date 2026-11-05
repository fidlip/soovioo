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

    public record SubjectInput(int Id, string Name, string? Abbreviation, bool DoubleLessons = false)
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
        IReadOnlyList<AvailabilitySlot> Availability,
        bool HasProfile)
    {
        public IReadOnlyList<DayOfWeek> TeachingDays { get; } = AvailabilitySlot.TeachingDays(Availability);

        public bool Covers(int subjectId, int? grade) => Qualifications.Any(q => q.Covers(subjectId, grade));
    }

    /// <param name="GroupId">A lesson of a group: once, not once per class.</param>
    public record LessonInput(
        int SchoolClassId, int SubjectId, int? TeacherId, DayOfWeek DayOfWeek, WeekParity WeekParity, int Period = 1,
        int? GroupId = null)
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
        IReadOnlyList<LessonInput> Lessons,
        IReadOnlyList<GroupInput>? Groups = null);

    /// <param name="DoubleLessons">Its own, its division's or the subject's.</param>
    /// <param name="Members">How many pupils it has.</param>
    /// <param name="HoursPerWeek">Its own or its division's; null = as the učební plán of its classes.</param>
    /// <param name="CoversSubjectId">Its division's row of the učební plán, see <see cref="GroupDivision.CoversSubjectId"/>.</param>
    public record GroupInput(
        int Id, string Name, int SubjectId, IReadOnlyList<int> ClassIds, decimal? HoursPerWeek, bool DoubleLessons,
        int? TeacherId, int? DivisionId = null, string? DivisionName = null, int Members = 0, int? CoversSubjectId = null);

    private sealed class Row
    {
        /// <summary>Of a group: its first class.</summary>
        public required ClassInput Class { get; init; }
        public GroupInput? Group { get; init; }

        /// <summary>The class has the subject in groups.</summary>
        public IReadOnlyList<GroupInput> InGroups { get; init; } = [];

        /// <summary>The grade the group's classes share, otherwise none.</summary>
        public int? Grade { get; init; }
        public required SubjectInput Subject { get; init; }
        public decimal? PlanHours { get; init; }
        public decimal? GradeHours { get; init; }
        public bool ClassPlan { get; init; }

        /// <summary>Of a group: why its hours aren't clear from the plan.</summary>
        public string? HoursProblem { get; init; }
        public decimal TimetableHours { get; init; }
        public int? TeacherId { get; set; }
        public bool Automatic { get; set; }
        public bool DoubleLessons { get; init; }
        public bool GradeDoubleLessons { get; init; }

        /// <summary>What the teacher is given for it: the plan's hours, or
        /// the timetable's where the plan has none.</summary>
        public decimal Hours => InGroups.Count > 0 ? TimetableHours : PlanHours ?? TimetableHours;
    }

    public static (List<TeachingClassDto> Classes, List<TeachingTeacherDto> Teachers) Plan(Input input)
    {
        var (classes, teachers, _) = PlanWithGroups(input);
        return (classes, teachers);
    }

    public static (List<TeachingClassDto> Classes, List<TeachingTeacherDto> Teachers, List<TeachingGroupDto> Groups)
        PlanWithGroups(Input input)
    {
        var subjects = input.Subjects.ToDictionary(s => s.Id);
        var teachers = input.Teachers.ToDictionary(t => t.Id);
        var plan = input.Curriculum.ToDictionary(c => (c.Grade, c.SubjectId));
        var classPlan = input.ClassCurriculum.ToDictionary(c => (c.SchoolClassId, c.SubjectId));
        var assigned = input.Assignments.ToDictionary(a => (a.SchoolClassId, a.SubjectId), a => a.TeacherId);
        var lessons = input.Lessons.Where(l => l.GroupId is null).ToLookup(l => (l.SchoolClassId, l.SubjectId));
        var groupLessons = input.Lessons.Where(l => l.GroupId is not null).ToLookup(l => l.GroupId!.Value);
        var groups = (input.Groups ?? []).Where(g => subjects.ContainsKey(g.SubjectId)).ToList();
        var classesById = input.Classes.ToDictionary(c => c.Id);

        var rows = new List<Row>();
        foreach (var c in input.Classes)
        {
            var subjectIds = input.Curriculum.Where(p => p.Grade == c.Grade).Select(p => p.SubjectId)
                .Concat(input.ClassCurriculum.Where(p => p.SchoolClassId == c.Id).Select(p => p.SubjectId))
                .Concat(input.Lessons.Where(l => l.SchoolClassId == c.Id && l.GroupId is null).Select(l => l.SubjectId))
                .Concat(input.Assignments.Where(a => a.SchoolClassId == c.Id).Select(a => a.SubjectId))
                .Distinct()
                .Where(subjects.ContainsKey);
            foreach (var subjectId in subjectIds)
            {
                var gradeItem = c.Grade is { } grade ? plan.GetValueOrDefault((grade, subjectId)) : null;
                var classItem = classPlan.GetValueOrDefault((c.Id, subjectId));
                var gradeHours = gradeItem?.HoursPerWeek;
                var own = classItem?.HoursPerWeek is not null;
                var gradeBlocks = gradeItem?.DoubleLessons ?? subjects[subjectId].DoubleLessons;
                rows.Add(new Row
                {
                    Class = c,
                    Grade = c.Grade,
                    InGroups = groups.Where(g => (g.CoversSubjectId ?? g.SubjectId) == subjectId && g.ClassIds.Contains(c.Id)).ToList(),
                    Subject = subjects[subjectId],
                    PlanHours = own ? classItem!.HoursPerWeek : gradeHours,
                    DoubleLessons = classItem?.DoubleLessons ?? gradeBlocks,
                    GradeDoubleLessons = gradeBlocks,
                    GradeHours = gradeHours,
                    ClassPlan = own,
                    TimetableHours = lessons[(c.Id, subjectId)].Sum(l => l.Hours),
                    TeacherId = assigned.TryGetValue((c.Id, subjectId), out var teacherId) ? teacherId : null,
                });
            }
        }

        // A class's hours of a subject: its own, else its grade's.
        decimal? PlanOf(ClassInput c, int subjectId) =>
            classPlan.GetValueOrDefault((c.Id, subjectId))?.HoursPerWeek
            ?? (c.Grade is { } grade ? plan.GetValueOrDefault((grade, subjectId))?.HoursPerWeek : null);
        foreach (var g in groups)
        {
            var groupClasses = g.ClassIds.Where(classesById.ContainsKey).Select(id => classesById[id]).ToList();
            if (groupClasses.Count == 0)
            {
                continue;
            }
            var grades = groupClasses.Select(c => c.Grade).Distinct().ToList();
            // Without hours of its own, the plan's — of the row its division
            // covers, else of every group of its division.
            var together = g.DivisionId is { } divisionId ? groups.Where(o => o.DivisionId == divisionId).ToList() : [g];
            var planned = together
                .Select(o => o.CoversSubjectId is { } covers && subjects.ContainsKey(covers) ? covers : o.SubjectId)
                .Distinct()
                .SelectMany(id => groupClasses.Select(c => (Class: c, Subject: subjects[id], Hours: PlanOf(c, id))))
                .ToList();
            var hours = planned.Where(p => p.Hours is not null).Select(p => p.Hours!.Value).Distinct().ToList();
            string? hoursProblem = g.HoursPerWeek is not null ? null
                : hours.Count == 0 ? $"{(together.Count > 1 ? "Předměty skupin nejsou" : "Předmět není")} v učebním plánu {(groupClasses.Count > 1 ? "tříd" : "třídy")}, zadejte hodiny {(g.DivisionId is null ? "skupiny" : "dělení")}."
                : hours.Count > 1 ? $"V učebním plánu má {string.Join(", ", planned.Where(p => p.Hours is not null).OrderBy(p => p.Class.Name, StringComparer.Ordinal).ThenBy(p => p.Subject.Short, StringComparer.Ordinal).Select(p => $"{p.Class.Name} {p.Subject.Short} {FormatHours(p.Hours!.Value)}").Distinct())}, zadejte hodiny {(g.DivisionId is null ? "skupiny" : "dělení")}."
                : null;
            rows.Add(new Row
            {
                Class = groupClasses[0],
                Group = g,
                Grade = grades.Count == 1 ? grades[0] : null,
                Subject = subjects[g.SubjectId],
                PlanHours = g.HoursPerWeek ?? (hours.Count == 0 ? 0 : hours.Max()),
                HoursProblem = hoursProblem,
                DoubleLessons = g.DoubleLessons,
                TimetableHours = groupLessons[g.Id].Sum(l => l.Hours),
                TeacherId = g.TeacherId,
            });
        }
        IEnumerable<LessonInput> LessonsOf(Row r) => r.Group is { } group ? groupLessons[group.Id] : lessons[(r.Class.Id, r.Subject.Id)];

        var load = rows.Where(r => r.TeacherId is not null).GroupBy(r => r.TeacherId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Hours));
        List<TeacherInput> Candidates(Row r) => input.Teachers
            .Where(t => t.Active && t.TeachingDays.Count > 0 && t.Covers(r.Subject.Id, r.Grade))
            .ToList();
        var open = rows.Where(r => r.TeacherId is null && r.Hours > 0)
            .Select(r => (Row: r, Candidates: Candidates(r)))
            .OrderBy(x => x.Candidates.Count).ThenByDescending(x => x.Row.Hours)
            .ThenBy(x => x.Row.Group is not null).ThenBy(x => x.Row.Class.Name).ThenBy(x => x.Row.Subject.Name)
            .ThenBy(x => x.Row.Group?.Name)
            .ToList();
        foreach (var (row, candidates) in open)
        {
            // Groups of a division are taught at once: each by someone else.
            var teacher = candidates
                .Where(t => row.Group?.DivisionId is not { } d
                    || !rows.Any(o => o != row && o.Group?.DivisionId == d && o.TeacherId == t.Id))
                .OrderByDescending(t => LessonsOf(row).Where(l => l.TeacherId == t.Id).Sum(l => l.Hours))
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
            var classRows = rows.Where(r => r.Class == c && r.Group is null).OrderBy(r => r.Subject.Name).Select(r =>
            {
                var problems = new List<string>();
                var inGrade = c.Grade is { } g ? $" v {g}. ročníku" : "";
                var groupNames = r.InGroups.Select(x => x.Name).Order(StringComparer.Ordinal).ToList();
                if (r.InGroups.Count > 0)
                {
                    if (r.TimetableHours > 0)
                    {
                        problems.Add($"Učí se ve skupinách ({string.Join(", ", groupNames)}), v rozvrhu má i {FormatHours(r.TimetableHours)} celé třídy.");
                    }
                    // A row covered by divisions: together they have its hours.
                    var covering = rows.Where(o => o.Group is { } x && x.CoversSubjectId == r.Subject.Id && x.ClassIds.Contains(c.Id))
                        .DistinctBy(o => o.Group!.DivisionId)
                        .Sum(o => o.PlanHours ?? 0);
                    if (covering > 0 && r.PlanHours is { } plan && covering != plan)
                    {
                        problems.Add($"Dělení, která ho učí, mají {FormatHours(covering)}, v plánu je {FormatHours(plan)}.");
                    }
                }
                else if (r.PlanHours is { } planned && r.TimetableHours < planned)
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
                    r.TeacherId, r.TeacherId is null ? null : NameOf(r.TeacherId), r.Automatic, problems,
                    r.DoubleLessons, r.GradeDoubleLessons, groupNames);
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
            problems.AddRange(Unavailable(t.Availability, own.Select(l => (l.DayOfWeek, l.Period)))
                .Select(when => $"V rozvrhu učí {when}, kdy učit nemůže."));
            var taught = rows.Where(r => r.TeacherId == t.Id && r.Hours > 0)
                .OrderBy(r => r.Group is not null).ThenBy(r => r.Group?.Name ?? r.Class.Name).ThenBy(r => r.Subject.Name)
                .Select(r => new TeachingTaughtDto(
                    r.Class.Id, r.Group?.Name ?? r.Class.Name, r.Subject.Id, r.Subject.Name, r.Subject.Abbreviation, r.Hours, r.Automatic,
                    r.Group?.Id))
                .ToList();
            return new TeachingTeacherDto(
                t.Id, t.Name, t.Qualification, t.ContractedHours, assignedHours, own.Sum(l => l.Hours), t.TeachingDays,
                taught, problems, t.Availability);
        }).ToList();

        var groupDtos = rows.Where(r => r.Group is not null)
            .OrderBy(r => r.Group!.DivisionName ?? r.Group.Name, StringComparer.Ordinal).ThenBy(r => r.Group!.Name, StringComparer.Ordinal)
            .Select(r =>
            {
                var g = r.Group!;
                var problems = new List<string>();
                var inGrade = r.Grade is { } grade ? $" v {grade}. ročníku" : "";
                if (r.TimetableHours < r.Hours)
                {
                    problems.Add($"V rozvrhu chybí {FormatHours(r.Hours - r.TimetableHours)}.");
                }
                else if (r.TimetableHours > r.Hours)
                {
                    problems.Add($"V rozvrhu je o {FormatHours(r.TimetableHours - r.Hours)} víc než v plánu.");
                }
                if (r.HoursProblem is { } hoursProblem)
                {
                    problems.Add(hoursProblem);
                }
                if (g.DivisionId is { } division && r.TeacherId is { } own
                    && rows.FirstOrDefault(o => o != r && o.Group?.DivisionId == division && o.TeacherId == own) is { } sibling)
                {
                    problems.Add($"{NameOf(own)} učí i skupinu {sibling.Group!.Name} téhož dělení, ve stejnou dobu.");
                }
                if (g.Members == 0)
                {
                    problems.Add("Skupina nemá žádné žáky.");
                }
                if (r.TeacherId is null && r.Hours > 0)
                {
                    problems.Add(Candidates(r).Count > 0
                        ? $"Kdo má aprobaci pro {r.Subject.Short}{inGrade}, učí jinou skupinu dělení."
                        : $"Nikdo nemá aprobaci pro {r.Subject.Short}{inGrade}.");
                }
                if (r.TeacherId is { } teacherId)
                {
                    if (!r.Automatic && teachers.TryGetValue(teacherId, out var teacher) && !teacher.Covers(r.Subject.Id, r.Grade))
                    {
                        problems.Add($"{teacher.Name} nemá aprobaci pro {r.Subject.Short}{inGrade}.");
                    }
                    var others = groupLessons[g.Id]
                        .Where(l => l.TeacherId is not null && l.TeacherId != teacherId)
                        .Select(l => NameOf(l.TeacherId)).Distinct().Order().ToList();
                    if (others.Count > 0)
                    {
                        problems.Add($"V rozvrhu učí {string.Join(", ", others)}.");
                    }
                }
                var untaught = groupLessons[g.Id].Where(l => l.TeacherId is null).Sum(l => l.Hours);
                if (untaught > 0)
                {
                    problems.Add($"{FormatHours(untaught)} v rozvrhu bez učitele.");
                }
                var classNames = g.ClassIds.Where(classesById.ContainsKey).Select(id => classesById[id].Name)
                    .Order(StringComparer.Ordinal).ToList();
                return new TeachingGroupDto(
                    g.Id, g.Name, g.DivisionId, g.DivisionName, r.Subject.Id, r.Subject.Name, r.Subject.Abbreviation,
                    g.ClassIds, string.Join(", ", classNames), r.Grade, r.Hours, r.TimetableHours, r.TeacherId,
                    r.TeacherId is null ? null : NameOf(r.TeacherId), r.Automatic, r.DoubleLessons, g.Members, problems,
                    g.HoursPerWeek is null);
            }).ToList();

        return (classes, teacherDtos, groupDtos);
    }

    /// <summary>"v pondělí", "v úterý 1., 2. hod." — when lessons are where
    /// the teacher can't teach.</summary>
    public static IEnumerable<string> Unavailable(
        IReadOnlyList<AvailabilitySlot> availability, IEnumerable<(DayOfWeek Day, int Period)> lessons)
    {
        var days = AvailabilitySlot.TeachingDays(availability);
        return lessons.Distinct()
            .Where(l => AvailabilitySlot.At(availability, l.Day, l.Period) == AvailabilityKind.Unavailable)
            .GroupBy(l => l.Day).OrderBy(g => g.Key)
            .Select(g => days.Contains(g.Key)
                ? $"{OnDay(g.Key)} {string.Join(", ", g.Select(l => l.Period).Order().Select(p => $"{p}."))} hod."
                : OnDay(g.Key));
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

/// <param name="DoubleLessons">Null = as the subject.</param>
public record CurriculumItemDto(int Grade, int SubjectId, decimal HoursPerWeek, bool? DoubleLessons = null);

/// <param name="HoursPerWeek">0 = the class doesn't have the subject; null = as its grade.</param>
/// <param name="DoubleLessons">Null = as its grade.</param>
public record ClassCurriculumItemDto(int SchoolClassId, int SubjectId, decimal? HoursPerWeek, bool? DoubleLessons = null);

/// <param name="Grade">Ročník; null = the class has none, the plan doesn't apply.</param>
public record TeachingClassDto(
    int Id, string Name, int? Grade, IReadOnlyList<string> Problems, IReadOnlyList<TeachingRowDto> Rows);

/// <param name="PlanHours">Hours a week by the class's učební plán; null = not in it.</param>
/// <param name="GradeHours">What the grade's plan says; null = nothing.</param>
/// <param name="ClassPlan">The class has its own hours for the subject, not the grade's.</param>
/// <param name="TimetableHours">Hours a week the version's timetable has.</param>
/// <param name="Automatic">Chosen by the system, not by the school.</param>
/// <param name="DoubleLessons">In blocks of two: the class's, its grade's or the subject's.</param>
/// <param name="GradeDoubleLessons">What the grade (or the subject) says.</param>
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
    IReadOnlyList<string> Problems,
    bool DoubleLessons = false,
    bool GradeDoubleLessons = false,
    IReadOnlyList<string>? Groups = null)
{
    /// <summary>The class has the subject in these groups, not as a whole class.</summary>
    public IReadOnlyList<string> Groups { get; init; } = Groups ?? [];
}

/// <summary>A group's učební plán and přidělení.</summary>
/// <param name="Grade">The ročník its classes share; null = none or several.</param>
/// <param name="PlanHours">Its own, its division's or the učební plán's.</param>
/// <param name="HoursByPlan">The hours come from the učební plán of its classes.</param>
/// <param name="ClassNames">"7.A, 7.B".</param>
public record TeachingGroupDto(
    int Id,
    string Name,
    int? DivisionId,
    string? DivisionName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    IReadOnlyList<int> ClassIds,
    string ClassNames,
    int? Grade,
    decimal PlanHours,
    decimal TimetableHours,
    int? TeacherId,
    string? TeacherName,
    bool Automatic,
    bool DoubleLessons,
    int MemberCount,
    IReadOnlyList<string> Problems,
    bool HoursByPlan = false);

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
    IReadOnlyList<string> Problems,
    IReadOnlyList<AvailabilitySlot>? Availability = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<AvailabilitySlot> Availability { get; init; } = Availability ?? [];
}

/// <summary>A class and subject a teacher is given (or chosen for automatically);
/// of a group <paramref name="SchoolClassName"/> is the group's name.</summary>
public record TeachingTaughtDto(
    int SchoolClassId,
    string SchoolClassName,
    int SubjectId,
    string SubjectName,
    string? SubjectAbbreviation,
    decimal Hours,
    bool Automatic,
    int? StudentGroupId = null);
