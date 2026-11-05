using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Grades;
using Soovioo.Api.Homework;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Tests.Timetable;

public class StudentGroupsTests
{
    private const int SchoolId = 1;

    private readonly AppDbContext db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeCurrentSchoolContext(SchoolId));

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero));
    private readonly TimetableVersion draft;
    private readonly SchoolClass a, b;
    private readonly Subject pe, maths, german;
    private readonly Classroom gym1, gym2, small;
    private readonly User novakova, dvorak, kral;
    private readonly StudentProfile anna, petr, eva, karel;

    public StudentGroupsTests()
    {
        a = new SchoolClass { Name = "2.A", Rocnik = 2, SchoolId = SchoolId };
        b = new SchoolClass { Name = "2.B", Rocnik = 2, SchoolId = SchoolId };
        gym1 = new Classroom { Name = "Tělocvična 1", Type = ClassroomType.Gym, Capacity = 30, SchoolId = SchoolId };
        gym2 = new Classroom { Name = "Tělocvična 2", Type = ClassroomType.Gym, Capacity = 30, SchoolId = SchoolId };
        small = new Classroom { Name = "Kabinet", Capacity = 1, SchoolId = SchoolId };
        pe = new Subject { Name = "Tělesná výchova", Abbreviation = "Tv", Classrooms = [new() { Type = ClassroomType.Gym }], SchoolId = SchoolId };
        maths = new Subject { Name = "Matematika", Abbreviation = "M", SchoolId = SchoolId };
        german = new Subject { Name = "Německý jazyk", Abbreviation = "Nj", SchoolId = SchoolId };
        draft = new TimetableVersion
        {
            SchoolId = SchoolId, Name = "Od února", ValidFrom = new DateOnly(2027, 2, 1), Status = TimetableVersionStatus.Draft,
        };
        db.AddRange(a, b, gym1, gym2, small, pe, maths, german, draft);
        db.SaveChanges();
        novakova = Teacher("Nováková", pe, maths);
        dvorak = Teacher("Dvořák", pe, maths);
        kral = Teacher("Král", german);
        anna = Pupil("Anna", a);
        petr = Pupil("Petr", a);
        eva = Pupil("Eva", b);
        karel = Pupil("Karel", b);
    }

    private User Teacher(string lastName, params Subject[] subjects)
    {
        var user = new User { Username = lastName.ToLowerInvariant(), PasswordHash = "x", SchoolId = SchoolId, Roles = [db.Role(UserRole.Teacher)] };
        db.Users.Add(user);
        db.Persons.Add(new Person
        {
            SchoolId = SchoolId, FirstName = "", LastName = lastName, User = user,
            TeacherProfile = new TeacherProfile
            {
                SchoolId = SchoolId, Qualifications = [.. subjects.Select(s => new TeacherQualification { SchoolId = SchoolId, Subject = s })],
            },
        });
        db.SaveChanges();
        return user;
    }

    private StudentProfile Pupil(string firstName, SchoolClass schoolClass)
    {
        var student = new StudentProfile
        {
            Person = new Person { FirstName = firstName, LastName = "Žák", SchoolId = SchoolId }, SchoolClassId = schoolClass.Id, SchoolId = SchoolId,
        };
        db.Students.Add(student);
        db.SaveChanges();
        return student;
    }

    private StudentGroupsController Groups() =>
        new(db, new FakeCurrentSchoolContext(SchoolId)) { ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)) };

    private TimetableController Timetable() =>
        new(db, new FakeCurrentSchoolContext(SchoolId), clock) { ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)) };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static string Bad<T>(ActionResult<T> result) => Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    private async Task<GroupDivisionDto> Division(string name = "Tv 2.AB", decimal? hours = 2) =>
        Ok(await Groups().CreateDivision(draft.Id, new SaveGroupDivisionRequest(name, hours, null, [a.Id, b.Id])));

    private static SaveStudentGroupRequest Request(
        string name, Subject subject, User? teacher, int? divisionId, IReadOnlyList<SchoolClass> classes, params StudentProfile[] pupils) =>
        new(name, subject.Id, 2, null, teacher?.Id, null, divisionId, [.. classes.Select(c => c.Id)], [.. pupils.Select(p => p.Id)]);

    private async Task<TeachingOverviewDto> Overview() =>
        Ok(await new TeachingController(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        }.GetTeaching(draft.Id));

    [Fact]
    public async Task WithoutHoursOfItsOwnADivisionHasThePlansAndSaysWhenItIsNotClear()
    {
        Plan(pe, 2);
        db.SaveChanges();
        var division = await Division(hours: null);
        var girls = Ok(await Groups().CreateGroup(draft.Id, Request("Dívky", pe, novakova, division.Id, [], anna)));
        var seminar = Ok(await Groups().CreateGroup(draft.Id, Request("Seminář", german, kral, null, [a], anna) with { HoursPerWeek = null }));

        var overview = await Overview();
        var tv = overview.Groups.Single(g => g.Id == girls.Id);
        Assert.Equal((2m, true), (tv.PlanHours, tv.HoursByPlan));
        Assert.DoesNotContain(tv.Problems, p => p.Contains("zadejte"));
        Assert.Contains("Předmět není v učebním plánu třídy, zadejte hodiny skupiny.", overview.Groups.Single(g => g.Id == seminar.Id).Problems);

        // 2.B has Tv 3 h of its own.
        db.ClassCurriculumItems.Add(new ClassCurriculumItem
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = b.Id, SubjectId = pe.Id, HoursPerWeek = 3,
        });
        db.SaveChanges();
        Assert.Contains("V učebním plánu má 2.A Tv 2 h, 2.B Tv 3 h, zadejte hodiny dělení.",
            (await Overview()).Groups.Single(g => g.Id == girls.Id).Problems);
    }

    [Fact]
    public async Task GroupsOfADivisionGetDifferentTeachersAutomaticallyAndTheSameOneByHandIsReported()
    {
        Plan(pe, 2);
        db.SaveChanges();
        var division = await Division();
        var girls = Ok(await Groups().CreateGroup(draft.Id, Request("Dívky", pe, null, division.Id, [], anna, eva)));
        var boys = Ok(await Groups().CreateGroup(draft.Id, Request("Chlapci", pe, null, division.Id, [], petr, karel)));

        var automatic = (await Overview()).Groups;
        Assert.All(automatic, g => Assert.True(g.Automatic));
        Assert.NotEqual(automatic.Single(g => g.Id == girls.Id).TeacherId, automatic.Single(g => g.Id == boys.Id).TeacherId);

        // A third group finds nobody else qualified.
        var third = Ok(await Groups().CreateGroup(draft.Id, Request("Třetí", pe, null, division.Id, [])));
        Assert.Contains("Kdo má aprobaci pro Tv v 2. ročníku, učí jinou skupinu dělení.",
            (await Overview()).Groups.Single(g => g.Id == third.Id).Problems);
        Assert.IsType<NoContentResult>(await Groups().DeleteGroup(draft.Id, third.Id));

        Ok(await Groups().UpdateGroup(draft.Id, girls.Id, Request("Dívky", pe, dvorak, division.Id, [], anna, eva)));
        Ok(await Groups().UpdateGroup(draft.Id, boys.Id, Request("Chlapci", pe, dvorak, division.Id, [], petr, karel)));
        Assert.Contains("Dvořák učí i skupinu Chlapci téhož dělení, ve stejnou dobu.",
            (await Overview()).Groups.Single(g => g.Id == girls.Id).Problems);
    }

    /// <summary>Tv 2.AB: girls with Nováková, boys with Dvořák.</summary>
    private async Task<(StudentGroupDto Girls, StudentGroupDto Boys)> PeDivision()
    {
        var division = await Division();
        var girls = Ok(await Groups().CreateGroup(draft.Id, Request("Dívky", pe, novakova, division.Id, [], anna, eva)));
        var boys = Ok(await Groups().CreateGroup(draft.Id, Request("Chlapci", pe, dvorak, division.Id, [], petr, karel)));
        return (girls, boys);
    }

    private void Plan(Subject subject, decimal hours) =>
        db.CurriculumItems.Add(new CurriculumItem { SchoolId = SchoolId, TimetableVersionId = draft.Id, Grade = 2, SubjectId = subject.Id, HoursPerWeek = hours });

    private TimetableEntry Lesson(SchoolClass schoolClass, Subject subject, DayOfWeek day, int period, int? groupId = null, Classroom? room = null)
    {
        var entry = new TimetableEntry
        {
            SchoolId = SchoolId, TimetableVersionId = draft.Id, SchoolClassId = schoolClass.Id, SubjectId = subject.Id,
            DayOfWeek = day, PeriodNumber = period, StudentGroupId = groupId, ClassroomId = room?.Id,
        };
        db.TimetableEntries.Add(entry);
        db.SaveChanges();
        return entry;
    }

    [Fact]
    public async Task GroupsOfADivisionHaveItsClassesAndHoursAndAPupilIsInOneOfThem()
    {
        var (girls, boys) = await PeDivision();

        Assert.Equal([a.Id, b.Id], girls.ClassIds.Order());
        Assert.Equal(2, boys.HoursPerWeek);
        var twice = Bad(await Groups().UpdateGroup(draft.Id, boys.Id, Request("Chlapci", pe, dvorak, girls.DivisionId, [], petr, karel, anna)));
        Assert.Equal("Anna Žák už je ve skupině Dívky tohoto dělení.", twice);

        // Out of one, then into the other.
        Ok(await Groups().UpdateGroup(draft.Id, girls.Id, Request("Dívky", pe, novakova, girls.DivisionId, [], eva)));
        Ok(await Groups().UpdateGroup(draft.Id, boys.Id, Request("Chlapci", pe, dvorak, girls.DivisionId, [], petr, karel, anna)));
        var groups = Ok(await Groups().GetGroups(draft.Id));
        Assert.Equal([eva.Id], groups.Groups.Single(g => g.Id == girls.Id).StudentIds);
        Assert.Contains(anna.Id, groups.Groups.Single(g => g.Id == boys.Id).StudentIds);
        Assert.Equal([a.Id, b.Id], Assert.Single(groups.Divisions).ClassIds.Order());
    }

    [Fact]
    public async Task APupilOfAnotherClassIsRefused()
    {
        var error = Bad(await Groups().CreateGroup(draft.Id, Request("Nj", german, kral, null, [a], anna, eva)));

        Assert.Equal("Eva Žák není v žádné třídě skupiny.", error);
    }

    [Fact]
    public async Task GeneratingPutsTheDivisionsGroupsAtOnceForBothClassesAndTheClassesNoTvOfTheirOwn()
    {
        Plan(pe, 2);
        Plan(maths, 3);
        db.SaveChanges();
        var (girls, boys) = await PeDivision();

        var generation = new TimetableGeneration { SchoolId = SchoolId, TimetableVersionId = draft.Id, Mode = TimetableGenerationMode.All };
        db.TimetableGenerations.Add(generation);
        await TimetableGenerationJob.GenerateAsync(db, generation, TimeSpan.FromSeconds(5));
        await db.SaveChangesAsync();

        var entries = db.TimetableEntries.Where(e => e.TimetableVersionId == draft.Id).ToList();
        Assert.DoesNotContain(entries, e => e.SubjectId == pe.Id && e.StudentGroupId == null);
        var girlsLessons = entries.Where(e => e.StudentGroupId == girls.Id).ToList();
        var boysLessons = entries.Where(e => e.StudentGroupId == boys.Id).ToList();
        // One lesson per class of the group, two a week.
        Assert.Equal(4, girlsLessons.Count);
        Assert.Equal(2, girlsLessons.Count(e => e.SchoolClassId == b.Id));
        var slots = girlsLessons.Select(e => (e.DayOfWeek, e.PeriodNumber)).Distinct().Order().ToList();
        Assert.Equal(slots, boysLessons.Select(e => (e.DayOfWeek, e.PeriodNumber)).Distinct().Order());
        Assert.All(girlsLessons, e => Assert.Equal(novakova.Id, e.TeacherId));
        Assert.All(boysLessons, e => Assert.Equal(dvorak.Id, e.TeacherId));
        Assert.All(girlsLessons, e => Assert.NotEqual(e.ClassroomId,
            boysLessons.First(o => o.DayOfWeek == e.DayOfWeek && o.PeriodNumber == e.PeriodNumber).ClassroomId));
        Assert.DoesNotContain(entries, e => e.StudentGroupId == null && slots.Contains((e.DayOfWeek, e.PeriodNumber)));
        Assert.Equal(3, entries.Count(e => e.SchoolClassId == a.Id && e.SubjectId == maths.Id));

        var overview = Ok(await new TeachingController(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        }.GetTeaching(draft.Id));
        var tv = overview.Classes.Single(c => c.Id == a.Id).Rows.Single(r => r.SubjectId == pe.Id);
        Assert.Equal(["Chlapci", "Dívky"], tv.Groups);
        Assert.Empty(tv.Problems);
        var group = overview.Groups.Single(g => g.Id == girls.Id);
        Assert.Equal((2m, 2m, "2.A, 2.B", 2), (group.PlanHours, group.TimetableHours, group.ClassNames, group.MemberCount));
        Assert.Empty(group.Problems);
        // The teacher's hours count the group's lesson once.
        var teacher = overview.Teachers.Single(t => t.Id == novakova.Id);
        Assert.Equal(entries.Where(e => e.TeacherId == novakova.Id).Select(e => (e.DayOfWeek, e.PeriodNumber)).Distinct().Count(),
            teacher.TimetableHours);
        Assert.Contains(teacher.Taught, t => t.StudentGroupId == girls.Id && t.Hours == 2);
    }

    [Fact]
    public async Task APupilSeesTheirGroupAndTheTeacherTheLessonOnce()
    {
        var (girls, boys) = await PeDivision();
        Lesson(a, pe, DayOfWeek.Monday, 1, girls.Id);
        Lesson(b, pe, DayOfWeek.Monday, 1, girls.Id);
        Lesson(a, pe, DayOfWeek.Monday, 1, boys.Id);
        Lesson(b, pe, DayOfWeek.Monday, 1, boys.Id);
        db.TimetableEntries.Where(e => e.StudentGroupId == girls.Id).ToList().ForEach(e => e.TeacherId = novakova.Id);
        db.SaveChanges();

        var forClass = Ok(await Timetable().GetTimetable(a.Id, draft.Id)).ToList();
        var forAnna = Ok(await Timetable().GetTimetable(a.Id, draft.Id, studentId: anna.Id)).ToList();
        var forTeacher = Ok(await Timetable().GetTeacherTimetable(novakova.Id, draft.Id)).ToList();

        Assert.Equal(2, forClass.Count);
        Assert.Equal("Dívky", Assert.Single(forAnna).StudentGroupName);
        Assert.Equal("Dívky (2.A, 2.B)", Assert.Single(forTeacher).SchoolClassName);
    }

    [Fact]
    public async Task AGroupsLessonIsAddedChangedAndDeletedForAllItsClasses()
    {
        var group = Ok(await Groups().CreateGroup(draft.Id, Request("Nj", german, kral, null, [a, b], anna, eva)));

        var added = Assert.IsType<TimetableEntryDto>(Assert.IsType<CreatedAtActionResult>((await Timetable().AddEntry(
            b.Id, new CreateTimetableEntryRequest(german.Id, DayOfWeek.Tuesday, 2, WeekParity.Every, kral.Id, null, draft.Id,
                StudentGroupId: group.Id))).Result).Value);
        Assert.Equal(2, db.TimetableEntries.Count(e => e.StudentGroupId == group.Id && e.DayOfWeek == DayOfWeek.Tuesday));

        Ok(await Timetable().UpdateEntry(b.Id, added.Id,
            new CreateTimetableEntryRequest(german.Id, DayOfWeek.Friday, 3, WeekParity.Every, kral.Id)));
        Assert.All(db.TimetableEntries.Where(e => e.StudentGroupId == group.Id), e => Assert.Equal((DayOfWeek.Friday, 3), (e.DayOfWeek, e.PeriodNumber)));

        var wrongSubject = await Timetable().AddEntry(a.Id, new CreateTimetableEntryRequest(
            maths.Id, DayOfWeek.Monday, 1, WeekParity.Every, null, null, draft.Id, StudentGroupId: group.Id));
        Assert.Equal("Hodina skupiny má předmět skupiny.", Bad(wrongSubject));

        await Timetable().DeleteEntry(a.Id, added.Id + 1);
        Assert.Empty(db.TimetableEntries.Where(e => e.StudentGroupId == group.Id));
    }

    [Fact]
    public async Task AClassOfAGroupWithLessonsGetsThemTooAndLosesThem()
    {
        var group = Ok(await Groups().CreateGroup(draft.Id, Request("Nj", german, kral, null, [a], anna)));
        Lesson(a, german, DayOfWeek.Monday, 3, group.Id);

        Ok(await Groups().UpdateGroup(draft.Id, group.Id, Request("Nj", german, kral, null, [a, b], anna, eva)));
        Assert.Equal([a.Id, b.Id], db.TimetableEntries.Where(e => e.StudentGroupId == group.Id).Select(e => e.SchoolClassId).Order());

        Ok(await Groups().UpdateGroup(draft.Id, group.Id, Request("Nj", german, kral, null, [b], eva)));
        Assert.Equal(b.Id, Assert.Single(db.TimetableEntries.Where(e => e.StudentGroupId == group.Id)).SchoolClassId);
    }

    [Fact]
    public async Task APupilWithAnotherLessonThenOrOneTooManyForTheClassroomIsRefused()
    {
        var group = Ok(await Groups().CreateGroup(draft.Id, Request("Seminář", german, kral, null, [a, b], eva)));
        Lesson(a, german, DayOfWeek.Monday, 1, group.Id, small);
        Lesson(b, german, DayOfWeek.Monday, 1, group.Id, small);
        Lesson(a, maths, DayOfWeek.Monday, 1);

        var busy = Bad(await Groups().UpdateGroup(draft.Id, group.Id, Request("Seminář", german, kral, null, [a, b], petr)));
        var full = Bad(await Groups().UpdateGroup(draft.Id, group.Id, Request("Seminář", german, kral, null, [a, b], eva, karel)));

        Assert.Equal("Petr Žák má v pondělí 1. hod. hodinu 2.A M, kdy má skupina hodinu. Upravte nejdřív rozvrh.", busy);
        Assert.StartsWith("Skupina by měla 2 žáků, učebna Kabinet má kapacitu 1", full);
        // Who's in it already doesn't hold up other changes.
        Ok(await Groups().UpdateGroup(draft.Id, group.Id, Request("Seminář Nj", german, kral, null, [a, b], eva)));
    }

    [Fact]
    public async Task TheCheckBeforePublishingFindsPupilsWithoutAGroupAndAGroupTooBigForItsRoom()
    {
        var division = await Division();
        var girls = Ok(await Groups().CreateGroup(draft.Id, Request("Dívky", pe, novakova, division.Id, [], anna, eva)));
        Ok(await Groups().CreateGroup(draft.Id, Request("Chlapci", pe, dvorak, division.Id, [], petr)));
        Lesson(a, pe, DayOfWeek.Monday, 1, girls.Id, small);
        Lesson(b, pe, DayOfWeek.Monday, 1, girls.Id, small);
        // Two groups of a class at once are fine, a lesson of the whole class not.
        Lesson(a, maths, DayOfWeek.Monday, 1);

        var problems = await TimetableCheck.ProblemsAsync(db, draft);

        Assert.Contains("Tv 2.AB: 1 žák není v žádné skupině, má volno (Karel Žák).", problems);
        Assert.Contains("Dívky: 2 žáků se nevejde do učebny Kabinet (kapacita 1), po 1. hod.", problems);
        Assert.Contains("Třída 2.A má dvě hodiny najednou, po 1. hod.: 2.A M, Dívky Tv.", problems);
        Assert.DoesNotContain(problems, p => p.StartsWith("Třída 2.B"));
    }

    [Fact]
    public async Task ACopiedVersionHasTheGroupsWithTheirPupilsAndLessons()
    {
        var (girls, _) = await PeDivision();
        Lesson(a, pe, DayOfWeek.Monday, 1, girls.Id);

        var created = await new TimetableVersionsController(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        }.CreateVersion(new CreateTimetableVersionRequest("Kopie", new DateOnly(2027, 9, 1), draft.Id));
        var copy = Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var groups = Ok(await Groups().GetGroups(copy.Id));
        Assert.Equal("Tv 2.AB", Assert.Single(groups.Divisions).Name);
        var copiedGirls = groups.Groups.Single(g => g.Name == "Dívky");
        Assert.NotEqual(girls.Id, copiedGirls.Id);
        Assert.Equal([anna.Id, eva.Id], copiedGirls.StudentIds.Order());
        Assert.Equal(copiedGirls.Id, db.TimetableEntries.Single(e => e.TimetableVersionId == copy.Id).StudentGroupId);
    }

    [Fact]
    public async Task ADivisionCoversARowOfThePlanItsGroupsSubjectsAreChosenFor()
    {
        var second = new Subject { Name = "Další cizí jazyk", Abbreviation = "Dcj", SchoolId = SchoolId };
        var french = new Subject { Name = "Francouzský jazyk", Abbreviation = "Fj", SchoolId = SchoolId };
        db.AddRange(second, french);
        db.SaveChanges();
        Plan(second, 2);
        db.SaveChanges();
        Assert.Equal("Neznámý předmět.", Bad(await Groups().CreateDivision(draft.Id,
            new SaveGroupDivisionRequest("Jazyky", null, null, [a.Id, b.Id], 9999))));
        var languages = Ok(await Groups().CreateDivision(draft.Id, new SaveGroupDivisionRequest("Jazyky", null, null, [a.Id, b.Id], second.Id)));
        Assert.Equal(second.Id, languages.CoversSubjectId);
        Ok(await Groups().CreateGroup(draft.Id, Request("Nj", german, kral, languages.Id, [], anna, eva) with { HoursPerWeek = null }));
        Ok(await Groups().CreateGroup(draft.Id, Request("Fj", french, dvorak, languages.Id, [], petr, karel) with { HoursPerWeek = null }));

        // The row is taught by the groups, their hours are the row's.
        var overview = await Overview();
        var row = overview.Classes.Single(c => c.Id == a.Id).Rows.Single(r => r.SubjectId == second.Id);
        Assert.Equal(["Fj", "Nj"], row.Groups);
        Assert.Empty(row.Problems);
        Assert.Null(row.TeacherId);
        Assert.All(overview.Groups, g => Assert.Equal((2m, true), (g.PlanHours, g.HoursByPlan)));
        Assert.DoesNotContain(overview.Classes.SelectMany(c => c.Rows), r => r.SubjectId == french.Id || r.SubjectId == german.Id);

        // Divisions covering it together have its hours.
        var more = Ok(await Groups().CreateDivision(draft.Id, new SaveGroupDivisionRequest("Konverzace", 1, null, [a.Id, b.Id], second.Id)));
        Ok(await Groups().CreateGroup(draft.Id, Request("Konverzace Nj", german, kral, more.Id, [], anna)));
        Assert.Contains("Dělení, která ho učí, mají 3 h, v plánu je 2 h.",
            (await Overview()).Classes.Single(c => c.Id == a.Id).Rows.Single(r => r.SubjectId == second.Id).Problems);
        Assert.Equal(2, Ok(await Groups().UpdateDivision(draft.Id, languages.Id,
            new SaveGroupDivisionRequest("Jazyky", 1, null, [a.Id, b.Id], second.Id))).ClassIds.Count);
        Assert.Empty((await Overview()).Classes.Single(c => c.Id == a.Id).Rows.Single(r => r.SubjectId == second.Id).Problems);

        // A placeholder: not graded, no homework of it.
        var subjects = Assert.IsAssignableFrom<IEnumerable<Subject>>(Assert.IsType<OkObjectResult>(
            (await new SubjectsController(db, new FakeCurrentSchoolContext(SchoolId)).GetSubjects()).Result).Value).ToList();
        Assert.Equal([second.Id], subjects.Where(s => s.CoveredByDivisions).Select(s => s.Id));
        var admin = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin));
        var grades = new GradesController(db, new FakeCurrentSchoolContext(SchoolId), new Notifier(db, TimeProvider.System)) { ControllerContext = admin };
        Assert.Equal("Předmět se učí ve skupinách — známku dejte z předmětu skupiny.",
            Bad(await grades.AddGrade(new CreateGradeRequest(anna.Id, second.Id, 1, 1, null))));
        Assert.IsType<CreatedAtActionResult>((await grades.AddGrade(new CreateGradeRequest(anna.Id, german.Id, 1, 1, null))).Result);
        var homework = new HomeworkController(db, new FakeCurrentSchoolContext(SchoolId),
            new MessageAttachmentStorageService(new FakeWebHostEnvironment()), new Notifier(db, TimeProvider.System)) { ControllerContext = admin };
        Assert.Equal("Předmět se učí ve skupinách — úkol zadejte z předmětu skupiny.",
            Bad(await homework.CreateHomework(new CreateHomeworkRequest(a.Id, second.Id, "Slovíčka", new DateOnly(2026, 11, 10), false))));

        // A copied version keeps the row its division covers.
        var created = await new TimetableVersionsController(db, new FakeCurrentSchoolContext(SchoolId), clock)
        {
            ControllerContext = TestRoles.Context(TestRoles.Principal(db, 900, UserRole.Admin)),
        }.CreateVersion(new CreateTimetableVersionRequest("Kopie", new DateOnly(2027, 9, 1), draft.Id));
        var copy = Assert.IsType<TimetableVersionDto>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.All(Ok(await Groups().GetGroups(copy.Id)).Divisions, d => Assert.Equal(second.Id, d.CoversSubjectId));
    }
}
