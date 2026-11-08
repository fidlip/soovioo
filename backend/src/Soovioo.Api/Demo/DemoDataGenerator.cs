using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Homework;
using Soovioo.Api.Messaging;
using Soovioo.Api.Payments;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Demo;

/// <param name="ClassesPerGrade">1–3 (parallel classes A, B, C).</param>
/// <param name="Seed">Same seed + same day = the same school.</param>
public record DemoSchoolOptions(SchoolType Type, int ClassesPerGrade, int Seed);

/// <summary>The users the wizard's "za koho se chcete podívat" logs in as —
/// the ones with the most to show.</summary>
public record DemoFeaturedUsers(int AdminId, int TeacherId, int ParentId, int StudentId);

/// <summary>Fills an empty demo school with a plausible school year up to
/// today: classes, pupils with their families, teachers with their
/// aprobace, a collision-free (as far as greedy placement gets) timetable
/// with divisions (Tv of girls and boys of a grade, a gymnázium's
/// seminars), and every agenda — grades, absences, behaviour, homework, messages,
/// events, payments. Deterministic for a given seed and day, so tests can
/// pin it down. Nobody gets a password: demo users log in by being picked
/// (see <see cref="DemoController"/>). Profile photos are illustrated
/// avatars (<see cref="DemoAvatars"/>) — only when a
/// <see cref="PhotoStorage"/> is given.</summary>
public class DemoDataGenerator(AppDbContext db, PhotoStorage? photos = null)
{
    // Fixed CET as the fallback for a container without tzdata — an hour
    // off in summer is fine for made-up data.
    private static readonly TimeZoneInfo Prague =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Prague", out var prague)
            ? prague
            : TimeZoneInfo.CreateCustomTimeZone("CET", TimeSpan.FromHours(1), "CET", "CET");

    private sealed record SubjectDef(string Abbr, string Name, string Color, string? RoomGroup = null);

    private static readonly SubjectDef[] ElementarySubjects =
    [
        new("Čj", "Český jazyk", "#fca5a5"), new("M", "Matematika", "#93c5fd"), new("Aj", "Anglický jazyk", "#fcd34d"),
        new("Nj", "Německý jazyk", "#fde68a"), new("Prv", "Prvouka", "#86efac"), new("Přv", "Přírodověda", "#6ee7b7"),
        new("Vl", "Vlastivěda", "#fdba74"), new("D", "Dějepis", "#d6b48c"), new("Z", "Zeměpis", "#a7f3d0"),
        new("Př", "Přírodopis", "#bbf7d0"), new("F", "Fyzika", "#a5b4fc", "lab"), new("Ch", "Chemie", "#c4b5fd", "lab"),
        new("Inf", "Informatika", "#67e8f9", "computer"), new("Ov", "Občanská výchova", "#f9a8d4"),
        new("Hv", "Hudební výchova", "#f0abfc", "music"), new("Vv", "Výtvarná výchova", "#fbcfe8", "art"),
        new("Tv", "Tělesná výchova", "#bef264", "gym"), new("Vz", "Výchova ke zdraví", "#d9f99d"),
        new("Pč", "Pracovní činnosti", "#e7e5e4", "workshop"),
        new("Fj", "Francouzský jazyk", "#bfdbfe"), new("Šj", "Španělský jazyk", "#fecaca"),
        new("PVP", "Povinně volitelný předmět", "#ddd6fe"), new("Dcj", "Další cizí jazyk", "#fde68a"),
    ];

    private static readonly SubjectDef[] SecondarySubjects =
    [
        new("Čj", "Český jazyk a literatura", "#fca5a5"), new("Aj", "Anglický jazyk", "#fcd34d"),
        new("Nj", "Německý jazyk", "#fde68a"), new("M", "Matematika", "#93c5fd"), new("F", "Fyzika", "#a5b4fc", "lab"),
        new("Ch", "Chemie", "#c4b5fd", "lab"), new("Bi", "Biologie", "#86efac", "lab"), new("D", "Dějepis", "#d6b48c"),
        new("Z", "Zeměpis", "#a7f3d0"), new("ZSV", "Základy společenských věd", "#f9a8d4"),
        new("Inf", "Informatika", "#67e8f9", "computer"), new("Vv", "Výtvarná výchova", "#fbcfe8", "art"),
        new("Tv", "Tělesná výchova", "#bef264", "gym"), new("Sem", "Seminář", "#e7e5e4"),
        new("Fj", "Francouzský jazyk", "#bfdbfe"), new("Šj", "Španělský jazyk", "#fecaca"),
        new("Dcj", "Další cizí jazyk", "#fde68a"),
    ];

    /// <summary>Created only when the school offers them.</summary>
    private static readonly HashSet<string> OfferedSubjects = ["Fj", "Šj", "PVP"];

    /// <summary>A nepovinný předmět for pupils of the grades who sign up,
    /// after their lessons. Taught by a teacher of <paramref name="TeacherOf"/>,
    /// a first-stage teacher ("1st") or one from outside (null).</summary>
    private sealed record OptionalDef(
        SubjectDef Subject, int FromGrade, int ToGrade, int MinPupils, int MaxPupils, int Hours, string? TeacherOf);

    private static readonly OptionalDef[] ElementaryOptional =
    [
        new(new("Náb", "Náboženství", "#fef3c7"), 1, 5, 7, 10, 1, null),
        new(new("Log", "Logopedická péče", "#fce7f3"), 1, 2, 3, 6, 1, "1st"),
        new(new("NPU", "Nápravy poruch učení", "#e0e7ff"), 3, 5, 4, 8, 1, "1st"),
        new(new("Sbz", "Sborový zpěv", "#f5d0fe", "music"), 4, 9, 12, 20, 2, "Hv"),
        new(new("SpH", "Sportovní hry", "#d9f99d", "gym"), 6, 9, 10, 16, 2, "Tv"),
        new(new("KAj", "Konverzace v anglickém jazyce", "#fef08a", "language"), 8, 9, 8, 12, 1, "Aj"),
    ];

    private static readonly OptionalDef[] SecondaryOptional =
    [
        new(new("FCE", "Příprava na FCE/CAE", "#fef08a", "language"), 2, 4, 8, 14, 2, "Aj"),
        new(new("DG", "Deskriptivní geometrie", "#bae6fd"), 2, 3, 5, 10, 1, "M"),
        new(new("KRm", "Konverzace s rodilým mluvčím", "#fde68a", "language"), 1, 2, 8, 12, 1, null),
        new(new("Prg", "Programování a 3D modelování", "#a5f3fc", "computer"), 1, 3, 8, 12, 2, "Inf"),
        new(new("SpH", "Sportovní hry", "#d9f99d", "gym"), 1, 4, 10, 16, 2, "Tv"),
    ];

    /// <summary>Povinně volitelný předmět of a ZŠ's grade: its hour is one
    /// of these subjects' (the RVP's minima and totals still met).</summary>
    private static readonly Dictionary<int, string> ElectiveHourFrom = new() { [6] = "Vv", [8] = "M", [9] = "Čj" };

    /// <summary>A subject a pupil chooses: its name, abbreviation and the
    /// subject its teacher teaches.</summary>
    private sealed record Topic(string Name, string Abbr, string Teacher);

    /// <summary>What a grade's pupils choose from for the plan's
    /// "Povinně volitelný předmět".</summary>
    private static readonly Dictionary<int, Topic[]> ElectiveTopics = new()
    {
        [6] = [new("Dramatická výchova", "DV", "Čj"), new("Domácnost", "Dom", "Pč"), new("Informatika plus", "Inf+", "Inf"), new("Rozvoj pohybových dovedností", "RPD", "Tv")],
        [8] = [new("Matematické praktikum", "MP", "M"), new("Čtenářský seminář", "ČtS", "Čj"), new("Fyzikální hrátky", "FH", "F"), new("Kouzla matematiky", "KM", "M"), new("Cambridge English", "CE", "Aj")],
        [9] = [new("Ekologický seminář", "EkS", "Př"), new("Mediální výchova", "MeV", "Ov"), new("Historický seminář", "HiS", "D"), new("Rozšiřující čeština", "RČj", "Čj"), new("Rozšiřující matematika", "RM", "M")],
    };

    /// <summary>A gymnázium's seminars: blocks of the plan's "Seminář"
    /// hours, in each a seminar of the pupil's choice.</summary>
    private static readonly Dictionary<int, (string Label, int Hours, Topic[] Topics)[]> SeminarBlocks = new()
    {
        [3] =
        [
            ("I", 3, [new("Seminář z chemie", "SCh", "Ch"), new("Seminář z dějepisu", "SD", "D"), new("Aplikovaná matematika", "AM", "M"), new("Seminář z fyziky", "SF", "F")]),
            ("II", 2, [new("Seminář z biologie", "SBi", "Bi"), new("Psychologie", "Psy", "ZSV"), new("Dějiny kultury", "DK", "D")]),
        ],
        [4] =
        [
            ("I", 2, [new("Seminář z fyziky", "SF", "F"), new("Dějiny kultury", "DK", "D"), new("Programování", "Prog", "Inf"), new("Seminář z chemie", "SCh", "Ch")]),
            ("II", 2, [new("Základy práva", "ZPr", "ZSV"), new("Seminář ze ZSV", "SZSV", "ZSV"), new("Seminář z biologie", "SBi", "Bi")]),
            ("III", 2, [new("American history and culture", "AHC", "Aj"), new("Konverzace v Nj", "KNj", "Nj"), new("Seminář z dějepisu", "SD", "D"), new("Aplikovaná matematika", "AM", "M")]),
        ],
    };

    private static readonly string[] TopicColors = ["#e9d5ff", "#c7d2fe", "#bbf7d0", "#fed7aa", "#fbcfe8", "#a5f3fc", "#fde68a", "#d9f99d"];

    /// <summary>Weekly hours per grade (index 0 = 1st grade), meeting
    /// <see cref="ElementaryProgram"/>.</summary>
    private const string ElementaryProgram = "ZV2021", SecondaryProgram = "G4";

    private static readonly Dictionary<string, int>[] ElementaryPlan =
    [
        new() { ["Čj"] = 8, ["M"] = 4, ["Aj"] = 1, ["Prv"] = 2, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 8, ["M"] = 5, ["Aj"] = 1, ["Prv"] = 2, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Prv"] = 3, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Přv"] = 2, ["Vl"] = 2, ["Inf"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Přv"] = 2, ["Vl"] = 2, ["Inf"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 5, ["M"] = 4, ["Aj"] = 3, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Vz"] = 1, ["Pč"] = 1 },
        new() { ["Čj"] = 4, ["M"] = 4, ["Aj"] = 3, ["Dcj"] = 2, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Vz"] = 1, ["Pč"] = 1 },
        new() { ["Čj"] = 4, ["M"] = 5, ["Aj"] = 3, ["Dcj"] = 2, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Ch"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 5, ["M"] = 5, ["Aj"] = 3, ["Dcj"] = 2, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Ch"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2, ["Pč"] = 1 },
    ];

