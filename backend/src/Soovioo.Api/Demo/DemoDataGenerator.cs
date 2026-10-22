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
/// aprobace, a collision-free (as far as greedy placement gets) timetable,
/// and every agenda — grades, absences, behaviour, homework, messages,
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
        new("Tv", "Tělesná výchova", "#bef264", "gym"), new("Pč", "Pracovní činnosti", "#e7e5e4", "workshop"),
    ];

    private static readonly SubjectDef[] SecondarySubjects =
    [
        new("Čj", "Český jazyk a literatura", "#fca5a5"), new("Aj", "Anglický jazyk", "#fcd34d"),
        new("Nj", "Německý jazyk", "#fde68a"), new("M", "Matematika", "#93c5fd"), new("F", "Fyzika", "#a5b4fc", "lab"),
        new("Ch", "Chemie", "#c4b5fd", "lab"), new("Bi", "Biologie", "#86efac", "lab"), new("D", "Dějepis", "#d6b48c"),
        new("Z", "Zeměpis", "#a7f3d0"), new("ZSV", "Základy společenských věd", "#f9a8d4"),
        new("Inf", "Informatika", "#67e8f9", "computer"), new("Vv", "Výtvarná výchova", "#fbcfe8", "art"),
        new("Tv", "Tělesná výchova", "#bef264", "gym"), new("Sem", "Seminář", "#e7e5e4"),
    ];

    /// <summary>Weekly hours per grade (index 0 = 1st grade), roughly the RVP.</summary>
    private static readonly Dictionary<string, int>[] ElementaryPlan =
    [
        new() { ["Čj"] = 8, ["M"] = 4, ["Aj"] = 1, ["Prv"] = 2, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 8, ["M"] = 5, ["Aj"] = 1, ["Prv"] = 2, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Prv"] = 3, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Přv"] = 2, ["Vl"] = 2, ["Inf"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 7, ["M"] = 5, ["Aj"] = 3, ["Přv"] = 2, ["Vl"] = 2, ["Inf"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 4, ["M"] = 4, ["Aj"] = 3, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 4, ["M"] = 4, ["Aj"] = 3, ["Nj"] = 2, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 2, ["Tv"] = 2, ["Pč"] = 1 },
        new() { ["Čj"] = 4, ["M"] = 4, ["Aj"] = 3, ["Nj"] = 2, ["D"] = 2, ["Z"] = 2, ["Př"] = 2, ["F"] = 2, ["Ch"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2 },
        new() { ["Čj"] = 5, ["M"] = 5, ["Aj"] = 3, ["Nj"] = 2, ["D"] = 2, ["Z"] = 1, ["Př"] = 2, ["F"] = 2, ["Ch"] = 2, ["Inf"] = 1, ["Ov"] = 1, ["Hv"] = 1, ["Vv"] = 1, ["Tv"] = 2 },
    ];

    private static readonly Dictionary<string, int>[] SecondaryPlan =
    [
        new() { ["Čj"] = 3, ["Aj"] = 3, ["Nj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 1, ["Inf"] = 2, ["Vv"] = 2, ["Tv"] = 2 },
        new() { ["Čj"] = 3, ["Aj"] = 3, ["Nj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 1, ["Inf"] = 2, ["Vv"] = 2, ["Tv"] = 2 },
        new() { ["Čj"] = 3, ["Aj"] = 3, ["Nj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["Z"] = 2, ["ZSV"] = 2, ["Inf"] = 1, ["Tv"] = 2, ["Sem"] = 2 },
        new() { ["Čj"] = 4, ["Aj"] = 4, ["Nj"] = 3, ["M"] = 4, ["F"] = 2, ["Ch"] = 2, ["Bi"] = 2, ["D"] = 2, ["ZSV"] = 2, ["Tv"] = 2, ["Sem"] = 2 },
    ];

    /// <summary>Aprobace pairs a newly needed teacher is drawn from.</summary>
    private static readonly (string, string)[] ElementaryQualifications =
    [
        ("Čj", "D"), ("Čj", "Ov"), ("M", "F"), ("M", "Inf"), ("Aj", "Nj"), ("Aj", "Z"), ("Př", "Ch"),
        ("Z", "Tv"), ("Hv", "Vv"), ("Tv", "Pč"), ("D", "Ov"), ("F", "Inf"), ("Př", "Z"), ("Vv", "Pč"),
    ];

    private static readonly (string, string)[] SecondaryQualifications =
    [
        ("Čj", "D"), ("Čj", "ZSV"), ("M", "F"), ("M", "Inf"), ("Aj", "Nj"), ("Aj", "Z"), ("Bi", "Ch"),
        ("Z", "Tv"), ("Ch", "F"), ("D", "ZSV"), ("Tv", "Bi"), ("Vv", "Čj"), ("Inf", "Sem"), ("M", "Sem"),
    ];

    private static readonly (TimeOnly Start, TimeOnly End)[] Bells =
    [
        (new(8, 0), new(8, 45)), (new(8, 55), new(9, 40)), (new(10, 0), new(10, 45)), (new(10, 55), new(11, 40)),
        (new(11, 50), new(12, 35)), (new(12, 45), new(13, 30)), (new(13, 40), new(14, 25)),
    ];

    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private Random rng = new(0);
    private DateTimeOffset now;
    private School school = null!;
    private DemoSchoolCalendar calendar = null!;
    private readonly HashSet<string> usernames = [];
    private readonly List<User> users = [];

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
    }

    private sealed class StudentInfo
    {
        public required Student Student { get; init; }
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

        var elementary = target.Type == SchoolType.Elementary;
        var seed = options.Seed;
        var subjectDefs = elementary ? ElementarySubjects : SecondarySubjects;
        var plan = elementary ? ElementaryPlan : SecondaryPlan;

        foreach (var (period, (start, end)) in Bells.Select((b, i) => (i + 1, b)))
        {
            db.SchoolPeriods.Add(new SchoolPeriod { SchoolId = school.Id, PeriodNumber = period, StartTime = start, EndTime = end });
        }

        var roomGroups = CreateSpecialRooms(elementary, options.ClassesPerGrade);
        var subjects = subjectDefs.ToDictionary(d => d.Abbr, d =>
        {
            var subject = new Subject
            {
                Name = d.Name,
                Abbreviation = d.Abbr,
                Color = d.Color,
                SchoolId = school.Id,
                DefaultClassroom = d.RoomGroup is { } g ? roomGroups[g][0] : null,
            };
            db.Subjects.Add(subject);
            return subject;
        });

        var admin = NewPerson(rng.Next(2) == 0, adult: true, [UserRole.Admin]);
        school.DirectorName = $"Mgr. {admin.Person.FullName}";
        FillSchoolDetails();

        var classes = CreateClasses(plan, options.ClassesPerGrade, elementary);
        var teachers = AssignTeachers(classes, elementary);
        CreateFamilies(classes, elementary);
        db.TeacherProfiles.AddRange(teachers.Select(t => new TeacherProfile
        {
            Person = t.Person,
            SchoolId = school.Id,
            Qualification = t.FirstStage ? "1. stupeň" + (t.Qualifications.Contains("Aj") ? ", Aj" : "") : string.Join(", ", t.Qualifications),
        }));

        BuildTimetable(classes, subjects, roomGroups);

        // One save for the whole people/structure graph — EF orders the
        // inserts by the navigations; the agendas below need real ids.
        await SaveAsync();

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
                DemoAvatars.Get(female, adult, photoRng.Next(DemoAvatars.VariantsPerKind)));

        foreach (var person in teachers.Select(t => t.Person).Prepend(admin))
        {
            if (person == admin || photoRng.NextDouble() < 0.85)
            {
                person.PhotoKey = await Avatar(IsFemale(person.FirstName), adult: true);
            }
        }

        var students = classes.SelectMany(c => c.Students).ToList();
        var featuredStudent = students.First(s => s.Student.User?.Id == featured.StudentId);
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
            student.PhotoKey = await Avatar(student.Gender == Gender.Female, adult: false);
            student.PhotoStatus = info == pendingChild && info != featuredStudent ? PhotoStatus.Pending : PhotoStatus.Approved;
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

        var gyms = new List<Classroom> { Room("Tělocvična", "TV1", ClassroomType.Gym, 30) };
        if (perGrade > 1)
        {
            gyms.Add(Room("Malá tělocvična", "TV2", ClassroomType.Gym, 20));
        }
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
        };
        if (elementary)
        {
            groups["music"] = [Room("Hudebna", "HV", ClassroomType.Specialized, 30)];
            groups["workshop"] = [Room("Dílna", "DÍL", ClassroomType.Workshop, 16)];
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
                var schoolClass = new SchoolClass { Name = name, Rocnik = grade, TeachingLanguage = "čeština", SchoolId = school.Id };
                var floor = elementary ? (grade <= 5 ? 1 : 2) : (grade <= 2 ? 1 : 2);
                roomsPerFloor[floor] = roomsPerFloor.GetValueOrDefault(floor) + 1;
                var homeroom = new Classroom
                {
                    Name = $"{floor * 100 + roomsPerFloor[floor]}",
                    Type = ClassroomType.Homeroom,
                    Capacity = 30,
                    SchoolId = school.Id,
                };
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

        TeacherInfo TeacherFor(string subject, int hours)
        {
            var candidates = teachers
                .Where(t => !t.FirstStage && t.Qualifications.Contains(subject) && t.Load + hours <= 22)
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
            foreach (var (subject, hours) in info.Plan.OrderByDescending(p => p.Value))
            {
                var teacher = TeacherFor(subject, hours);
                teacher.Load += hours;
                info.TeacherBySubject[subject] = teacher;
            }
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
                if (family.Children.Any(c => c.Student.FirstName == first))
                {
                    first = Pick(male ? DemoNames.MaleFirstNames : DemoNames.FemaleFirstNames);
                }
                // Age at 1 September: 6 in the first grade of a ZŠ, 15 in the
                // first year of a SŠ.
                var ageAtStart = (elementary ? 5 : 14) + grade;
                var born = new DateOnly(calendar.StartYear - ageAtStart - 1, 9, 1).AddDays(rng.Next(365));
                var enrolledYear = calendar.StartYear - grade + 1;
                variableSymbolCounters[enrolledYear] = variableSymbolCounters.GetValueOrDefault(enrolledYear) + 1;
                var student = new Student
                {
                    FirstName = first,
                    LastName = male ? family.Surname.Male : family.Surname.Female,
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

    private (User User, Person Person) NewPerson(bool female, bool adult, List<UserRole> roles)
    {
        var first = Pick(female
            ? adult ? DemoNames.AdultFemaleFirstNames : DemoNames.FemaleFirstNames
            : adult ? DemoNames.AdultMaleFirstNames : DemoNames.MaleFirstNames);
        var surname = Pick(DemoNames.Surnames);
        var last = female ? surname.Female : surname.Male;
        var user = NewUser(first, last, roles);
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

    private User NewUser(string first, string last, List<UserRole> roles)
    {
        var baseName = DemoNames.Ascii($"{first}.{last}");
        var username = baseName;
        for (var i = 2; !usernames.Add(username); i++)
        {
            username = $"{baseName}{i}";
        }
        // Empty hash never verifies: demo users are picked, not logged in.
        var user = new User { Username = username, PasswordHash = "", Roles = [.. roles], Role = roles[0], SchoolId = school.Id };
        db.Users.Add(user);
        users.Add(user);
        return user;
    }

    /// <summary>The pupil's own login, with a Person like every account.</summary>
    private void StudentAccount(Student student)
    {
        student.User = NewUser(student.FirstName, student.LastName, [UserRole.Student]);
        db.Persons.Add(new Person
        {
            FirstName = student.FirstName,
            LastName = student.LastName,
            SchoolId = school.Id,
            User = student.User,
            Email = $"{student.User.Username}@example.com",
        });
    }

    private string Phone() => $"+420 {rng.Next(601, 799)} {rng.Next(100, 999)} {rng.Next(100, 999)}";

    // ---- timetable ----------------------------------------------------

    private sealed record Lesson(ClassInfo Class, string Subject, TeacherInfo Teacher);

    private sealed class Placement(Lesson lesson, int day, int period, Classroom room)
    {
        public Lesson Lesson { get; } = lesson;
        public int Day { get; set; } = day;
        public int Period { get; set; } = period;
        public Classroom Room { get; set; } = room;
    }

    private void BuildTimetable(
        List<ClassInfo> classes, Dictionary<string, Subject> subjects, Dictionary<string, List<Classroom>> roomGroups)
    {
        var classBusy = new HashSet<(ClassInfo, int, int)>();
        var subjectDay = new Dictionary<(ClassInfo, string, int), int>();
        var defs = (school.Type == SchoolType.Elementary ? ElementarySubjects : SecondarySubjects).ToDictionary(d => d.Abbr);

        foreach (var info in classes)
        {
            var total = info.Plan.Values.Sum();
            var lengths = Enumerable.Repeat(total / 5, 5).ToArray();
            foreach (var day in Enumerable.Range(0, 5).OrderBy(_ => rng.Next()).Take(total % 5))
            {
                lengths[day]++;
            }
            info.DayLengths = lengths;
        }

        var lessons = classes
            .SelectMany(c => c.Plan.SelectMany(p => Enumerable.Repeat(new Lesson(c, p.Key, c.TeacherBySubject[p.Key]), p.Value)))
            .ToList();
        // Most constrained first: specialists teaching many classes, rooms
        // that are shared, then the class teachers' own lessons.
        var ordered = lessons
            .OrderBy(l => l.Teacher == l.Class.ClassTeacher && l.Teacher.FirstStage ? 1 : 0)
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

        foreach (var lesson in ordered)
        {
            var info = lesson.Class;
            var hours = info.Plan[lesson.Subject];
            (int Day, int Period, Classroom Room, double Score)? best = null;
            for (var day = 0; day < 5; day++)
            {
                for (var period = 1; period <= info.DayLengths[day]; period++)
                {
                    if (classBusy.Contains((info, day, period)))
                    {
                        continue;
                    }
                    var score = rng.NextDouble();
                    if (teacherBusy.GetValueOrDefault((lesson.Teacher, day, period)) > 0)
                    {
                        score += 100;
                    }
                    var room = RoomFor(lesson, day, period);
                    if (room is null)
                    {
                        score += 10;
                    }
                    var sameDay = subjectDay.GetValueOrDefault((info, lesson.Subject, day));
                    if (sameDay > 0)
                    {
                        score += hours > 5 ? 2 * sameDay : 5 * sameDay;
                    }
                    if (best is null || score < best.Value.Score)
                    {
                        best = (day, period, room ?? roomGroups[defs[lesson.Subject].RoomGroup!][0], score);
                    }
                }
            }
            var (d, p, r, _) = best!.Value;
            classBusy.Add((info, d, p));
            teacherBusy[(lesson.Teacher, d, p)] = teacherBusy.GetValueOrDefault((lesson.Teacher, d, p)) + 1;
            roomBusy.Add((r, d, p));
            subjectDay[(info, lesson.Subject, d)] = subjectDay.GetValueOrDefault((info, lesson.Subject, d)) + 1;
            placed.Add(new Placement(lesson, d, p, r));
        }

        // Repair: a lesson whose teacher is double-booked swaps slots with
        // another lesson of the same class when that frees both teachers.
        foreach (var clash in placed.Where(x => teacherBusy[(x.Lesson.Teacher, x.Day, x.Period)] > 1).ToList())
        {
            if (teacherBusy[(clash.Lesson.Teacher, clash.Day, clash.Period)] <= 1)
            {
                continue;
            }
            var swap = placed.FirstOrDefault(o =>
                o.Lesson.Class == clash.Lesson.Class && o != clash && o.Lesson.Teacher != clash.Lesson.Teacher
                && teacherBusy.GetValueOrDefault((clash.Lesson.Teacher, o.Day, o.Period)) == 0
                && teacherBusy.GetValueOrDefault((o.Lesson.Teacher, clash.Day, clash.Period)) == 0
                && RoomFor(clash.Lesson, o.Day, o.Period, except: o.Room) is not null
                && RoomFor(o.Lesson, clash.Day, clash.Period, except: clash.Room) is not null);
            if (swap is null)
            {
                continue;
            }
            teacherBusy[(clash.Lesson.Teacher, clash.Day, clash.Period)]--;
            teacherBusy[(swap.Lesson.Teacher, swap.Day, swap.Period)]--;
            roomBusy.Remove((clash.Room, clash.Day, clash.Period));
            roomBusy.Remove((swap.Room, swap.Day, swap.Period));
            var clashRoom = RoomFor(clash.Lesson, swap.Day, swap.Period)!;
            roomBusy.Add((clashRoom, swap.Day, swap.Period));
            var swapRoom = RoomFor(swap.Lesson, clash.Day, clash.Period)!;
            roomBusy.Add((swapRoom, clash.Day, clash.Period));
            (clash.Day, clash.Period, swap.Day, swap.Period) = (swap.Day, swap.Period, clash.Day, clash.Period);
            (clash.Room, swap.Room) = (clashRoom, swapRoom);
            teacherBusy[(clash.Lesson.Teacher, clash.Day, clash.Period)] = teacherBusy.GetValueOrDefault((clash.Lesson.Teacher, clash.Day, clash.Period)) + 1;
            teacherBusy[(swap.Lesson.Teacher, swap.Day, swap.Period)] = teacherBusy.GetValueOrDefault((swap.Lesson.Teacher, swap.Day, swap.Period)) + 1;
        }

        foreach (var x in placed)
        {
            db.TimetableEntries.Add(new TimetableEntry
            {
                SchoolId = school.Id,
                SchoolClass = x.Lesson.Class.Class,
                Subject = subjects[x.Lesson.Subject],
                DayOfWeek = Weekdays[x.Day],
                PeriodNumber = x.Period,
                WeekParity = WeekParity.Every,
                Teacher = x.Lesson.Teacher.User,
                Classroom = x.Room,
            });
        }
    }

    // ---- agendas ------------------------------------------------------

    private static readonly Dictionary<string, (string Note, int Weight, bool WholeClass)[]> GradeKinds = new()
    {
        ["Čj"] = [("Diktát", 2, true), ("Mluvnický test", 3, true), ("Čtení s porozuměním", 1, true), ("Slohová práce", 2, true), ("Ústní zkoušení", 1, false), ("Recitace", 1, false)],
        ["M"] = [("Písemná práce", 3, true), ("Pětiminutovka", 1, true), ("Test – geometrie", 2, true), ("Počítání u tabule", 1, false), ("Domácí úkol", 1, true)],
        ["Aj"] = [("Slovíčka", 1, true), ("Test – unit", 3, true), ("Poslech", 1, true), ("Konverzace", 1, false)],
        ["Nj"] = [("Slovíčka", 1, true), ("Test – lekce", 3, true), ("Konverzace", 1, false)],
        ["Tv"] = [("Atletika – běh", 1, true), ("Gymnastika", 1, true), ("Míčové hry", 1, true)],
        ["Hv"] = [("Zpěv", 1, false), ("Hudební teorie", 1, true)],
        ["Vv"] = [("Výtvarná práce", 1, true)],
        ["Pč"] = [("Výrobek", 1, true)],
        ["Inf"] = [("Praktický úkol", 2, true), ("Test", 2, true)],
    };

    private static readonly (string Note, int Weight, bool WholeClass)[] DefaultGradeKinds =
        [("Test", 3, true), ("Opakování", 2, true), ("Ústní zkoušení", 1, false), ("Referát", 1, false), ("Pracovní list", 1, true)];

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
            foreach (var (abbr, hours) in info.Plan)
            {
                var kinds = GradeKinds.GetValueOrDefault(abbr) ?? DefaultGradeKinds;
                var count = (int)Math.Round(weeks * Math.Min(hours, 4) * 0.25);
                for (var i = 0; i < count; i++)
                {
                    // Leave the first days of the year ungraded.
                    var day = days[rng.Next(Math.Min(4, days.Count - 1), days.Count)];
                    var (note, weight, wholeClass) = Pick(kinds);
                    var graded = wholeClass
                        ? info.Students.Where(_ => rng.NextDouble() < 0.94)
                        : info.Students.OrderBy(_ => rng.Next()).Take(rng.Next(2, 6));
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
                        for (var j = i; j < Math.Min(days.Count, i + length); j++)
                        {
                            var sickDay = days[j];
                            AddAbsence(student, sickDay, Enumerable.Range(1, info.DayLengths[(int)sickDay.DayOfWeek - 1]).ToList(), reason, recent);
                        }
                        i += length;
                    }
                    else if (rng.NextDouble() < 0.006)
                    {
                        var start = rng.Next(1, Math.Max(2, dayLength - 1));
                        AddAbsence(student, day, Enumerable.Range(start, Math.Min(2, dayLength - start + 1)).ToList(), Pick(PartialReasons), recent);
                    }
                }
            }
        }
    }

    private void AddAbsence(StudentInfo student, DateOnly day, List<int> periods, string reason, DateOnly recent)
    {
        AbsenceStatus status;
        string? excuse = reason;
        var roll = rng.NextDouble();
        if (day < recent)
        {
            status = roll < 0.95 ? AbsenceStatus.Excused : roll < 0.98 ? AbsenceStatus.Unexcused : AbsenceStatus.Rejected;
        }
        else
        {
            status = roll < 0.4 ? AbsenceStatus.PendingExcuse : roll < 0.75 ? AbsenceStatus.Unexcused : AbsenceStatus.Excused;
        }
        if (status == AbsenceStatus.Unexcused)
        {
            excuse = null;
        }
        db.Absences.Add(new Absence
        {
            SchoolId = school.Id,
            StudentId = student.Student.Id,
            Date = day,
            Periods = periods,
            Status = status,
            ExcuseReason = excuse,
            CreatedAt = At(day, 8, 10),
        });
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
            var mainSubjects = info.Plan.Keys.Where(s => s is "Čj" or "M" or "Aj" or "Nj" or "Př" or "D" or "Z" or "F" or "Bi" or "Ch" or "Prv" or "Přv" or "Vl").ToList();
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
    }

    private DemoFeaturedUsers PickFeatured(User admin, List<ClassInfo> classes)
    {
        // A class teacher of an upper class — many subjects, many classes.
        var featuredClass = classes.FirstOrDefault(c => c.Grade == (school.Type == SchoolType.Elementary ? 6 : 2)) ?? classes[0];
        var teacher = featuredClass.ClassTeacher.User;

        var student = featuredClass.Students.FirstOrDefault(s => s.Student.User is not null)
            ?? classes.SelectMany(c => c.Students).FirstOrDefault(s => s.Student.User is not null);
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
        if (student.Student.User!.Id == 0 || parent.Id == 0)
        {
            // Accounts added just now — the ids come with the next save.
            db.ChangeTracker.DetectChanges();
            db.SaveChanges();
        }
        return new DemoFeaturedUsers(admin.Id, teacher.Id, parent.Id, student.Student.User!.Id);
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
                .Concat(s.Student.User is { } own ? [own.Id] : [])
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
                var firstBody = parentFirst ? parentText : string.Format(teacherText, student.Student.FirstName);
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
                        Body = parentFirst ? string.Format(teacherText, student.Student.FirstName) : parentText,
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
