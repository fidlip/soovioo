using Soovioo.Api.People;
using Soovioo.Api.Timetable;
using static Soovioo.Api.Timetable.TimetableGenerator;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableGeneratorTests
{
    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static Result Run(
        IReadOnlyList<Demand> demands, IReadOnlyList<Kept>? kept = null,
        Dictionary<int, Teacher>? teachers = null, int periods = 6) =>
        Generate(new Input(Week, [.. Enumerable.Range(1, periods)], demands, kept ?? [],
            teachers ?? [], TimeSpan.FromSeconds(5)));

    private static Teacher DaysOff(params DayOfWeek[] days) =>
        new([.. days.Select(d => new AvailabilitySlot(d, null, AvailabilityKind.Unavailable))]);

    [Fact]
    public void EveryLessonFindsAPlaceWithoutClashesOrGaps()
    {
        // Two classes, 22 h each; the maths teacher teaches both.
        List<Demand> demands = [];
        foreach (var classId in new[] { 1, 2 })
        {
            demands.Add(new Demand(classId, 10, 100, 5, 0));
            demands.Add(new Demand(classId, 11, 100 + classId, 8, 0));
            demands.Add(new Demand(classId, 12, 200 + classId, 4, 0));
            demands.Add(new Demand(classId, 13, 300 + classId, 5, 0));
        }

        var result = Run(demands);

        Assert.True(result.Solved);
        Assert.Empty(result.Unplaced);
        Assert.Equal(44, result.Placements.Count);
        Assert.All(result.Placements.GroupBy(p => (p.ClassId, p.Day, p.Period)), g => Assert.Single(g));
        Assert.All(result.Placements.Where(p => p.TeacherId is not null).GroupBy(p => (p.TeacherId, p.Day, p.Period)),
            g => Assert.Single(g));
        // A class's day starts with the first period and has no gap.
        foreach (var day in result.Placements.GroupBy(p => (p.ClassId, p.Day)))
        {
            Assert.Equal(Enumerable.Range(1, day.Count()), day.Select(p => p.Period).Order());
        }
        // Maths once a day.
        Assert.All(result.Placements.Where(p => p.SubjectId == 10).GroupBy(p => (p.ClassId, p.Day)), g => Assert.Single(g));
    }

    [Fact]
    public void AClassHasLessonsEveryDay()
    {
        // A first grade, 20 h, mostly its class teacher's — four days of
        // five lessons would save the teachers blocks.
        var result = Run([
            new Demand(1, 10, 100, 8, 0), new Demand(1, 11, 100, 4, 0), new Demand(1, 12, 100, 2, 0),
            new Demand(1, 13, 100, 1, 0), new Demand(1, 14, 200, 2, 0), new Demand(1, 15, 200, 1, 0),
            new Demand(1, 16, 200, 1, 0), new Demand(1, 17, 300, 1, 0)]);

        Assert.Empty(result.Unplaced);
        Assert.All(Week, day => Assert.Equal(4, result.Placements.Count(p => p.Day == day)));
    }

    [Fact]
    public void ATeacherOnlyOnTheirDaysAndWhatDoesNotFitIsReported()
    {
        var result = Run([new Demand(1, 10, 100, 5, 0)],
            teachers: new() { [100] = DaysOff([.. Week.Skip(1)]) });

        Assert.True(result.Solved);
        Assert.All(result.Placements, p => Assert.Equal(DayOfWeek.Monday, p.Day));
        // 5 lessons a week make at most 2 a day.
        Assert.Equal(2, result.Placements.Count);
        Assert.Equal(new Unplaced(1, 10, 100, 3), Assert.Single(result.Unplaced));
    }

    [Fact]
    public void KeptLessonsStayAndBlockTheirSlot()
    {
        var kept = new Kept(1, 10, 100, null, DayOfWeek.Monday, 1);

        var result = Run([new Demand(1, 11, 100, 4, 0), new Demand(2, 12, 100, 4, 0)], [kept]);

        Assert.Empty(result.Unplaced);
        Assert.DoesNotContain(result.Placements, p => p.Day == DayOfWeek.Monday && p.Period == 1);
    }

    [Fact]
    public void ASubjectGetsAFreeClassroom()
    {
        var kept = new Kept(3, 14, null, 1, DayOfWeek.Monday, 1);

        var result = Run([new Demand(1, 10, 101, 3, 0, [1]), new Demand(2, 10, 102, 3, 0, [1])], [kept]);

        Assert.Empty(result.Unplaced);
        Assert.All(result.Placements, p => Assert.Equal(1, p.ClassroomId));
        Assert.All(result.Placements.GroupBy(p => (p.Day, p.Period)), g => Assert.Single(g));
        Assert.DoesNotContain(result.Placements, p => p.Day == DayOfWeek.Monday && p.Period == 1);
    }

    [Fact]
    public void TheFirstChoiceFirstAndAClassKeepsItsClassroom()
    {
        static Demand Pe(int lessons, params int[] rooms) => new(1, 10, 101, lessons, 0, rooms);
        static void InRoom(Result result, int roomId, int count)
        {
            Assert.Empty(result.Unplaced);
            Assert.Equal(count, result.Placements.Count);
            Assert.All(result.Placements, p => Assert.Equal(roomId, p.ClassroomId));
        }

        InRoom(Run([Pe(2, 2, 1)]), 2, 2);
        // Gym 1 is busy Monday to Wednesday: the class moves to gym 2 for the whole week.
        InRoom(Run([Pe(5, 1, 2)], [.. from day in Week.Take(3) from period in Enumerable.Range(1, 6)
                                      select new Kept(99, 50, null, 1, day, period)]), 2, 5);
        // It already has a lesson in gym 1.
        InRoom(Run([Pe(2, 2, 1)], [new Kept(1, 10, 101, 1, DayOfWeek.Friday, 6)]), 1, 2);
    }

    [Fact]
    public void ALessonNoClassroomIsLeftForGoesInWithoutOne()
    {
        // The only gym is free on Monday first period only.
        List<Kept> kept = [.. from day in Week from period in Enumerable.Range(1, 6)
                              where (day, period) != (DayOfWeek.Monday, 1)
                              select new Kept(99, 50, null, 1, day, period)];

        var result = Run([new Demand(1, 10, 101, 1, 0, [1]), new Demand(2, 10, 102, 1, 0, [1])], kept);

        Assert.Empty(result.Unplaced);
        Assert.Single(result.Placements, p => p.ClassroomId == 1 && p.Day == DayOfWeek.Monday && p.Period == 1);
        Assert.Single(result.Placements, p => p.ClassroomId is null);
    }

    [Fact]
    public void HalfAnHourIsALessonEveryOtherWeek()
    {
        var result = Run([new Demand(1, 10, 100, 1, 1)]);

        Assert.Equal([WeekParity.Every, WeekParity.Odd], result.Placements.Select(p => p.Parity).Order());
    }

    [Fact]
    public void NeverWhenTheTeacherCannotAndRatherNotOnlyWhenItMust()
    {
        // Can't the first two periods, would rather not the last one.
        Teacher teacher = new([.. from day in Week from period in new[] { 1, 2, 6 }
                               select new AvailabilitySlot(day, period, period == 6 ? AvailabilityKind.Avoid : AvailabilityKind.Unavailable)]);

        var result = Run([new Demand(1, 10, 100, 5, 0), new Demand(1, 11, 101, 15, 0)], teachers: new() { [100] = teacher });

        Assert.Empty(result.Unplaced);
        Assert.All(result.Placements.Where(p => p.TeacherId == 100), p => Assert.InRange(p.Period, 3, 5));
    }

    [Fact]
    public void ABlockIsTwoLessonsInARowInOneClassroom()
    {
        var result = Run([new Demand(1, 10, 100, 5, 0, [1, 2], DoubleLessons: true), new Demand(1, 11, 101, 10, 0)]);

        Assert.Empty(result.Unplaced);
        var days = result.Placements.Where(p => p.SubjectId == 10).GroupBy(p => p.Day).ToList();
        Assert.Equal([1, 2, 2], days.Select(d => d.Count()).Order());
        Assert.All(days.Where(d => d.Count() == 2), d =>
        {
            Assert.Equal(1, Math.Abs(d.First().Period - d.Last().Period));
            Assert.Single(d.Select(p => p.ClassroomId).Distinct());
        });
    }

    [Fact]
    public void TheTeachersWishes()
    {
        // 6 lessons in two classes: two free days and 2–3 a day want three
        // days of two; without gaps.
        Dictionary<int, Teacher> teachers = new()
        {
            [100] = new([], FreeDays: 2, new TeacherWishes(MinLessonsPerDay: 2, MaxLessonsPerDay: 3, MaxGapsPerDay: 0)),
        };

        var result = Run([new Demand(1, 10, 100, 3, 0), new Demand(1, 11, 101, 12, 0),
            new Demand(2, 10, 100, 3, 0), new Demand(2, 11, 102, 12, 0)], teachers: teachers);

        Assert.Empty(result.Unplaced);
        var days = result.Placements.Where(p => p.TeacherId == 100).GroupBy(p => p.Day).ToList();
        Assert.Equal(3, days.Count);
        Assert.All(days, d => Assert.Equal(2, d.Count()));
        Assert.All(days, d => Assert.Equal(0, Holes(d.Select(p => p.Period))));
    }

    [Fact]
    public void AClassWhoseTeacherCannotTheFirstPeriodStartsWithTheSecondAndTheSearchDoesNotRunToTheLimit()
    {
        // The class teacher has 19 of 20 lessons and can't teach the first
        // period: four days start late, one lost period each, not two.
        Teacher teacher = new([.. Week.Select(d => new AvailabilitySlot(d, 1, AvailabilityKind.Unavailable))]);
        var started = Environment.TickCount64;

        var result = Generate(new Input(Week, [.. Enumerable.Range(1, 6)],
            [new Demand(1, 10, 100, 10, 0), new Demand(1, 11, 100, 9, 0), new Demand(1, 12, 101, 1, 0)], [],
            new Dictionary<int, Teacher> { [100] = teacher }, TimeSpan.FromMinutes(2))
        {
            RequirementsStall = TimeSpan.FromSeconds(3),
            PolishStall = TimeSpan.FromSeconds(1),
        });

        Assert.Empty(result.Unplaced);
        Assert.False(result.TimedOut);
        Assert.True(Environment.TickCount64 - started < 60_000);
        Assert.All(result.Placements.GroupBy(p => p.Day), d => Assert.True(d.Min(p => p.Period) <= 2));
    }

    /// <summary>Teacher 100 has 2 lessons in each of classes 1–3, whose other
    /// lessons (14 a week) are another teacher's.</summary>
    private static Result ThreeClasses(Teacher teacher) =>
        Run([.. new[] { 1, 2, 3 }.SelectMany(c => new[] { new Demand(c, 10, 100, 2, 0), new Demand(c, 11, 100 + c, 14, 0) })],
            teachers: new() { [100] = teacher });

    private static List<IGrouping<DayOfWeek, Placement>> DaysOf100(Result result) =>
        [.. result.Placements.Where(p => p.TeacherId == 100).GroupBy(p => p.Day)];

    [Fact]
    public void FreeDaysAlone()
    {
        var result = ThreeClasses(new([], FreeDays: 3));

        Assert.Empty(result.Unplaced);
        Assert.Equal(2, DaysOf100(result).Count);
    }

    [Fact]
    public void AtLeastLessonsADayAlone()
    {
        var result = ThreeClasses(new([], Wishes: new TeacherWishes(MinLessonsPerDay: 3)));

        Assert.Empty(result.Unplaced);
        Assert.All(DaysOf100(result), d => Assert.True(d.Count() >= 3));
    }

    [Fact]
    public void AtMostLessonsADayAlone()
    {
        // Rather not Wednesday to Friday: without the wish 3 + 3 on Monday and Tuesday.
        Teacher teacher = new([.. from day in Week.Skip(2) from period in Enumerable.Range(1, 6)
                               select new AvailabilitySlot(day, period, AvailabilityKind.Avoid)],
            Wishes: new TeacherWishes(MaxLessonsPerDay: 2));

        var result = ThreeClasses(teacher);

        Assert.Empty(result.Unplaced);
        Assert.All(DaysOf100(result), d => Assert.True(d.Count() <= 2));
    }

    [Fact]
    public void AtMostGapsADayAlone()
    {
        // Two lessons a day, only in periods 1–3 and rather not the second:
        // without the wish 1st and 3rd with a gap between.
        List<AvailabilitySlot> slots = [.. from day in Week from period in Enumerable.Range(2, 5)
                                           select new AvailabilitySlot(day, period, period == 2 ? AvailabilityKind.Avoid : AvailabilityKind.Unavailable)];
        slots.RemoveAll(a => a.Period == 3);

        var result = Run([new Demand(1, 10, 100, 5, 0), new Demand(1, 11, 100, 5, 0), new Demand(1, 12, 101, 10, 0)],
            teachers: new() { [100] = new(slots, Wishes: new TeacherWishes(MaxGapsPerDay: 0)) });

        Assert.Empty(result.Unplaced);
        Assert.All(DaysOf100(result), d => Assert.Equal(0, Holes(d.Select(p => p.Period))));
    }

    [Fact]
    public void GroupsOfADivisionAreTaughtAtOnceEachWithItsTeacherAndClassroom()
    {
        // 2.A and 2.B: Tv in two groups, girls in gym 1 and boys in gym 2,
        // each class also maths of its own.
        var tv = new Demand(1, 20, 100, 2, 0, [1, 2])
        {
            ClassIds = [1, 2],
            Parts = [new Part(7, 100, [1, 2], 20), new Part(8, 101, [1, 2], 21)],
            Key = -1_000_001,
        };
        var result = Run([tv, new Demand(1, 10, 200, 4, 0), new Demand(2, 10, 201, 4, 0)]);

        Assert.Empty(result.Unplaced);
        var girls = result.Placements.Where(p => p.GroupId == 7).ToList();
        var boys = result.Placements.Where(p => p.GroupId == 8).ToList();
        Assert.Equal(2, girls.Count);
        Assert.Equal(girls.Select(p => (p.Day, p.Period)).Order(), boys.Select(p => (p.Day, p.Period)).Order());
        Assert.All(girls, p => Assert.Equal((20, 100), (p.SubjectId, p.TeacherId!.Value)));
        Assert.All(boys, p => Assert.Equal((21, 101), (p.SubjectId, p.TeacherId!.Value)));
        // Two gyms at once, never the same one.
        Assert.All(girls.Concat(boys), p => Assert.NotNull(p.ClassroomId));
        Assert.All(girls, g => Assert.NotEqual(g.ClassroomId, boys.Single(b => b.Day == g.Day && b.Period == g.Period).ClassroomId));
        // Neither class has maths then.
        var tvSlots = girls.Select(p => (p.Day, p.Period)).ToHashSet();
        Assert.DoesNotContain(result.Placements, p => p.GroupId is null && tvSlots.Contains((p.Day, p.Period)));
    }

    [Fact]
    public void AGroupTakesAllItsClassesAndALessonOfAGroupKeptBlocksThem()
    {
        // A seminar of 9.A and 9.B pupils; one of its lessons is kept, one of a
        // group of 9.B alone too.
        var seminar = new Demand(1, 30, 100, 2, 0) { ClassIds = [1, 2], Parts = [new Part(5, 100, [])], Key = -5 };
        var kept = new[]
        {
            new Kept(2, 31, 300, null, DayOfWeek.Monday, 1, -6, 6),
        };
        var result = Run([seminar, new Demand(1, 10, 200, 5, 0), new Demand(2, 10, 201, 4, 0)], kept);

        Assert.Empty(result.Unplaced);
        var seminarSlots = result.Placements.Where(p => p.GroupId == 5).Select(p => (p.Day, p.Period)).ToHashSet();
        Assert.Equal(2, seminarSlots.Count);
        Assert.DoesNotContain((DayOfWeek.Monday, 1), seminarSlots);
        Assert.DoesNotContain(result.Placements, p => p.GroupId is null && seminarSlots.Contains((p.Day, p.Period)));
        Assert.DoesNotContain(result.Placements, p => p.ClassId == 2 && p.Day == DayOfWeek.Monday && p.Period == 1);
    }

    [Fact]
    public void ADivisionWaitsForAllItsTeachers()
    {
        var division = new Demand(1, 20, 100, 2, 0)
        {
            ClassIds = [1], Parts = [new Part(7, 100, []), new Part(8, 101, [])], Key = -1_000_001,
        };
        var result = Run([division], teachers: new() { [101] = DaysOff([.. Week.Where(d => d != DayOfWeek.Wednesday)]) });

        Assert.All(result.Placements, p => Assert.Equal(DayOfWeek.Wednesday, p.Day));
        Assert.Equal(2, result.Placements.Count(p => p.GroupId == 7));
    }
}
