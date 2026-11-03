using Soovioo.Api.Evidence;
using Soovioo.Api.Timetable;
using static Soovioo.Api.Timetable.TimetableGenerator;

namespace Soovioo.Api.Tests.Timetable;

public class TimetableGeneratorTests
{
    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static Result Run(
        IReadOnlyList<Demand> demands, IReadOnlyList<Kept>? kept = null, IReadOnlyList<Room>? rooms = null,
        Dictionary<int, IReadOnlyCollection<DayOfWeek>>? teacherDays = null, int periods = 6) =>
        Generate(new Input(Week, [.. Enumerable.Range(1, periods)], demands, kept ?? [], rooms ?? [],
            teacherDays ?? [], TimeSpan.FromSeconds(5)));

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
    public void ATeacherOnlyOnTheirDaysAndWhatDoesNotFitIsReported()
    {
        var result = Run([new Demand(1, 10, 100, 5, 0)],
            teacherDays: new() { [100] = [DayOfWeek.Monday] });

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
    public void ASubjectGetsAFreeClassroomOfItsType()
    {
        Room[] rooms = [new(1, ClassroomType.Gym), new(2, ClassroomType.Computer)];
        var kept = new Kept(3, 14, null, 1, DayOfWeek.Monday, 1);

        var result = Run(
            [new Demand(1, 10, 101, 3, 0, ClassroomType: ClassroomType.Gym),
             new Demand(2, 10, 102, 3, 0, ClassroomType: ClassroomType.Gym)],
            [kept], rooms);

        Assert.Empty(result.Unplaced);
        Assert.All(result.Placements, p => Assert.Equal(1, p.ClassroomId));
        Assert.All(result.Placements.GroupBy(p => (p.Day, p.Period)), g => Assert.Single(g));
        Assert.DoesNotContain(result.Placements, p => p.Day == DayOfWeek.Monday && p.Period == 1);
    }

    [Fact]
    public void HalfAnHourIsALessonEveryOtherWeek()
    {
        var result = Run([new Demand(1, 10, 100, 1, 1)]);

        Assert.Equal([WeekParity.Every, WeekParity.Odd], result.Placements.Select(p => p.Parity).Order());
    }
}