    private static readonly Dictionary<string, int>[] SecondaryPlan =
    [
        new() { ["Čj"] = 4, ["Aj"] = 4, ["Dcj"] = 3, ["M"] = 5, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 1, ["Inf"] = 2, ["Vv"] = 2, ["Tv"] = 2 },
        new() { ["Čj"] = 4, ["Aj"] = 4, ["Dcj"] = 3, ["M"] = 5, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 1, ["Inf"] = 2, ["Vv"] = 2, ["Tv"] = 2 },
        new() { ["Čj"] = 3, ["Aj"] = 3, ["Dcj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 2, ["Inf"] = 1, ["Tv"] = 2, ["Sem"] = 5 },
        new() { ["Čj"] = 4, ["Aj"] = 4, ["Dcj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["ZSV"] = 2, ["Tv"] = 2, ["Sem"] = 6 },
    ];

    /// <summary>Aprobace pairs a newly needed teacher is drawn from.</summary>
    private static readonly (string, string)[] ElementaryQualifications =
    [
        ("Čj", "D"), ("Čj", "Ov"), ("M", "F"), ("M", "Inf"), ("Aj", "Nj"), ("Aj", "Z"), ("Př", "Ch"),
        ("Z", "Tv"), ("Hv", "Vv"), ("Tv", "Pč"), ("Tv", "Vz"), ("D", "Ov"), ("F", "Inf"), ("Př", "Z"), ("Vv", "Pč"),
        ("Aj", "Fj"), ("Nj", "Šj"), ("Čj", "Fj"),
    ];

    private static readonly (string, string)[] SecondaryQualifications =
    [
        ("Čj", "D"), ("Čj", "ZSV"), ("M", "F"), ("M", "Inf"), ("Aj", "Nj"), ("Aj", "Z"), ("Bi", "Ch"),
        ("Z", "Tv"), ("Ch", "F"), ("D", "ZSV"), ("Tv", "Bi"), ("Vv", "Čj"), ("Inf", "Sem"), ("M", "Sem"),
        ("Aj", "Fj"), ("Nj", "Šj"), ("Fj", "Šj"),
    ];

    private static readonly (TimeOnly Start, TimeOnly End)[] Bells =
    [
        (new(8, 0), new(8, 45)), (new(8, 55), new(9, 40)), (new(10, 0), new(10, 45)), (new(10, 55), new(11, 40)),
        (new(11, 50), new(12, 35)), (new(12, 45), new(13, 30)), (new(13, 40), new(14, 25)),
        (new(14, 35), new(15, 20)),
    ];

    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private Random rng = new(0);
    private DateTimeOffset now;
    private School school = null!;
    private DemoSchoolCalendar calendar = null!;
    private readonly HashSet<string> usernames = [];
    private readonly List<User> users = [];
    private readonly List<DivisionInfo> divisions = [];
    private readonly List<GroupInfo> optionalGroups = [];

    /// <summary>The subjects pupils chose from for a placeholder row of the plan.</summary>
    private readonly Dictionary<string, SubjectDef> topicSubjects = [];
    private Dictionary<UserRole, Role> roles = [];

    private sealed class TeacherInfo
    {
        public required User User { get; init; }
        public required Person Person { get; init; }
        public required HashSet<string> Qualifications { get; init; }
        public int Load { get; set; }
        public bool FirstStage { get; init; }
        public ClassInfo? ClassOf { get; set; }
    }

    private sealed class ClassInfo
    {
        public required SchoolClass Class { get; init; }
        public required int Grade { get; init; }
        public required Classroom Homeroom { get; init; }
        public required Dictionary<string, int> Plan { get; init; }
        public Dictionary<string, TeacherInfo> TeacherBySubject { get; } = [];
        public List<StudentInfo> Students { get; } = [];
        public int TargetSize { get; init; }

        /// <summary>Lessons per day (Monday first).</summary>
        public int[] DayLengths { get; set; } = [];
        public TeacherInfo ClassTeacher { get; set; } = null!;

        /// <summary>Subjects of the plan taught in the groups of a division.</summary>
        public HashSet<string> Divided { get; } = [];
    }

    /// <summary>The grade's classes taught <see cref="PlanSubject"/> (or
    /// <see cref="Hours"/> of it) in groups at the same time.</summary>
    private sealed class DivisionInfo
    {
        public required string Name { get; init; }
        public required string PlanSubject { get; init; }
        public required int Hours { get; init; }
        public required List<ClassInfo> Classes { get; init; }

        /// <summary>Rooms its groups take; null = the subject's.</summary>
        public List<Classroom>? Rooms { get; init; }

        public List<GroupInfo> Groups { get; } = [];
        public GroupDivision Entity { get; set; } = null!;
    }

    /// <summary>A group of a division, or a nepovinný předmět's on its own
    /// (<see cref="Optional"/>).</summary>
    private sealed class GroupInfo
    {
        public required string Name { get; init; }
        public required string Subject { get; init; }

        /// <summary>The subject its teacher is picked by.</summary>
        public required string TeacherSubject { get; init; }

        public TeacherInfo Teacher { get; set; } = null!;
        public List<StudentInfo> Members { get; } = [];
        public StudentGroup Entity { get; set; } = null!;

        public OptionalDef? Optional { get; init; }

        /// <summary>Of an optional group: its members'.</summary>
        public List<ClassInfo> Classes { get; set; } = [];

        /// <summary>Of an optional group: when it's taught.</summary>
        public List<(int Day, int Period, Classroom Room)> Slots { get; } = [];
    }

    private sealed class StudentInfo
    {
        public required StudentProfile Student { get; init; }
        public required ClassInfo Class { get; init; }
        public required FamilyInfo Family { get; init; }
        public double Ability { get; init; }
        public double Sickliness { get; init; }
    }

    private sealed class FamilyInfo
    {
        public required (string Male, string Female) Surname { get; init; }
        public List<(Person Person, ParentProfile Profile, User? User)> Parents { get; } = [];
        public List<StudentInfo> Children { get; } = [];
        public required string Street { get; init; }
    }

    public async Task<DemoFeaturedUsers> GenerateAsync(School target, DemoSchoolOptions options, DateTimeOffset now)
    {
        rng = new Random(options.Seed);
        school = target;
        this.now = now.ToUniversalTime();
        calendar = new DemoSchoolCalendar(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Prague).DateTime));
        usernames.Clear();
        users.Clear();
        divisions.Clear();
        optionalGroups.Clear();
        topicSubjects.Clear();
        roles = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.SchoolId == target.Id && r.BuiltIn != null)
            .ToDictionaryAsync(r => r.BuiltIn!.Value);

        var elementary = target.Type == SchoolType.Elementary;
        var seed = options.Seed;
        var plan = (elementary ? ElementaryPlan : SecondaryPlan).Select(p => new Dictionary<string, int>(p)).ToArray();

        // Not every school offers everything: a second language besides Nj
        // (two with more classes in a grade), some povinně volitelné and
        // nepovinné předměty.
        var languages = new[] { "Fj", "Šj" }.OrderBy(_ => rng.Next())
            .Take(options.ClassesPerGrade > 1 ? rng.Next(1, 3) : 1).Prepend("Nj").ToList();
        var electiveGrades = elementary ? ElectiveHourFrom.Keys.Where(_ => rng.NextDouble() < 0.7).ToList() : [];
        foreach (var grade in electiveGrades)
        {
            plan[grade - 1][ElectiveHourFrom[grade]]--;
            plan[grade - 1]["PVP"] = 1;
        }
        var optional = (elementary ? ElementaryOptional : SecondaryOptional).Where(_ => rng.NextDouble() < 0.6).ToList();
        var subjectDefs = (elementary ? ElementarySubjects : SecondarySubjects)
            .Where(d => !OfferedSubjects.Contains(d.Abbr) || languages.Contains(d.Abbr) || (d.Abbr == "PVP" && electiveGrades.Count > 0))
            .Concat(optional.Select(o => o.Subject))
            .ToList();

        foreach (var (period, (start, end)) in Bells.Select((b, i) => (i + 1, b)))
        {
            db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = school.Id, PeriodNumber = period, StartTime = start, EndTime = end });
        }

        var roomGroups = CreateSpecialRooms(elementary, options.ClassesPerGrade);
        var admin = NewPerson(rng.Next(2) == 0, adult: true, [UserRole.Admin]);
        school.DirectorName = $"Mgr. {admin.Person.FullName}";
        FillSchoolDetails();

        var classes = CreateClasses(plan, options.ClassesPerGrade, elementary);
        PlanDivisions(classes, elementary, roomGroups, languages, optional);
        var subjects = subjectDefs.Concat(topicSubjects.Values).ToDictionary(d => d.Abbr, d =>
        {
            var subject = new Subject
            {
                Name = d.Name,
                Abbreviation = d.Abbr,
                Color = d.Color,
                SchoolId = school.Id,
            };
            // The first room, else any of its kind.
            if (d.RoomGroup is { } g)
            {
                var group = roomGroups[g];
                subject.Classrooms.Add(new SubjectClassroom { Position = 0, Classroom = group[0] });
                if (group.Count > 1)
                {
                    subject.Classrooms.Add(new SubjectClassroom { Position = 1, Type = group[0].Type });
                }
                if (g is "art" or "music" or "workshop")
                {
                    subject.Classrooms.Add(new SubjectClassroom { Position = subject.Classrooms.Count, Type = ClassroomType.Homeroom });
                }
            }
            db.Subjects.Add(subject);
            return subject;
        });
        var teachers = AssignTeachers(classes, elementary);
        CreateFamilies(classes, elementary);
        FillGroups(classes);
        BuildTimetable(classes, subjects, roomGroups);

        // Na prvním stupni aprobace pro předměty své třídy v 1.–5. ročníku;
        // nasmlouváno mají, co učí.
        db.TeacherProfiles.AddRange(teachers.Select(t => new TeacherProfile
        {
            Person = t.Person,
            SchoolId = school.Id,
            ContractedHours = t.Load,
            Qualifications = t.FirstStage
                ? [.. t.ClassOf!.Plan.Keys.Where(k => k is not "Aj" and not "Inf").Concat(t.Qualifications).Distinct().Select(k => new TeacherQualification
                {
                    SchoolId = school.Id, Subject = subjects[k], GradeFrom = 1, GradeTo = 5,
                })]
                : [.. t.Qualifications.Where(subjects.ContainsKey).Select(k => new TeacherQualification
                {
                    SchoolId = school.Id, Subject = subjects[k],
                })],
        }));

        // One save for the whole people/structure graph — EF orders the
        // inserts by the navigations; the agendas below need real ids.
        await SaveAsync();
        foreach (var division in divisions)
        {
            division.Entity.ClassIds = [.. division.Classes.Select(c => c.Class.Id)];
        }

        GenerateGrades(classes, subjects);
        GenerateAbsences(classes);
        GenerateBehavior(classes);
        GenerateHomework(classes, subjects);
        var featured = PickFeatured(admin.User, classes);
        GenerateMessages(admin, classes, featured);
        GenerateEvents(admin.User, classes, elementary);
        GeneratePayments(admin.User, classes, elementary);
        // Own generator: photos don't shift what the seed gives everything else.
        await GeneratePhotosAsync(new Random(seed + 1), admin.Person, teachers, classes, featured);
        await SaveAsync();
        return featured;
    }

    // ---- photos -------------------------------------------------------

    /// <summary>Most staff, a share of the pupils (with a recorded consent —
    /// others have none, to show photos need one) and of the parents get an
    /// avatar. The featured pupil has an approved photo; a child of the
    /// featured parent has one waiting for that parent's approval.</summary>
    private async Task GeneratePhotosAsync(
        Random photoRng, Person admin, List<TeacherInfo> teachers, List<ClassInfo> classes, DemoFeaturedUsers featured)
    {
        if (photos is null)
        {
            return;
        }

        async Task<string> Avatar(bool female, bool adult) =>
            await photos.SaveProcessedAsync(school.Id,
                DemoAvatars.Get(DemoAvatars.SetFor(female, adult), photoRng.Next(DemoAvatars.VariantsPerSet)));

        foreach (var person in teachers.Select(t => t.Person).Prepend(admin))
        {
            if (person == admin || photoRng.NextDouble() < 0.85)
            {
                person.PhotoKey = await Avatar(IsFemale(person.FirstName), adult: true);
            }
        }

        var students = classes.SelectMany(c => c.Students).ToList();
        var featuredStudent = students.First(s => s.Student.Person!.User?.Id == featured.StudentId);
        var featuredFamily = students.First(s => s.Family.Parents.Any(p => p.User?.Id == featured.ParentId)).Family;
        var pendingChild = featuredFamily.Children.FirstOrDefault(c => c != featuredStudent) ?? featuredFamily.Children[0];

        foreach (var info in students)
        {
            var student = info.Student;
            var isFeatured = info == featuredStudent || info == pendingChild;
            if (!isFeatured && photoRng.NextDouble() >= 0.6)
            {
                continue; // no consent
            }
            var guardian = info.Family.Parents[0].Person;
            student.PhotoConsentGivenBy = guardian.FullName;
            student.PhotoConsentAt = Past(now.AddDays(-photoRng.Next(1, 120)));
            if (!isFeatured && photoRng.NextDouble() >= 0.75)
            {
                continue; // consent, but no photo yet
            }
            student.Person!.PhotoKey = await Avatar(student.Gender == Gender.Female, adult: false);
            student.Person.PhotoStatus = info == pendingChild && info != featuredStudent ? PhotoStatus.Pending : PhotoStatus.Approved;
        }

        foreach (var (person, _, user) in students.SelectMany(s => s.Family.Parents).Distinct())
        {
            if (user is not null && photoRng.NextDouble() < 0.3)
            {
                person.PhotoKey = await Avatar(IsFemale(person.FirstName), adult: true);
            }
        }
    }

    private static bool IsFemale(string firstName) =>
        DemoNames.AdultFemaleFirstNames.Contains(firstName) || DemoNames.FemaleFirstNames.Contains(firstName);

    // ---- structure ----------------------------------------------------

    private void FillSchoolDetails()
    {
        school.Street = $"{Pick(DemoNames.Streets)} {rng.Next(1, 60)}";
        school.City = "Ukázkov";
        school.PostalCode = "123 45";
        school.Email = "info@skola.example.com";
        school.Phone = $"+420 {rng.Next(200, 600)} {rng.Next(100, 999)} {rng.Next(100, 999)}";
        // Well-known sample IBAN (ČNB documentation) — the QR code renders,
        // nobody's real account is behind it.
        school.BankAccountNumber = "CZ6508000000192000145399";
    }

    private Dictionary<string, List<Classroom>> CreateSpecialRooms(bool elementary, int perGrade)
    {
        Classroom Room(string name, string? shortName, ClassroomType type, int capacity)
        {
            var room = new Classroom { Name = name, ShortName = shortName, Type = type, Capacity = capacity, SchoolId = school.Id };
            db.Classrooms.Add(room);
            return room;
        }

        // Two at least: girls and boys have Tv at the same time.
        var gyms = new List<Classroom>
        {
            Room("Tělocvična", "TV1", ClassroomType.Gym, 30), Room("Malá tělocvična", "TV2", ClassroomType.Gym, 24),
        };
        if (perGrade > 2)
        {
            gyms.Add(Room("Školní hřiště", "HŘ", ClassroomType.Gym, 60));
        }
        var computers = new List<Classroom> { Room("Počítačová učebna", "PC1", ClassroomType.Computer, 24) };
        if (perGrade > 2)
        {
            computers.Add(Room("Počítačová učebna 2", "PC2", ClassroomType.Computer, 24));
        }
        var labs = elementary
            ? new List<Classroom> { Room("Laboratoř", "LAB", ClassroomType.Laboratory, 24) }
            : [Room("Laboratoř fyziky", "LF", ClassroomType.Laboratory, 24), Room("Laboratoř chemie", "LCH", ClassroomType.Laboratory, 24)];
        var groups = new Dictionary<string, List<Classroom>>
        {
            ["gym"] = gyms,
            ["computer"] = computers,
            ["lab"] = labs,
            ["art"] = [Room("Výtvarný ateliér", "VV", ClassroomType.Specialized, 30)],
            ["language"] = [Room("Jazyková učebna", "JAZ", ClassroomType.Specialized, 24)],
        };
        if (elementary)
        {
            groups["music"] = [Room("Hudebna", "HV", ClassroomType.Specialized, 30)];
            groups["workshop"] = [Room("Dílna", "DÍL", ClassroomType.Workshop, 16)];
        }
        else
        {
            groups["seminar"] = [Room("Seminární učebna", "SEM", ClassroomType.Specialized, 24)];
        }
        return groups;
    }

    private List<ClassInfo> CreateClasses(Dictionary<string, int>[] plan, int perGrade, bool elementary)
    {
        var classes = new List<ClassInfo>();
        var roomsPerFloor = new Dictionary<int, int>();
        for (var grade = 1; grade <= plan.Length; grade++)
        {
            for (var i = 0; i < perGrade; i++)
            {
                var name = $"{grade}.{(char)('A' + i)}";
                var schoolClass = new SchoolClass
                {
                    Name = name, Rocnik = grade, TeachingLanguage = "čeština", SchoolId = school.Id,
                    RvpProgramCode = elementary ? ElementaryProgram : SecondaryProgram,
                };
                var floor = elementary ? (grade <= 5 ? 1 : 2) : (grade <= 2 ? 1 : 2);
                roomsPerFloor[floor] = roomsPerFloor.GetValueOrDefault(floor) + 1;
                var homeroom = new Classroom
                {
                    Name = $"{floor * 100 + roomsPerFloor[floor]}",
                    Type = ClassroomType.Homeroom,
                    Capacity = 30,
                    SchoolId = school.Id,
                };
                schoolClass.Homeroom = homeroom;
                db.SchoolClasses.Add(schoolClass);
                db.Classrooms.Add(homeroom);
                classes.Add(new ClassInfo
                {
                    Class = schoolClass,
                    Grade = grade,
                    Homeroom = homeroom,
                    Plan = plan[grade - 1],
                    TargetSize = rng.Next(10, 21),
                });
            }
        }
        return classes;
    }

    /// <summary>Divisions of a grade's classes: Tv of girls and boys (the
    /// upper grades of a ZŠ, every grade of a gymnázium), the second
    /// language, a ZŠ's povinně volitelný předmět, a gymnázium's seminars.
    /// Their groups are in the grade's homerooms and the shared rooms. And
    /// the nepovinné předměty, each a group on its own.</summary>
    private void PlanDivisions(
        List<ClassInfo> classes, bool elementary, Dictionary<string, List<Classroom>> roomGroups,
        List<string> languages, List<OptionalDef> optional)
    {
        var shared = roomGroups["language"].Concat(roomGroups.GetValueOrDefault("seminar") ?? []).ToList();
        foreach (var grade in classes.GroupBy(c => c.Grade))
        {
            var gradeClasses = grade.ToList();
            var plan = gradeClasses[0].Plan;
            var rooms = gradeClasses.Select(c => c.Homeroom).Concat(shared).ToList();
            var groupCount = Math.Max(2, gradeClasses.Count);
            if (!elementary || grade.Key >= 6)
            {
                Add("Tv", $"Tv {grade.Key}. ročník", plan["Tv"], [($"Tv dívky {grade.Key}", "Tv", "Tv"), ($"Tv chlapci {grade.Key}", "Tv", "Tv")], null);
            }
            if (plan.TryGetValue("Dcj", out var languageHours))
            {
                Add("Dcj", $"Další cizí jazyk {grade.Key}. ročník", languageHours,
                    [.. languages.Take(groupCount).Select(l => ($"{l} {grade.Key}", l, l))], rooms);
            }
            if (plan.ContainsKey("PVP"))
            {
                Add("PVP", $"Povinně volitelný předmět {grade.Key}. ročník", plan["PVP"], Topics(ElectiveTopics[grade.Key], "PVP"), rooms);
            }
            if (plan.ContainsKey("Sem"))
            {
                foreach (var (label, hours, topics) in SeminarBlocks[grade.Key])
                {
                    Add("Sem", $"Semináře {label} {grade.Key}. ročník", hours, Topics(topics, "Sem"), rooms);
                }
            }

            List<(string, string, string)> Topics(Topic[] topics, string planRow) =>
                [.. topics.OrderBy(_ => rng.Next()).Take(groupCount).Select(t =>
                {
                    topicSubjects.TryAdd(t.Abbr, new SubjectDef(t.Abbr, t.Name, TopicColors[topicSubjects.Count % TopicColors.Length]));
                    return ($"{t.Name} {grade.Key}", t.Abbr, t.Teacher);
                })];

            void Add(string planSubject, string name, int hours, List<(string Name, string Subject, string Teacher)> groups, List<Classroom>? groupRooms)
            {
                var division = new DivisionInfo { Name = name, PlanSubject = planSubject, Hours = hours, Classes = gradeClasses, Rooms = groupRooms };
                division.Groups.AddRange(groups.Select(g => new GroupInfo { Name = g.Name, Subject = g.Subject, TeacherSubject = g.Teacher }));
                divisions.Add(division);
                gradeClasses.ForEach(c => c.Divided.Add(planSubject));
            }
        }
        optionalGroups.AddRange(optional.Select(o => new GroupInfo
        {
            Name = $"{o.Subject.Name} {o.FromGrade}.–{o.ToGrade}.",
            Subject = o.Subject.Abbr,
            TeacherSubject = o.TeacherOf ?? o.Subject.Abbr,
            Optional = o,
        }));
    }

    /// <summary>Each pupil in one group of each division of their class —
    /// girls and boys apart in Tv, otherwise as they chose; up to two
    /// nepovinné předměty each.</summary>
    private void FillGroups(List<ClassInfo> classes)
    {
        foreach (var division in divisions)
        {
            var pupils = division.Classes.SelectMany(c => c.Students).ToList();
            if (division.PlanSubject == "Tv")
            {
                division.Groups[0].Members.AddRange(pupils.Where(p => p.Student.Gender == Gender.Female));
                division.Groups[1].Members.AddRange(pupils.Where(p => p.Student.Gender != Gender.Female));
                continue;
            }
            foreach (var (pupil, i) in pupils.OrderBy(_ => rng.Next()).Select((p, i) => (p, i)))
            {
                division.Groups[i % division.Groups.Count].Members.Add(pupil);
            }
        }
        var signedUp = new Dictionary<StudentInfo, int>();
        foreach (var group in optionalGroups.ToList())
        {
            var o = group.Optional!;
            var candidates = classes.Where(c => c.Grade >= o.FromGrade && c.Grade <= o.ToGrade)
                .SelectMany(c => c.Students)
                .Where(p => signedUp.GetValueOrDefault(p) < 2)
                .OrderBy(_ => rng.Next())
                .Take(rng.Next(o.MinPupils, o.MaxPupils + 1))
                .ToList();
            if (candidates.Count < o.MinPupils)
            {
                Drop(group);
                continue;
            }
            group.Members.AddRange(candidates);
            candidates.ForEach(p => signedUp[p] = signedUp.GetValueOrDefault(p) + 1);
            group.Classes = [.. candidates.Select(p => p.Class).Distinct().OrderBy(c => c.Class.Name, StringComparer.Ordinal)];
        }
    }

    private void Drop(GroupInfo group)
    {
        optionalGroups.Remove(group);
        group.Teacher.Load -= group.Optional!.Hours;
    }

    private List<TeacherInfo> AssignTeachers(List<ClassInfo> classes, bool elementary)
    {
        var teachers = new List<TeacherInfo>();
        var pairs = elementary ? ElementaryQualifications : SecondaryQualifications;

        TeacherInfo NewTeacher(IEnumerable<string> qualifications, bool firstStage)
        {
            var (user, person) = NewPerson(rng.NextDouble() < 0.75, adult: true, [UserRole.Teacher]);
            var teacher = new TeacherInfo { User = user, Person = person, Qualifications = [.. qualifications], FirstStage = firstStage };
            teachers.Add(teacher);
            return teacher;
        }

        TeacherInfo TeacherFor(string subject, int hours, IReadOnlyCollection<TeacherInfo>? except = null)
        {
            var candidates = teachers
                .Where(t => !t.FirstStage && t.Qualifications.Contains(subject) && t.Load + hours <= 20 && except?.Contains(t) != true)
                .OrderBy(t => t.Load)
                .ToList();
            if (candidates.Count > 0)
            {
                return candidates[0];
            }
            var matching = pairs.Where(p => p.Item1 == subject || p.Item2 == subject).ToList();
            var (a, b) = matching.Count > 0 ? Pick(matching) : (subject, subject);
            return NewTeacher(a == b ? [a] : [a, b], firstStage: false);
        }

        foreach (var info in classes)
        {
            var firstStage = elementary && info.Grade <= 5;
            if (firstStage)
            {
                // Na prvním stupni učí třídní skoro všechno sám; angličtinu a
                // informatiku specialisté.
                var classTeacher = NewTeacher([], firstStage: true);
                classTeacher.ClassOf = info;
                info.ClassTeacher = classTeacher;
                foreach (var (subject, hours) in info.Plan)
                {
                    var teacher = subject is "Aj" or "Inf" ? TeacherFor(subject, hours) : classTeacher;
                    teacher.Load += hours;
                    info.TeacherBySubject[subject] = teacher;
                }
                continue;
            }
            foreach (var (subject, hours) in info.Plan.Where(p => !info.Divided.Contains(p.Key)).OrderByDescending(p => p.Value))
            {
                var teacher = TeacherFor(subject, hours);
                teacher.Load += hours;
                info.TeacherBySubject[subject] = teacher;
            }
        }

        // Groups of a division are taught at once, each by someone else;
        // a seminar or a povinně volitelný by a teacher of its field.
        foreach (var division in divisions)
        {
            foreach (var group in division.Groups)
            {
                group.Teacher = TeacherFor(
                    group.TeacherSubject, division.Hours, [.. division.Groups.Where(g => g.Teacher is not null).Select(g => g.Teacher)]);
                group.Teacher.Qualifications.Add(group.Subject);
                group.Teacher.Load += division.Hours;
            }
        }
        // A nepovinný: a first-stage one by a class teacher of its grades,
        // one nobody teaches (náboženství, a native speaker) from outside.
        foreach (var group in optionalGroups)
        {
            var o = group.Optional!;
            group.Teacher = o.TeacherOf == "1st"
                ? Pick(teachers.Where(t => t.FirstStage && t.ClassOf!.Grade >= o.FromGrade && t.ClassOf.Grade <= o.ToGrade).ToList())
                : TeacherFor(group.TeacherSubject, o.Hours);
            group.Teacher.Qualifications.Add(group.Subject);
            group.Teacher.Load += o.Hours;
        }

        // Upper classes: the class teacher is whoever teaches the class most.
        foreach (var info in classes.Where(c => c.ClassTeacher is null))
        {
            var classTeacher = info.TeacherBySubject
                .GroupBy(p => p.Value)
                .Select(g => (Teacher: g.Key, Hours: g.Sum(p => info.Plan[p.Key])))
                .Where(x => x.Teacher.ClassOf is null)
                .OrderByDescending(x => x.Hours)
                .Select(x => x.Teacher)
                .FirstOrDefault()
                ?? teachers.FirstOrDefault(t => !t.FirstStage && t.ClassOf is null)
                ?? NewTeacher([Pick(info.Plan.Keys.ToList())], firstStage: false);
            classTeacher.ClassOf = info;
            info.ClassTeacher = classTeacher;
        }

        foreach (var info in classes)
        {
            info.Class.ClassTeacher = info.ClassTeacher.User;
        }
        return teachers;
    }

    private void CreateFamilies(List<ClassInfo> classes, bool elementary)
    {
        var grades = classes.Select(c => c.Grade).Distinct().ToList();
        var variableSymbolCounters = new Dictionary<int, int>();
        var czechRepublicExists = db.Countries.Any(c => c.Code == "203");

        bool HasRoom(int grade) => classes.Any(c => c.Grade == grade && c.Students.Count < c.TargetSize);

        while (classes.Any(c => c.Students.Count < c.TargetSize))
        {
            var roll = rng.NextDouble();
            var childCount = roll < 0.7 ? 1 : roll < 0.95 ? 2 : 3;
            var freeGrades = grades.Where(HasRoom).OrderBy(_ => rng.Next()).Take(childCount).ToList();
            var family = new FamilyInfo { Surname = Pick(DemoNames.Surnames), Street = $"{Pick(DemoNames.Streets)} {rng.Next(1, 200)}" };

            foreach (var grade in freeGrades)
            {
                var info = classes.Where(c => c.Grade == grade && c.Students.Count < c.TargetSize).OrderBy(c => c.Students.Count).First();
                var male = rng.Next(2) == 0;
                var first = Pick(male ? DemoNames.MaleFirstNames : DemoNames.FemaleFirstNames);
                if (family.Children.Any(c => c.Student.Person!.FirstName == first))
                {
                    first = Pick(male ? DemoNames.MaleFirstNames : DemoNames.FemaleFirstNames);
                }
                // Age at 1 September: 6 in the first grade of a ZŠ, 15 in the
                // first year of a SŠ.
                var ageAtStart = (elementary ? 5 : 14) + grade;
                var born = new DateOnly(calendar.StartYear - ageAtStart - 1, 9, 1).AddDays(rng.Next(365));
                var enrolledYear = calendar.StartYear - grade + 1;
                variableSymbolCounters[enrolledYear] = variableSymbolCounters.GetValueOrDefault(enrolledYear) + 1;
                var student = new StudentProfile
                {
                    Person = new Person
                    {
                        FirstName = first,
                        LastName = male ? family.Surname.Male : family.Surname.Female,
                        SchoolId = school.Id,
                    },
                    SchoolClass = info.Class,
                    SchoolId = school.Id,
                    DateOfBirth = born,
                    Gender = male ? Gender.Male : Gender.Female,
                    CitizenshipQualifier = "3",
                    CountryCode = czechRepublicExists ? "203" : null,
                    Street = family.Street,
                    PostalCode = "123 45",
                    CatchmentArea = rng.NextDouble() < 0.8,
                    EnrollmentDate = new DateOnly(enrolledYear, 9, 1),
                    DefaultVariableSymbol = $"{enrolledYear % 100:00}{variableSymbolCounters[enrolledYear]:0000}",
                };
                db.Students.Add(student);
                var studentInfo = new StudentInfo
                {
                    Student = student,
                    Class = info,
                    Family = family,
                    // Most pupils do fine; a few struggle.
                    Ability = 1 + 3.2 * Math.Pow(rng.NextDouble(), 1.6),
                    Sickliness = 0.4 + rng.NextDouble() * 1.4,
                };
                info.Students.Add(studentInfo);
                family.Children.Add(studentInfo);

                // Older pupils often have their own account.
                var accountChance = elementary ? (grade >= 4 ? 0.5 : 0) : 0.7;
                if (rng.NextDouble() < accountChance)
                {
                    StudentAccount(student);
                }
            }

            // Mother and father, or just one of them.
            var parentsRoll = rng.NextDouble();
            var hasMother = parentsRoll < 0.92;
            var hasFather = parentsRoll >= 0.85 || !hasMother;
            if (hasMother)
            {
                AddParent(family, male: false, account: rng.NextDouble() < 0.75);
            }
            if (hasFather)
            {
                AddParent(family, male: true, account: rng.NextDouble() < (hasMother ? 0.35 : 0.75));
            }
            foreach (var child in family.Children)
            {
                var contact = family.Parents[0].Person;
                child.Student.ContactEmail = contact.Email;
                child.Student.ContactPhone = contact.Phone;
            }
        }
    }

    private void AddParent(FamilyInfo family, bool male, bool account)
    {
        var first = Pick(male ? DemoNames.AdultMaleFirstNames : DemoNames.AdultFemaleFirstNames);
        var last = male ? family.Surname.Male : family.Surname.Female;
        var person = new Person { FirstName = first, LastName = last, SchoolId = school.Id, Phone = Phone() };
        person.Email = $"{DemoNames.Ascii($"{first}.{last}")}{rng.Next(10, 99)}@example.com";
        if (account)
        {
            person.User = NewUser(first, last, [UserRole.Parent]);
            person.Email = $"{person.User.Username}@example.com";
        }
        var profile = new ParentProfile { Person = person, SchoolId = school.Id };
        foreach (var child in family.Children)
        {
            profile.Children.Add(new ParentStudent { Student = child.Student, SchoolId = school.Id });
        }
        db.Persons.Add(person);
        db.ParentProfiles.Add(profile);
        family.Parents.Add((person, profile, person.User));
    }

    private (User User, Person Person) NewPerson(bool female, bool adult, List<UserRole> builtIns)
    {
        var first = Pick(female
            ? adult ? DemoNames.AdultFemaleFirstNames : DemoNames.FemaleFirstNames
            : adult ? DemoNames.AdultMaleFirstNames : DemoNames.MaleFirstNames);
        var surname = Pick(DemoNames.Surnames);
        var last = female ? surname.Female : surname.Male;
        var user = NewUser(first, last, builtIns);
        var person = new Person
        {
            FirstName = first,
            LastName = last,
            SchoolId = school.Id,
            User = user,
            Email = $"{user.Username}@example.com",
            Phone = Phone(),
        };
        db.Persons.Add(person);
        return (user, person);
    }

    private User NewUser(string first, string last, List<UserRole> builtIns)
    {
        var baseName = DemoNames.Ascii($"{first}.{last}");
        var username = baseName;
        for (var i = 2; !usernames.Add(username); i++)
        {
            username = $"{baseName}{i}";
        }
        // Empty hash never verifies: demo users are picked, not logged in.
        var user = new User
        {
            Username = username, PasswordHash = "", Roles = [.. builtIns.Select(r => roles[r])],
            ActiveRole = roles[builtIns[0]], SchoolId = school.Id,
        };
        db.Users.Add(user);
        users.Add(user);
        return user;
    }

    /// <summary>The pupil's own login, on their Person.</summary>
    private void StudentAccount(StudentProfile student)
    {
        var person = student.Person!;
        person.User = NewUser(person.FirstName, person.LastName, [UserRole.Student]);
        person.Email = $"{person.User.Username}@example.com";
    }

    private string Phone() => $"+420 {rng.Next(601, 799)} {rng.Next(100, 999)} {rng.Next(100, 999)}";

    // ---- timetable ----------------------------------------------------

    /// <summary>A lesson of a class, or of a division — all its classes,
    /// each group with its teacher and room.</summary>
    private sealed record Lesson(ClassInfo Class, string Subject, TeacherInfo Teacher, DivisionInfo? Division = null)
    {
        public IReadOnlyList<ClassInfo> Classes => Division?.Classes ?? [Class];
        public IReadOnlyList<TeacherInfo> Teachers => Division?.Groups.Select(g => g.Teacher).ToList() ?? [Teacher];
    }

    /// <summary><see cref="Rooms"/> by the division's groups.</summary>
    private sealed class Placement(Lesson lesson, int day, int period, List<Classroom> rooms)
    {
        public Lesson Lesson { get; } = lesson;
        public int Day { get; set; } = day;
        public int Period { get; set; } = period;
        public List<Classroom> Rooms { get; set; } = rooms;
    }

    private void BuildTimetable(
        List<ClassInfo> classes, Dictionary<string, Subject> subjects, Dictionary<string, List<Classroom>> roomGroups)
    {
        var classBusy = new HashSet<(ClassInfo, int, int)>();
        var subjectDay = new Dictionary<(ClassInfo, string, int), int>();
        var defs = (school.Type == SchoolType.Elementary ? ElementarySubjects : SecondarySubjects)
            .Concat(optionalGroups.Select(g => g.Optional!.Subject))
            .DistinctBy(d => d.Abbr)
            .ToDictionary(d => d.Abbr);

        // The longer days are the same for every class — the others end
        // early enough for the nepovinné předměty.
        var longDays = Enumerable.Range(0, 5).OrderBy(_ => rng.Next()).ToList();
        foreach (var info in classes)
        {
            var total = info.Plan.Values.Sum();
            var lengths = Enumerable.Repeat(total / 5, 5).ToArray();
            foreach (var day in longDays.Take(total % 5))
            {
                lengths[day]++;
            }
            info.DayLengths = lengths;
        }

        var lessons = classes
            .SelectMany(c => c.Plan.Where(p => !c.Divided.Contains(p.Key))
                .SelectMany(p => Enumerable.Repeat(new Lesson(c, p.Key, c.TeacherBySubject[p.Key]), p.Value)))
            .Concat(divisions.SelectMany(d =>
                Enumerable.Repeat(new Lesson(d.Classes[0], d.PlanSubject, d.Groups[0].Teacher, d), d.Hours)))
            .ToList();
        // Most constrained first: divisions, specialists teaching many
        // classes, rooms that are shared, then the class teachers' own lessons.
        var ordered = lessons
            .OrderByDescending(l => l.Division is null ? 0 : 1)
            .ThenBy(l => l.Teacher == l.Class.ClassTeacher && l.Teacher.FirstStage ? 1 : 0)
            .ThenByDescending(l => defs[l.Subject].RoomGroup is null ? 0 : 1)
            .ThenByDescending(l => l.Teacher.Load)
            .ThenBy(_ => rng.Next())
            .ToList();

        var teacherBusy = new Dictionary<(TeacherInfo, int, int), int>();
        var roomBusy = new HashSet<(Classroom, int, int)>();
        var placed = new List<Placement>();

        // Shared rooms that a lesson can do without (art, music, workshop
        // fall back to the class's own room); a gym or a lab can't.
        Classroom? RoomFor(Lesson lesson, int day, int period, Classroom? except = null)
        {
            if (defs[lesson.Subject].RoomGroup is not { } g)
            {
                return lesson.Class.Homeroom;
            }
            var free = roomGroups[g].FirstOrDefault(r => r == except || !roomBusy.Contains((r, day, period)));
            return free ?? (g is "art" or "music" or "workshop" ? lesson.Class.Homeroom : null);
        }

        // A division's groups: free rooms of its own or of the subject — the
        // biggest group in the biggest.
        List<Classroom> Pool(DivisionInfo division) => division.Rooms ?? roomGroups[defs[division.PlanSubject].RoomGroup!];
        List<Classroom>? RoomsFor(Lesson lesson, int day, int period)
        {
            if (lesson.Division is not { } division)
            {
                return RoomFor(lesson, day, period) is { } room ? [room] : null;
            }
            var pool = Pool(division);
            var free = new Queue<Classroom>(pool.Where(r => !roomBusy.Contains((r, day, period)))
                .OrderByDescending(r => division.Rooms is null ? r.Capacity : -pool.IndexOf(r)));
            var rooms = new Classroom?[division.Groups.Count];
            foreach (var (_, i) in division.Groups.Select((x, i) => (x, i)).OrderByDescending(x => x.x.Members.Count))
            {
                rooms[i] = free.TryDequeue(out var room) ? room : null;
            }
            return rooms.All(r => r is not null) ? [.. rooms.Select(r => r!)] : null;
        }

        List<Classroom> AnyRooms(Lesson lesson) =>
            lesson.Division is { } division
                ? [.. division.Groups.Select((_, i) => Pool(division)[i % Pool(division).Count])]
                : [roomGroups[defs[lesson.Subject].RoomGroup!][0]];

        foreach (var lesson in ordered)
        {
            var hours = lesson.Class.Plan[lesson.Subject];
            (int Day, int Period, List<Classroom> Rooms, double Score)? best = null;
            for (var day = 0; day < 5; day++)
            {
                var length = lesson.Classes.Min(c => c.DayLengths[day]);
                for (var period = 1; period <= length; period++)
                {
                    if (lesson.Classes.Any(c => classBusy.Contains((c, day, period))))
                    {
                        continue;
                    }
                    var score = rng.NextDouble();
                    if (lesson.Teachers.Any(t => teacherBusy.GetValueOrDefault((t, day, period)) > 0))
                    {
                        score += 100;
                    }
                    var rooms = RoomsFor(lesson, day, period);
                    if (rooms is null)
                    {
                        score += 10;
                    }
                    var sameDay = lesson.Classes.Max(c => subjectDay.GetValueOrDefault((c, lesson.Subject, day)));
                    if (sameDay > 0)
                    {
                        score += hours > 5 ? 2 * sameDay : 5 * sameDay;
                    }
                    if (best is null || score < best.Value.Score)
                    {
                        best = (day, period, rooms ?? AnyRooms(lesson), score);
                    }
                }
            }
            var (d, p, r, _) = best!.Value;
            foreach (var c in lesson.Classes)
            {
                classBusy.Add((c, d, p));
                subjectDay[(c, lesson.Subject, d)] = subjectDay.GetValueOrDefault((c, lesson.Subject, d)) + 1;
            }
            foreach (var t in lesson.Teachers)
            {
                teacherBusy[(t, d, p)] = teacherBusy.GetValueOrDefault((t, d, p)) + 1;
            }
            r.ForEach(room => roomBusy.Add((room, d, p)));
            placed.Add(new Placement(lesson, d, p, r));
        }

        // Repair: a class's lesson whose teacher is double-booked swaps slots
        // with another lesson of the class when that leaves fewer teachers
        // double-booked — again while it helps.
        int Over(TeacherInfo t, int day, int period, int change = 0) =>
            Math.Max(0, teacherBusy.GetValueOrDefault((t, day, period)) + change - 1);
        for (var pass = 0; pass < 10; pass++)
        {
            var improved = false;
            foreach (var clash in placed.Where(x => x.Lesson.Division is null && Over(x.Lesson.Teacher, x.Day, x.Period) > 0).ToList())
            {
                if (Over(clash.Lesson.Teacher, clash.Day, clash.Period) == 0)
                {
                    continue;
                }
                var (t1, t2) = (clash.Lesson.Teacher, (TeacherInfo?)null);
                int Gain(Placement o)
                {
                    t2 = o.Lesson.Teacher;
                    var before = Over(t1, clash.Day, clash.Period) + Over(t1, o.Day, o.Period) + Over(t2, clash.Day, clash.Period) + Over(t2, o.Day, o.Period);
                    var after = Over(t1, clash.Day, clash.Period, -1) + Over(t1, o.Day, o.Period, 1) + Over(t2, clash.Day, clash.Period, 1) + Over(t2, o.Day, o.Period, -1);
                    return before - after;
                }
                var swap = placed
                    .Where(o => o.Lesson.Division is null && o.Lesson.Class == clash.Lesson.Class && o != clash && o.Lesson.Teacher != t1
                        && RoomFor(clash.Lesson, o.Day, o.Period, except: o.Rooms[0]) is not null
                        && RoomFor(o.Lesson, clash.Day, clash.Period, except: clash.Rooms[0]) is not null)
                    .Select(o => (Lesson: o, Gain: Gain(o)))
                    .Where(x => x.Gain > 0)
                    .OrderByDescending(x => x.Gain)
                    .Select(x => x.Lesson)
                    .FirstOrDefault();
                if (swap is null)
                {
                    continue;
                }
                improved = true;
                teacherBusy[(clash.Lesson.Teacher, clash.Day, clash.Period)]--;
                teacherBusy[(swap.Lesson.Teacher, swap.Day, swap.Period)]--;
                roomBusy.Remove((clash.Rooms[0], clash.Day, clash.Period));
                roomBusy.Remove((swap.Rooms[0], swap.Day, swap.Period));
                var clashRoom = RoomFor(clash.Lesson, swap.Day, swap.Period)!;
                roomBusy.Add((clashRoom, swap.Day, swap.Period));
                var swapRoom = RoomFor(swap.Lesson, clash.Day, clash.Period)!;
                roomBusy.Add((swapRoom, clash.Day, clash.Period));
                (clash.Day, clash.Period, swap.Day, swap.Period) = (swap.Day, swap.Period, clash.Day, clash.Period);
                (clash.Rooms, swap.Rooms) = ([clashRoom], [swapRoom]);
                teacherBusy[(clash.Lesson.Teacher, clash.Day, clash.Period)] = teacherBusy.GetValueOrDefault((clash.Lesson.Teacher, clash.Day, clash.Period)) + 1;
                teacherBusy[(swap.Lesson.Teacher, swap.Day, swap.Period)] = teacherBusy.GetValueOrDefault((swap.Lesson.Teacher, swap.Day, swap.Period)) + 1;
            }
            if (!improved)
            {
                break;
            }
        }

        // Nepovinné předměty after their classes' lessons, the earliest
        // free; one that doesn't fit isn't offered.
        var pupilBusy = new HashSet<(StudentInfo, int, int)>();
        foreach (var group in optionalGroups.OrderByDescending(g => g.Members.Count).ToList())
        {
            var o = group.Optional!;
            var rooms = o.Subject.RoomGroup is { } g ? roomGroups[g] : [.. group.Classes.Select(c => c.Homeroom).Concat(roomGroups["language"])];
            for (var hour = 0; hour < o.Hours; hour++)
            {
                (int Day, int Period, Classroom Room, double Score)? best = null;
                for (var day = 0; day < 5; day++)
                {
                    for (var period = group.Classes.Max(c => c.DayLengths[day]) + 1; period <= Bells.Length; period++)
                    {
                        var room = rooms.FirstOrDefault(r => !roomBusy.Contains((r, day, period)));
                        if (room is null || teacherBusy.GetValueOrDefault((group.Teacher, day, period)) > 0
                            || group.Members.Any(m => pupilBusy.Contains((m, day, period))))
                        {
                            continue;
                        }
                        var score = period + rng.NextDouble() + (group.Slots.Any(s => s.Day == day) ? 5 : 0);
                        if (best is null || score < best.Value.Score)
                        {
                            best = (day, period, room, score);
                        }
                    }
                }
                if (best is not { } slot)
                {
                    break;
                }
                group.Slots.Add((slot.Day, slot.Period, slot.Room));
                teacherBusy[(group.Teacher, slot.Day, slot.Period)] = 1;
                roomBusy.Add((slot.Room, slot.Day, slot.Period));
                group.Members.ForEach(m => pupilBusy.Add((m, slot.Day, slot.Period)));
            }
            if (group.Slots.Count < o.Hours)
            {
                foreach (var (day, period, room) in group.Slots)
                {
                    teacherBusy.Remove((group.Teacher, day, period));
                    roomBusy.Remove((room, day, period));
                    group.Members.ForEach(m => pupilBusy.Remove((m, day, period)));
                }
                Drop(group);
            }
        }

        var version = new TimetableVersion
        {
            SchoolId = school.Id,
            Name = TimetableVersions.SchoolYearName(calendar.Start),
            ValidFrom = calendar.Start,
            Status = TimetableVersionStatus.Published,
            CreatedAt = now,
            PublishedAt = now,
        };
        db.TimetableVersions.Add(version);
        foreach (var grade in classes.GroupBy(c => c.Grade))
        {
            foreach (var (subject, hours) in grade.First().Plan)
            {
                db.CurriculumItems.Add(new CurriculumItem
                {
                    SchoolId = school.Id, TimetableVersion = version, Grade = grade.Key, Subject = subjects[subject], HoursPerWeek = hours,
                });
            }
        }
        foreach (var info in classes)
        {
            foreach (var (subject, teacher) in info.TeacherBySubject)
            {
                db.TeachingAssignments.Add(new TeachingAssignment
                {
                    SchoolId = school.Id, TimetableVersion = version, SchoolClass = info.Class, Subject = subjects[subject],
                    Teacher = teacher.User,
                });
            }
        }

        StudentGroup Group(GroupInfo group, IEnumerable<ClassInfo> groupClasses, GroupDivision? division, decimal? hours) =>
            group.Entity = new StudentGroup
            {
                SchoolId = school.Id,
                TimetableVersion = version,
                GroupDivision = division,
                Name = group.Name,
                Subject = subjects[group.Subject],
                HoursPerWeek = hours,
                Teacher = group.Teacher.User,
                Classes = [.. groupClasses.Select(c => new StudentGroupClass { SchoolClass = c.Class, SchoolId = school.Id })],
                Members = [.. group.Members.Select(m => new StudentGroupMember { Student = m.Student, SchoolId = school.Id })],
            };
        foreach (var division in divisions)
        {
            // Own hours when the plan's are of more of them (seminars).
            decimal? hours = division.Hours == division.Classes[0].Plan[division.PlanSubject] ? null : division.Hours;
            division.Entity = new GroupDivision
            {
                SchoolId = school.Id, TimetableVersion = version, Name = division.Name, HoursPerWeek = hours,
                // Groups of other subjects than the plan's row teach it.
                CoversSubject = division.Groups.Any(g => g.Subject != division.PlanSubject) ? subjects[division.PlanSubject] : null,
            };
            db.GroupDivisions.Add(division.Entity);
            db.StudentGroups.AddRange(division.Groups.Select(g => Group(g, division.Classes, division.Entity, hours)));
        }
        db.StudentGroups.AddRange(optionalGroups.Select(g => Group(g, g.Classes, null, g.Optional!.Hours)));

        TimetableEntry Entry(ClassInfo info, string subject, int day, int period, TeacherInfo teacher, Classroom room, GroupInfo? group) => new()
        {
            SchoolId = school.Id,
            TimetableVersion = version,
            SchoolClass = info.Class,
            Subject = subjects[subject],
            DayOfWeek = Weekdays[day],
            PeriodNumber = period,
            WeekParity = WeekParity.Every,
            Teacher = teacher.User,
            Classroom = room,
            StudentGroup = group?.Entity,
        };
        foreach (var x in placed)
        {
            if (x.Lesson.Division is not { } division)
            {
                db.TimetableEntries.Add(Entry(x.Lesson.Class, x.Lesson.Subject, x.Day, x.Period, x.Lesson.Teacher, x.Rooms[0], null));
                continue;
            }
            foreach (var (group, i) in division.Groups.Select((g, i) => (g, i)))
            {
                db.TimetableEntries.AddRange(division.Classes.Select(c => Entry(c, group.Subject, x.Day, x.Period, group.Teacher, x.Rooms[i], group)));
            }
        }
        foreach (var group in optionalGroups)
        {
            db.TimetableEntries.AddRange(group.Slots.SelectMany(s =>
                group.Classes.Select(c => Entry(c, group.Subject, s.Day, s.Period, group.Teacher, s.Room, group))));
        }
    }

    // ---- agendas ------------------------------------------------------

    private static readonly Dictionary<string, (string Note, int Weight, bool WholeClass)[]> GradeKinds = new()
    {
        ["Čj"] = [("Diktát", 2, true), ("Mluvnický test", 3, true), ("Čtení s porozuměním", 1, true), ("Slohová práce", 2, true), ("Ústní zkoušení", 1, false), ("Recitace", 1, false)],
        ["M"] = [("Písemná práce", 3, true), ("Pětiminutovka", 1, true), ("Test – geometrie", 2, true), ("Počítání u tabule", 1, false), ("Domácí úkol", 1, true)],
        ["Aj"] = [("Slovíčka", 1, true), ("Test – unit", 3, true), ("Poslech", 1, true), ("Konverzace", 1, false)],
        ["Nj"] = [("Slovíčka", 1, true), ("Test – lekce", 3, true), ("Konverzace", 1, false)],
        ["Fj"] = [("Slovíčka", 1, true), ("Test – leçon", 3, true), ("Konverzace", 1, false)],
        ["Šj"] = [("Slovíčka", 1, true), ("Test – lección", 3, true), ("Konverzace", 1, false)],
        ["Tv"] = [("Atletika – běh", 1, true), ("Gymnastika", 1, true), ("Míčové hry", 1, true)],
        ["Hv"] = [("Zpěv", 1, false), ("Hudební teorie", 1, true)],
        ["Vv"] = [("Výtvarná práce", 1, true)],
        ["Pč"] = [("Výrobek", 1, true)],
        ["Inf"] = [("Praktický úkol", 2, true), ("Test", 2, true)],
    };

    private static readonly (string Note, int Weight, bool WholeClass)[] DefaultGradeKinds =
        [("Test", 3, true), ("Opakování", 2, true), ("Ústní zkoušení", 1, false), ("Referát", 1, false), ("Pracovní list", 1, true)];

    /// <summary>A class's subjects for the class; a group's for its
    /// members, a nepovinný předmět's less often.</summary>
    private void GenerateGrades(List<ClassInfo> classes, Dictionary<string, Subject> subjects)
    {
        var days = calendar.PastSchoolDays();
        if (days.Count < 3)
        {
            return;
        }
        var weeks = days.Count / 5.0;
        foreach (var info in classes)
        {
            foreach (var (abbr, hours) in info.Plan.Where(p => !info.Divided.Contains(p.Key)))
            {
                Grade(info.Students, abbr, hours, 0.25);
            }
        }
        foreach (var division in divisions)
        {
            foreach (var group in division.Groups)
            {
                Grade(group.Members, group.Subject, division.Hours, 0.25);
            }
        }
        foreach (var group in optionalGroups)
        {
            Grade(group.Members, group.Subject, group.Optional!.Hours, 0.12);
        }

        void Grade(List<StudentInfo> pupils, string abbr, int hours, double perHour)
        {
            var kinds = GradeKinds.GetValueOrDefault(abbr) ?? DefaultGradeKinds;
            var count = (int)Math.Round(weeks * Math.Min(hours, 4) * perHour);
            for (var i = 0; i < count; i++)
            {
                // Leave the first days of the year ungraded.
                var day = days[rng.Next(Math.Min(4, days.Count - 1), days.Count)];
                var (note, weight, wholeClass) = Pick(kinds);
                var graded = wholeClass
                    ? pupils.Where(_ => rng.NextDouble() < 0.94)
                    : pupils.OrderBy(_ => rng.Next()).Take(rng.Next(2, 6));
                foreach (var student in graded)
                {
                    var value = (int)Math.Round(student.Ability + Normal() * 0.75);
                    db.Grades.Add(new Grade
                    {
                        SchoolId = school.Id,
                        StudentId = student.Student.Id,
                        SubjectId = subjects[abbr].Id,
                        Value = Math.Clamp(value, 1, 5),
                        Weight = weight,
                        Note = note,
                        GradedOn = day,
                        CreatedAt = At(day, 14, 0),
                    });
                }
            }
        }
    }

    private static readonly string[] SickReasons = ["Nemoc", "Nachlazení", "Viróza", "Horečka"];
    private static readonly string[] PartialReasons = ["Návštěva lékaře", "Zubař", "Rodinné důvody", "Úřední záležitost"];

    private void GenerateAbsences(List<ClassInfo> classes)
    {
        var days = calendar.PastSchoolDays();
        var recent = calendar.Today.AddDays(-10);
        foreach (var info in classes)
        {
            foreach (var student in info.Students)
            {
                for (var i = 0; i < days.Count; i++)
                {
                    var day = days[i];
                    var dayLength = info.DayLengths[(int)day.DayOfWeek - 1];
                    if (rng.NextDouble() < 0.011 * student.Sickliness)
                    {
                        var length = rng.Next(1, 6);
                        var reason = Pick(SickReasons);
                        var sickDays = days.Skip(i).Take(length).ToList();
                        AddAbsences(student, sickDays.Select(d => (d, Enumerable.Range(1, info.DayLengths[(int)d.DayOfWeek - 1]).ToList())).ToList(),
                            reason, recent);
                        i += length;
                    }
                    else if (rng.NextDouble() < 0.006)
                    {
                        var start = rng.Next(1, Math.Max(2, dayLength - 1));
                        AddAbsences(student, [(day, Enumerable.Range(start, Math.Min(2, dayLength - start + 1)).ToList())], Pick(PartialReasons), recent);
                    }
                }
            }
        }
    }

    /// <summary>The days of one absence, with the family's omluvenka unless it's unexcused.</summary>
    private void AddAbsences(StudentInfo student, List<(DateOnly Day, List<int> Periods)> days, string reason, DateOnly recent)
    {
        AbsenceStatus status;
        var roll = rng.NextDouble();
        if (days[0].Day < recent)
        {
            status = roll < 0.95 ? AbsenceStatus.Excused : roll < 0.98 ? AbsenceStatus.Unexcused : AbsenceStatus.Rejected;
        }
        else
        {
            status = roll < 0.4 ? AbsenceStatus.PendingExcuse : roll < 0.75 ? AbsenceStatus.Unexcused : AbsenceStatus.Excused;
        }
        var excuse = status == AbsenceStatus.Unexcused
            ? null
            : new Excuse
            {
                SchoolId = school.Id,
                StudentId = student.Student.Id,
                DateFrom = days[0].Day,
                DateTo = days[^1].Day,
                Reason = reason,
                CreatedAt = At(days[0].Day, 7, 30),
            };
        if (excuse is not null)
        {
            db.Excuses.Add(excuse);
        }
        foreach (var (day, periods) in days)
        {
            db.Absences.Add(new Absence
            {
                SchoolId = school.Id,
                StudentId = student.Student.Id,
                Date = day,
                Periods = periods,
                Status = status,
                Excuse = excuse,
                ReviewNote = status == AbsenceStatus.Rejected ? "Omluvenka přišla po lhůtě." : null,
                CreatedAt = At(day, 8, 10),
            });
        }
    }

    private static readonly (BehaviorRecordType Type, double Chance, string[] Texts)[] BehaviorKinds =
    [
        (BehaviorRecordType.Pochvala, 0.12, ["Pochvala za reprezentaci školy ve sportovní soutěži.", "Pochvala za pomoc spolužákům.", "Pochvala za vzornou přípravu na vyučování.", "Pochvala za úspěch v olympiádě."]),
        (BehaviorRecordType.Poznamka, 0.12, ["Zapomněl(a) domácí úkol.", "Vyrušuje při výuce.", "Nemá pomůcky na výtvarnou výchovu.", "Nepřinesl(a) podepsanou omluvenku."]),
        (BehaviorRecordType.NapomenutiTridnihoUcitele, 0.03, ["Opakované zapomínání pomůcek a domácích úkolů."]),
        (BehaviorRecordType.DutkaTridnihoUcitele, 0.01, ["Opakované nevhodné chování ke spolužákům."]),
    ];

    private void GenerateBehavior(List<ClassInfo> classes)
    {
        var days = calendar.PastSchoolDays();
        if (days.Count == 0)
        {
            return;
        }
        var share = Math.Min(1, days.Count / 100.0) + 0.2;
        foreach (var info in classes)
        {
            foreach (var student in info.Students)
            {
                foreach (var (type, chance, texts) in BehaviorKinds)
                {
                    if (rng.NextDouble() < chance * share)
                    {
                        var day = Pick(days);
                        db.BehaviorRecords.Add(new BehaviorRecord
                        {
                            SchoolId = school.Id,
                            StudentId = student.Student.Id,
                            Type = type,
                            Text = Pick(texts),
                            Date = day,
                            RecordedByName = info.ClassTeacher.Person.FullName,
                            CreatedAt = At(day, 13, 0),
                            ReadAt = day < calendar.Today.AddDays(-7) && rng.NextDouble() < 0.8 ? At(day.AddDays(1), 19, 0) : null,
                        });
                    }
                }
                if (calendar.Today >= calendar.SecondHalfStart)
                {
                    var roll = rng.NextDouble();
                    db.BehaviorClassifications.Add(new BehaviorClassification
                    {
                        SchoolId = school.Id,
                        StudentId = student.Student.Id,
                        SchoolYearStartYear = calendar.StartYear,
                        HalfYear = SchoolHalfYear.First,
                        Value = roll < 0.97 ? BehaviorClassificationValue.VelmiDobre : roll < 0.995 ? BehaviorClassificationValue.Uspokojive : BehaviorClassificationValue.Neuspokojive,
                        SetByName = info.ClassTeacher.Person.FullName,
                        SetAt = At(calendar.SecondHalfStart.AddDays(-4), 12, 0),
                    });
                }
            }
        }
    }

    private static readonly Dictionary<string, string[]> HomeworkTexts = new()
    {
        ["Čj"] = ["Pracovní sešit str. 24, cv. 3 a 4.", "Přečíst kapitolu z čítanky a napsat krátký zápis.", "Naučit se vyjmenovaná slova po B.", "Napsat sloh na téma Můj nejhezčí zážitek."],
        ["M"] = ["Učebnice str. 41, příklady 1–6.", "Pracovní list – zlomky.", "Procvičit násobilku 6 a 7.", "Narýsovat trojúhelníky podle zadání."],
        ["Aj"] = ["Naučit se slovíčka Unit 2.", "Workbook p. 18, ex. 1–3.", "Napsat 5 vět o svém dni."],
        ["Nj"] = ["Naučit se slovíčka lekce 3.", "Pracovní sešit str. 12."],
        ["Fj"] = ["Naučit se slovíčka leçon 2.", "Cahier d'exercices str. 9, cv. 1–4."],
        ["Šj"] = ["Naučit se slovíčka lección 2.", "Cuaderno de ejercicios str. 11."],
    };

    private static readonly string[] GenericHomework =
        ["Připravit si referát.", "Zopakovat učivo na test.", "Dokončit pracovní list.", "Vyhledat informace k probíranému tématu."];

    private void GenerateHomework(List<ClassInfo> classes, Dictionary<string, Subject> subjects)
    {
        var past = calendar.PastSchoolDays();
        var upcoming = calendar.UpcomingSchoolDays(6);
        var all = past.Concat(upcoming).ToList();
        if (all.Count < 3)
        {
            return;
        }
        foreach (var info in classes)
        {
            var mainSubjects = info.Plan.Keys.Where(s => !info.Divided.Contains(s)).Where(s => s is "Čj" or "M" or "Aj" or "Nj" or "Př" or "D" or "Z" or "F" or "Bi" or "Ch" or "Prv" or "Přv" or "Vl").ToList();
            // About two a week, the newest ones still due.
            for (var i = Math.Max(0, past.Count - 60); i < past.Count; i += rng.Next(2, 4))
            {
                var subject = Pick(mainSubjects);
                var created = past[i];
                var dueIndex = Math.Min(all.Count - 1, i + rng.Next(1, 4));
                var due = all[dueIndex];
                var requiresCompletion = rng.NextDouble() < 0.5;
                var homework = new Homework.Homework
                {
                    SchoolId = school.Id,
                    SchoolClassId = info.Class.Id,
                    SubjectId = subjects[subject].Id,
                    Text = Pick(HomeworkTexts.GetValueOrDefault(subject) ?? GenericHomework),
                    DueDate = due,
                    RequiresCompletion = requiresCompletion,
                    CreatedByName = info.TeacherBySubject[subject].Person.FullName,
                    CreatedAt = At(created, 12, 30),
                };
                if (requiresCompletion)
                {
                    var doneShare = due <= calendar.Today ? 0.85 : 0.3;
                    foreach (var student in info.Students.Where(_ => rng.NextDouble() < doneShare))
                    {
                        homework.Completions.Add(new HomeworkCompletion
                        {
                            StudentId = student.Student.Id,
                            SchoolId = school.Id,
                            CompletedAt = At(due.AddDays(-1), 18, 0),
                        });
                    }
                }
                db.Homework.Add(homework);
            }
        }

        // A language's, a seminar's or a povinně volitelný's homework is
        // its group's — about one a week, every other week for one hour.
        foreach (var (group, hours) in divisions.Where(d => d.PlanSubject != "Tv").SelectMany(d => d.Groups.Select(g => (g, d.Hours))))
        {
            for (var i = Math.Max(0, past.Count - 60) + rng.Next(5); i < past.Count; i += hours > 1 ? rng.Next(4, 7) : rng.Next(8, 12))
            {
                var due = all[Math.Min(all.Count - 1, i + rng.Next(3, 6))];
                var homework = new Homework.Homework
                {
                    SchoolId = school.Id,
                    StudentGroupId = group.Entity.Id,
                    SubjectId = group.Entity.SubjectId,
                    Text = Pick(HomeworkTexts.GetValueOrDefault(group.Subject) ?? GenericHomework),
                    DueDate = due,
                    RequiresCompletion = true,
                    CreatedByName = group.Teacher.Person.FullName,
                    CreatedAt = At(past[i], 12, 30),
                };
                var doneShare = due <= calendar.Today ? 0.85 : 0.3;
                foreach (var member in group.Members.Where(_ => rng.NextDouble() < doneShare))
                {
                    homework.Completions.Add(new HomeworkCompletion
                    {
                        StudentId = member.Student.Id,
                        SchoolId = school.Id,
                        CompletedAt = At(due.AddDays(-1), 18, 0),
                    });
                }
                db.Homework.Add(homework);
            }
        }
    }

    private DemoFeaturedUsers PickFeatured(User admin, List<ClassInfo> classes)
    {
        // A class teacher of an upper class — many subjects, many classes.
        var featuredClass = classes.FirstOrDefault(c => c.Grade == (school.Type == SchoolType.Elementary ? 6 : 2)) ?? classes[0];
        var teacher = featuredClass.ClassTeacher.User;

        var student = featuredClass.Students.FirstOrDefault(s => s.Student.Person!.User is not null)
            ?? classes.SelectMany(c => c.Students).FirstOrDefault(s => s.Student.Person!.User is not null);
        if (student is null)
        {
            student = featuredClass.Students[0];
            StudentAccount(student.Student);
        }

        var families = classes.SelectMany(c => c.Students).Select(s => s.Family).Distinct().ToList();
        var parent = families
            .Where(f => f.Children.Count >= 2 && f.Parents.Any(p => p.User is not null))
            .OrderByDescending(f => f.Children.Any(c => c.Class == featuredClass))
            .Select(f => f.Parents.First(p => p.User is not null).User)
            .FirstOrDefault()
            ?? student.Family.Parents.Select(p => p.User).FirstOrDefault(u => u is not null);
        if (parent is null)
        {
            var (person, _, _) = student.Family.Parents[0];
            parent = NewUser(person.FirstName, person.LastName, [UserRole.Parent]);
            person.User = parent;
        }
        if (student.Student.Person!.User!.Id == 0 || parent.Id == 0)
        {
            // Accounts added just now — the ids come with the next save.
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }
        return new DemoFeaturedUsers(admin.Id, teacher.Id, parent.Id, student.Student.Person!.User!.Id);
    }

    private static readonly (string Subject, string Body)[] SchoolBroadcasts =
    [
        ("Třídní schůzky", "Vážení rodiče,\n\nzveme vás na třídní schůzky, které proběhnou ve čtvrtek od 17:00 v kmenových třídách. Konzultace s vyučujícími jsou možné od 16:00.\n\nDěkujeme, vedení školy"),
        ("Ředitelské volno", "Vážení rodiče,\n\noznamujeme, že v pátek bude z provozních důvodů vyhlášeno ředitelské volno. Školní družina bude v provozu.\n\nVedení školy"),
        ("Sběr papíru", "Milí žáci a rodiče,\n\npříští týden proběhne sběr starého papíru. Papír noste svázaný ráno od 7:30 do 8:00 ke školnímu dvoru.\n\nDěkujeme!"),
    ];

    private static readonly (string Subject, string Body)[] ClassBroadcasts =
    [
        ("Školní výlet", "Dobrý den,\n\nna konci měsíce plánujeme třídní výlet. Podrobnosti a přihlášku najdete v příloze, platbu prosím uhraďte přes Platby.\n\nDěkuji"),
        ("Fotografování tříd", "Dobrý den,\n\nve středu proběhne fotografování tříd. Děti mohou přijít slavnostněji oblečené.\n\nS pozdravem"),
        ("Plavecký výcvik", "Dobrý den,\n\nod příštího týdne začíná plavecký výcvik. Děti potřebují plavky, ručník a čepici.\n\nDěkuji"),
    ];

    private static readonly (string Subject, string Teacher, string Parent)[] Conversations =
    [
        ("Zapomínání domácích úkolů", "Dobrý den, chtěla bych vás upozornit, že {0} v posledních dvou týdnech opakovaně nepřinesl(a) domácí úkol. Můžeme se domluvit na konzultaci?", "Dobrý den, děkujeme za informaci, doma si to pohlídáme. Konzultace by nám vyhovovala ve čtvrtek odpoledne."),
        ("Prospěch", "Dobrý den, {0} má v posledních testech horší výsledky. Doporučuji doma zopakovat poslední kapitolu, případně nabízím doučování v úterý 7. hodinu.", "Dobrý den, děkujeme, doučování rádi využijeme."),
        ("Pochvala", "Dobrý den, chtěla jsem vám jen napsat, že {0} se poslední dobou ve výuce moc snaží. Jen tak dál!", "Děkujeme, to nás moc potěšilo!"),
        ("Omluva z výuky", "Dobrý den, beru na vědomí. Úkoly a probrané učivo najde {0} v agendě Domácí úkoly.", "Dobrý den, prosím o uvolnění z výuky v pátek od 10:00 z důvodu návštěvy lékaře."),
    ];

    private void GenerateMessages((User User, Person Person) admin, List<ClassInfo> classes, DemoFeaturedUsers featured)
    {
        var days = calendar.PastSchoolDays();
        if (days.Count < 5)
        {
            return;
        }
        var families = classes.SelectMany(c => c.Students)
            .ToDictionary(s => s.Student.Id, s => s.Family.Parents.Where(p => p.User is not null).Select(p => p.User!.Id)
                .Concat(s.Student.Person!.User is { } own ? [own.Id] : [])
                .ToList());

        void Broadcast(User sender, string senderName, string subject, string body, string audience, IEnumerable<StudentInfo> students, DateOnly day)
        {
            var sentAt = Past(At(day, 15, 30));
            var broadcast = new MessageBroadcast
            {
                SchoolId = school.Id,
                SenderUser = sender,
                SenderName = senderName,
                Subject = subject,
                Body = body,
                AudienceLabel = audience,
                CreatedAt = sentAt,
            };
            db.MessageBroadcasts.Add(broadcast);
            foreach (var student in students)
            {
                var recipients = families[student.Student.Id];
                if (recipients.Count == 0)
                {
                    continue;
                }
                var conversation = ConversationsController.NewConversation(
                    subject, student.Student.Id, sender.Id, senderName, recipients, body, school.Id, sentAt);
                conversation.Broadcast = broadcast;
                MarkRead(conversation, sentAt, featured.ParentId);
                db.Conversations.Add(conversation);
            }
        }

        var allStudents = classes.SelectMany(c => c.Students).ToList();
        var adminName = admin.Person.FullName;
        foreach (var (subject, body) in SchoolBroadcasts)
        {
            Broadcast(admin.User, adminName, subject, body, "celá škola", allStudents, days[rng.Next(days.Count / 3, days.Count)]);
        }
        foreach (var info in classes)
        {
            var teacher = info.ClassTeacher;
            var (subject, body) = Pick(ClassBroadcasts);
            Broadcast(teacher.User, teacher.Person.FullName, subject, body, info.Class.Name, info.Students, days[rng.Next(days.Count)]);
        }

        // One-to-one threads between a class teacher and a family.
        foreach (var info in classes)
        {
            var teacher = info.ClassTeacher;
            var withAccounts = info.Students.Where(s => s.Family.Parents.Any(p => p.User is not null)).ToList();
            var chosen = withAccounts.OrderBy(_ => rng.Next()).Take(Math.Max(1, withAccounts.Count / 6)).ToList();
            var featuredChild = withAccounts.FirstOrDefault(s => s.Family.Parents.Any(p => p.User?.Id == featured.ParentId));
            if (featuredChild is not null && !chosen.Contains(featuredChild))
            {
                chosen.Add(featuredChild);
            }
            foreach (var student in chosen)
            {
                var (subject, teacherText, parentText) = Pick(Conversations);
                var parentUser = student.Family.Parents.First(p => p.User is not null);
                var day = days[rng.Next(Math.Max(0, days.Count - 30), days.Count)];
                var startedAt = Past(At(day, 16, 0));
                var parentFirst = subject == "Omluva z výuky";
                var firstBody = parentFirst ? parentText : string.Format(teacherText, student.Student.Person!.FirstName);
                var conversation = ConversationsController.NewConversation(
                    subject, student.Student.Id,
                    parentFirst ? parentUser.User!.Id : teacher.User.Id,
                    parentFirst ? parentUser.Person.FullName : teacher.Person.FullName,
                    [parentFirst ? teacher.User.Id : parentUser.User!.Id],
                    firstBody, school.Id, startedAt);
                var replyAt = startedAt.AddHours(rng.Next(2, 20));
                if (replyAt < now)
                {
                    conversation.Messages.Add(new Message
                    {
                        SchoolId = school.Id,
                        SenderUserId = parentFirst ? teacher.User.Id : parentUser.User!.Id,
                        SenderName = parentFirst ? teacher.Person.FullName : parentUser.Person.FullName,
                        Body = parentFirst ? string.Format(teacherText, student.Student.Person!.FirstName) : parentText,
                        SentAt = replyAt,
                    });
                    conversation.LastMessageAt = replyAt;
                    var replier = parentFirst ? teacher.User.Id : parentUser.User!.Id;
                    conversation.Participants.First(p => p.UserId == replier).LastReadAt = replyAt;
                }
                // The featured parent finds something unread.
                if (parentUser.User!.Id != featured.ParentId)
                {
                    MarkRead(conversation, conversation.LastMessageAt, featured.ParentId);
                }
                db.Conversations.Add(conversation);
            }
        }
    }

    /// <summary>Most recipients have read it — except the featured parent's
    /// newest ones, so the demo shows unread messages.</summary>
    private void MarkRead(Conversation conversation, DateTimeOffset at, int featuredParentId)
    {
        foreach (var participant in conversation.Participants.Where(p => p.LastReadAt is null))
        {
            var recent = at > now.AddDays(-10);
            if (participant.UserId == featuredParentId ? !recent : rng.NextDouble() < 0.75)
            {
                participant.LastReadAt = at.AddHours(rng.Next(1, 30));
            }
        }
    }

    private void GenerateEvents(User admin, List<ClassInfo> classes, bool elementary)
    {
        var y = calendar.StartYear;
        void Add(string title, DateOnly day, int startHour, int durationHours, string? location = null, bool allDay = false,
            string? color = null, ClassInfo? onlyClass = null, int? createdBy = null, string? description = null)
        {
            var start = allDay ? At(day, 0, 0) : At(day, startHour, 0);
            var ev = new Event
            {
                SchoolId = school.Id,
                Title = title,
                Description = description,
                Location = location,
                StartAt = start,
                EndAt = allDay ? At(day.AddDays(Math.Max(1, durationHours / 24)), 0, 0) : start.AddHours(durationHours),
                AllDay = allDay,
                Color = color,
                CreatedByUserId = createdBy ?? admin.Id,
                Audience = onlyClass is null ? EventAudience.Everyone : EventAudience.Selected,
            };
            if (onlyClass is not null)
            {
                ev.SchoolClasses.Add(new EventSchoolClass { SchoolClassId = onlyClass.Class.Id, SchoolId = school.Id });
            }
            db.Events.Add(ev);
        }

        Add("Zahájení školního roku", new DateOnly(y, 9, 1), 8, 1, "Kmenové třídy", color: "#86efac");
        Add("Fotografování tříd", NextWeekday(new DateOnly(y, 10, 7)), 8, 4, "Aula");
        Add("Třídní schůzky", NextWeekday(new DateOnly(y, 11, 13), DayOfWeek.Thursday), 17, 2, "Kmenové třídy", color: "#93c5fd",
            description: "Konzultace s vyučujícími od 16:00.");
        Add("Vánoční besídka", NextWeekday(new DateOnly(y, 12, 17)), 16, 2, "Tělocvična", color: "#fca5a5");
        Add("Den otevřených dveří", NextWeekday(new DateOnly(y + 1, 1, 14), DayOfWeek.Wednesday), 9, 8, color: "#fcd34d");
        Add("Vydání pololetního vysvědčení", NextWeekday(new DateOnly(y + 1, 1, 29), DayOfWeek.Thursday), 0, 24, allDay: true, color: "#c4b5fd");
        if (elementary)
        {
            Add("Zápis do 1. tříd", NextWeekday(new DateOnly(y + 1, 4, 10)), 14, 4, "Přízemí školy");
        }
        Add("Třídní schůzky", NextWeekday(new DateOnly(y + 1, 4, 16), DayOfWeek.Thursday), 17, 2, "Kmenové třídy", color: "#93c5fd");
        Add("Sportovní den", NextWeekday(new DateOnly(y + 1, 6, 12)), 8, 5, "Školní hřiště", color: "#bef264");
        Add("Ukončení školního roku", new DateOnly(y + 1, 6, 30), 8, 1, color: "#86efac");

        // Something happening this week and next, whatever today is.
        var soon = calendar.UpcomingSchoolDays(8);
        if (soon.Count >= 6)
        {
            Add("Ředitelské volno", soon[5], 0, 24, allDay: true, color: "#fdba74");
            Add("Pedagogická rada", soon[1], 14, 2, "Sborovna");
        }
        var trips = new[] { "Exkurze do muzea", "Divadelní představení", "Školní výlet", "Návštěva knihovny", "Dopravní hřiště" };
        foreach (var info in classes)
        {
            var day = soon.Count > 0 && rng.NextDouble() < 0.5 ? Pick(soon) : Pick(calendar.PastSchoolDays().DefaultIfEmpty(calendar.Today).ToList());
            Add($"{Pick(trips)} – {info.Class.Name}", day, 8, 6, onlyClass: info, createdBy: info.ClassTeacher.User.Id, color: "#f9a8d4");
        }
    }

    private void GeneratePayments(User admin, List<ClassInfo> classes, bool elementary)
    {
        var y = calendar.StartYear;
        var allStudents = classes.SelectMany(c => c.Students).ToList();

        void Add(string title, string description, decimal amount, DateOnly due, IEnumerable<StudentInfo> students, List<ClassInfo>? onlyClasses, int createdBy)
        {
            var payment = new Payment
            {
                SchoolId = school.Id,
                Title = title,
                Description = description,
                DueDate = due,
                AmountType = PaymentAmountType.FixedPerStudent,
                Amount = amount,
                CreatedByUserId = createdBy,
                Audience = onlyClasses is null ? PaymentAudience.Everyone : PaymentAudience.Selected,
            };
            foreach (var info in onlyClasses ?? [])
            {
                payment.SchoolClasses.Add(new PaymentSchoolClass { SchoolClassId = info.Class.Id, SchoolId = school.Id });
            }
            var overdue = due < calendar.Today;
            foreach (var student in students)
            {
                var paid = rng.NextDouble() < (overdue ? 0.92 : 0.4);
                var paidOn = due.AddDays(-rng.Next(1, 20));
                if (paidOn > calendar.Today)
                {
                    paidOn = calendar.Today;
                }
                payment.Recipients.Add(new PaymentRecipient
                {
                    StudentId = student.Student.Id,
                    SchoolId = school.Id,
                    Amount = amount,
                    InstallmentDueDate = due,
                    Paid = paid,
                    PaidAt = paid ? At(paidOn, 10, 0) : null,
                    CreatedAt = At(due.AddDays(-30), 9, 0),
                });
            }
            db.Payments.Add(payment);
        }

        Add("Pracovní sešity", "Pracovní sešity a pomůcky na celý školní rok.", elementary ? 450 : 600,
            new DateOnly(y, 9, 30), allStudents, null, admin.Id);
        Add("Kulturní fond", "Divadla, besedy a kulturní akce během roku.", 300,
            new DateOnly(y, 10, 31), allStudents, null, admin.Id);
        foreach (var info in classes.OrderBy(_ => rng.Next()).Take(Math.Max(2, classes.Count / 3)))
        {
            var due = calendar.Today.AddDays(rng.Next(-20, 25));
            Add($"Školní výlet – {info.Class.Name}", "Doprava, vstupné a průvodce.", rng.Next(4, 9) * 100,
                due, info.Students, [info], info.ClassTeacher.User.Id);
        }
        if (elementary)
        {
            var swimmers = classes.Where(c => c.Grade is 2 or 3).ToList();
            Add("Plavecký výcvik", "Deset lekcí plavání včetně dopravy.", 1200, calendar.Today.AddDays(14),
                swimmers.SelectMany(c => c.Students), swimmers, admin.Id);
        }
    }

    // ---- helpers ------------------------------------------------------

    private DateTimeOffset At(DateOnly day, int hour, int minute)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute));
        // Npgsql stores timestamptz only from UTC values.
        return new DateTimeOffset(local, Prague.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Not later than now — a message "sent" this afternoon when
    /// it's only morning would look odd.</summary>
    private DateTimeOffset Past(DateTimeOffset at) => at < now ? at : now.AddMinutes(-rng.Next(5, 120));

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek? dayOfWeek = null)
    {
        while (dayOfWeek is { } d ? from.DayOfWeek != d : from.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            from = from.AddDays(1);
        }
        return from;
    }

    /// <summary>The caller may have switched automatic change detection
    /// off (bulk insert), and navigations get set after Add here.</summary>
    private async Task SaveAsync()
    {
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    /// <summary>Standard normal sample (Box–Muller).</summary>
    private double Normal() =>
        Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
