using Google.OrTools.Sat;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Places lessons into the week with OR-Tools CP-SAT. Hard: a class
/// and a teacher at most once at a time, the teacher's days, at most a
/// balanced number of lessons a day for a class. Soft (fixed weights): a
/// free classroom for each lesson, lessons every day, no gaps for classes (a
/// day starts with the first lesson), a subject at most once a day, few gaps
/// for teachers, no 0. hodina. What doesn't fit is reported, not forced; a
/// lesson no classroom is left for goes in without one.</summary>
public static class TimetableGenerator
{
    private const int MissingWeight = 1000;
    private const int NoRoomWeight = 200;
    private const int ClassGapWeight = 10;
    private const int ShortDayWeight = 20;
    private const int EarlyPeriodWeight = 8;
    private const int SubjectTwiceWeight = 6;
    private const int TeacherGapWeight = 2;

    /// <param name="Weekly">Lessons every week.</param>
    /// <param name="Biweekly">Lessons every other week (0.5 h); placed in
    /// odd weeks and holding the slot in both.</param>
    /// <param name="Rooms">Classrooms it can have, first choice first; empty
    /// = none needed.</param>
    public record Demand(
        int ClassId, int SubjectId, int? TeacherId, int Weekly, int Biweekly, IReadOnlyList<int>? Rooms = null)
    {
        public IReadOnlyList<int> Rooms { get; init; } = Rooms ?? [];
    }

    /// <summary>A lesson that stays where it is.</summary>
    public record Kept(int ClassId, int SubjectId, int? TeacherId, int? ClassroomId, DayOfWeek Day, int Period);

    /// <param name="TeacherDays">Days a teacher can teach; missing = any.</param>
    public record Input(
        IReadOnlyList<DayOfWeek> Days,
        IReadOnlyList<int> Periods,
        IReadOnlyList<Demand> Demands,
        IReadOnlyList<Kept> KeptLessons,
        IReadOnlyDictionary<int, IReadOnlyCollection<DayOfWeek>> TeacherDays,
        TimeSpan TimeLimit);

    public record Placement(
        int ClassId, int SubjectId, int? TeacherId, DayOfWeek Day, int Period, WeekParity Parity, int? ClassroomId);

    /// <param name="Hours">Hours a week left out.</param>
    public record Unplaced(int ClassId, int SubjectId, int? TeacherId, decimal Hours);

    /// <param name="Solved">False when the solver found nothing in time.</param>
    public record Result(bool Solved, IReadOnlyList<Placement> Placements, IReadOnlyList<Unplaced> Unplaced);

    private record Unit(Demand Demand, WeekParity Parity, int Count);

    public static Result Generate(Input input)
    {
        var days = input.Days;
        var periods = input.Periods.Order().ToList();
        var slots = (from d in days from p in periods select (Day: d, Period: p)).ToList();
        var units = input.Demands
            .SelectMany(d => new[] { new Unit(d, WeekParity.Every, d.Weekly), new Unit(d, WeekParity.Odd, d.Biweekly) })
            .Where(u => u.Count > 0)
            .ToList();
        if (units.Count == 0 || slots.Count == 0)
        {
            return new Result(true, [], Missing(units, _ => 0));
        }

        var kept = input.KeptLessons;
        var classBusy = kept.Select(k => (k.ClassId, k.Day, k.Period)).ToHashSet();
        var teacherBusy = kept.Where(k => k.TeacherId is not null).Select(k => (k.TeacherId!.Value, k.Day, k.Period)).ToHashSet();
        var roomBusy = kept.Where(k => k.ClassroomId is not null).Select(k => (k.ClassroomId!.Value, k.Day, k.Period)).ToHashSet();

        var model = new CpModel();
        var objective = LinearExpr.NewBuilder();
        var x = new Dictionary<(int Unit, DayOfWeek Day, int Period), BoolVar>();
        var missing = new IntVar[units.Count];
        for (var u = 0; u < units.Count; u++)
        {
            var demand = units[u].Demand;
            var teacherDays = demand.TeacherId is { } t && input.TeacherDays.TryGetValue(t, out var allowed) ? allowed : null;
            var sum = LinearExpr.NewBuilder();
            foreach (var (day, period) in slots)
            {
                if ((teacherDays is not null && !teacherDays.Contains(day))
                    || classBusy.Contains((demand.ClassId, day, period))
                    || (demand.TeacherId is { } teacher && teacherBusy.Contains((teacher, day, period))))
                {
                    continue;
                }
                var v = model.NewBoolVar($"x{u}_{day}_{period}");
                x[(u, day, period)] = v;
                sum.Add(v);
                if (period < 1)
                {
                    objective.AddTerm(v, EarlyPeriodWeight);
                }
            }
            missing[u] = model.NewIntVar(0, units[u].Count, $"missing{u}");
            sum.Add(missing[u]);
            model.Add(sum == units[u].Count);
            objective.AddTerm(missing[u], MissingWeight);
        }

        LinearExpr Occupied(IEnumerable<int> unitIndexes, DayOfWeek day, int period, int fixedCount)
        {
            var b = LinearExpr.NewBuilder();
            foreach (var u in unitIndexes)
            {
                if (x.TryGetValue((u, day, period), out var v))
                {
                    b.Add(v);
                }
            }
            b.Add(fixedCount);
            return b;
        }

        var byClass = Group(units, u => u.Demand.ClassId);
        var byTeacher = Group(units.Select((u, i) => (u, i)).Where(p => p.u.Demand.TeacherId is not null).Select(p => p.i),
            i => units[i].Demand.TeacherId!.Value);

        // A class at most once at a time, gaps and the first lesson.
        foreach (var (classId, unitIndexes) in byClass)
        {
            var keptCount = kept.Count(k => k.ClassId == classId);
            var weekly = units.Where(u => u.Demand.ClassId == classId).Sum(u => u.Count) + keptCount;
            var perDay = (int)Math.Ceiling(weekly / (double)days.Count) + 1;
            var minPerDay = weekly / days.Count;
            foreach (var day in days)
            {
                var daySum = LinearExpr.NewBuilder();
                LinearExpr? previous = null;
                foreach (var period in periods)
                {
                    var busy = classBusy.Contains((classId, day, period)) ? 1 : 0;
                    var occupied = Occupied(unitIndexes, day, period, busy);
                    model.Add(occupied <= 1);
                    daySum.Add(occupied);
                    if (period >= 1 && period != periods.First(p => p >= 1))
                    {
                        // A lesson starting a new block after a free period.
                        var start = model.NewBoolVar($"cs{classId}_{day}_{period}");
                        model.Add(start >= occupied - (previous ?? LinearExpr.Constant(0)));
                        objective.AddTerm(start, ClassGapWeight);
                    }
                    previous = occupied;
                }
                model.Add(daySum <= perDay);
                var shortfall = model.NewIntVar(0, minPerDay, $"short{classId}_{day}");
                model.Add(shortfall >= minPerDay - daySum);
                objective.AddTerm(shortfall, ShortDayWeight);
            }

            // A subject at most once a day (or as evenly as its hours allow).
            foreach (var subjectUnits in unitIndexes.GroupBy(u => units[u].Demand.SubjectId))
            {
                var subjectId = subjectUnits.Key;
                var count = subjectUnits.Sum(u => units[u].Count)
                    + kept.Count(k => k.ClassId == classId && k.SubjectId == subjectId);
                var allowed = (int)Math.Ceiling(count / (double)days.Count);
                foreach (var day in days)
                {
                    var daySum = LinearExpr.NewBuilder();
                    foreach (var period in periods)
                    {
                        daySum.Add(Occupied(subjectUnits, day, period,
                            kept.Count(k => k.ClassId == classId && k.SubjectId == subjectId && k.Day == day && k.Period == period)));
                    }
                    var excess = model.NewIntVar(0, periods.Count, $"twice{classId}_{subjectId}_{day}");
                    model.Add(excess >= daySum - allowed);
                    objective.AddTerm(excess, SubjectTwiceWeight);
                }
            }
        }

        // A teacher at most once at a time, and few blocks a day.
        foreach (var (teacherId, unitIndexes) in byTeacher)
        {
            foreach (var day in days)
            {
                LinearExpr? previous = null;
                foreach (var period in periods)
                {
                    var busy = teacherBusy.Contains((teacherId, day, period)) ? 1 : 0;
                    var occupied = Occupied(unitIndexes, day, period, busy);
                    model.Add(occupied <= 1);
                    var start = model.NewBoolVar($"ts{teacherId}_{day}_{period}");
                    model.Add(start >= occupied - (previous ?? LinearExpr.Constant(0)));
                    objective.AddTerm(start, TeacherGapWeight);
                    previous = occupied;
                }
            }
        }

        // A free classroom for every lesson: for each set of classrooms, the
        // lessons that can only use those at most as many as are free.
        var roomSets = units.Select(u => u.Demand.Rooms).Where(r => r.Count > 0)
            .Select(r => r.Order().ToArray()).ToList();
        var pools = roomSets.Concat(Components(roomSets))
            .DistinctBy(r => string.Join(',', r)).ToList();
        foreach (var pool in pools)
        {
            var inPool = units.Select((u, i) => (u, i))
                .Where(p => p.u.Demand.Rooms.Count > 0 && p.u.Demand.Rooms.All(pool.Contains))
                .Select(p => p.i).ToList();
            if (inPool.Sum(i => units[i].Count) <= 0)
            {
                continue;
            }
            foreach (var (day, period) in slots)
            {
                var free = pool.Count(r => !roomBusy.Contains((r, day, period)));
                if (free >= inPool.Count(i => x.ContainsKey((i, day, period))))
                {
                    continue;
                }
                var over = model.NewIntVar(0, inPool.Count, $"noroom{string.Join('_', pool)}_{day}_{period}");
                model.Add(over >= Occupied(inPool, day, period, -free));
                objective.AddTerm(over, NoRoomWeight);
            }
        }

        model.Minimize(objective);
        var solver = new CpSolver
        {
            StringParameters = $"max_time_in_seconds:{input.TimeLimit.TotalSeconds:0.#} num_workers:4",
        };
        var status = solver.Solve(model);
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
        {
            return new Result(false, [], Missing(units, _ => 0));
        }

        var placed = x.Where(p => solver.BooleanValue(p.Value)).Select(p => p.Key).ToList();
        return new Result(true, AssignRooms(placed, units, input, roomBusy),
            Missing(units, u => units[u].Count - (int)solver.Value(missing[u])));
    }

    private static Dictionary<int, List<int>> Group(IEnumerable<Unit> units, Func<Unit, int> key) =>
        units.Select((u, i) => (Key: key(u), i)).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToList());

    private static Dictionary<int, List<int>> Group(IEnumerable<int> unitIndexes, Func<int, int> key) =>
        unitIndexes.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());

    private static List<Unplaced> Missing(List<Unit> units, Func<int, int> placedCount) =>
        units.Select((u, i) => (u, Left: u.Count - placedCount(i)))
            .Where(p => p.Left > 0)
            .GroupBy(p => p.u.Demand)
            .Select(g => new Unplaced(g.Key.ClassId, g.Key.SubjectId, g.Key.TeacherId,
                g.Sum(p => p.u.Parity == WeekParity.Every ? p.Left : p.Left * 0.5m)))
            .ToList();

    /// <summary>Overlapping classroom sets merged.</summary>
    private static List<int[]> Components(List<int[]> sets)
    {
        var merged = new List<HashSet<int>>();
        foreach (var set in sets)
        {
            var joined = new HashSet<int>(set);
            foreach (var other in merged.Where(m => m.Overlaps(set)).ToList())
            {
                joined.UnionWith(other);
                merged.Remove(other);
            }
            merged.Add(joined);
        }
        return [.. merged.Select(m => m.Order().ToArray())];
    }

    /// <summary>A class keeps one classroom for a subject where it can — the
    /// one free for most of its lessons, then the one it already has, then
    /// the first choice — and only the lessons that clash go elsewhere, to
    /// the first free one; none free = no classroom.</summary>
    private static List<Placement> AssignRooms(
        List<(int Unit, DayOfWeek Day, int Period)> placed, List<Unit> units, Input input,
        HashSet<(int, DayOfWeek, int)> roomBusy)
    {
        var taken = new HashSet<(int, DayOfWeek, int)>(roomBusy);
        var result = new List<Placement>();
        var groups = placed
            .GroupBy(p => (units[p.Unit].Demand.ClassId, units[p.Unit].Demand.SubjectId))
            .OrderBy(g => units[g.First().Unit].Demand.Rooms.Count is var n && n == 0 ? int.MaxValue : n)
            .ThenByDescending(g => g.Count());
        foreach (var lessons in groups)
        {
            var demand = units[lessons.First().Unit].Demand;
            var rooms = demand.Rooms;
            var has = input.KeptLessons.Where(k => k.ClassId == demand.ClassId && k.SubjectId == demand.SubjectId)
                .Select(k => k.ClassroomId).ToList();
            int? home = rooms.Count == 0 ? null : rooms
                .Select((r, i) => (r, i))
                .OrderByDescending(p => lessons.Count(l => !taken.Contains((p.r, l.Day, l.Period))))
                .ThenByDescending(p => has.Count(k => k == p.r))
                .ThenBy(p => p.i)
                .First().r;
            foreach (var (u, day, period) in lessons)
            {
                var roomId = home is { } h && !taken.Contains((h, day, period))
                    ? h
                    : rooms.Where(r => !taken.Contains((r, day, period))).Select(r => (int?)r).FirstOrDefault();
                if (roomId is { } id)
                {
                    taken.Add((id, day, period));
                }
                result.Add(new Placement(demand.ClassId, demand.SubjectId, demand.TeacherId, day, period, units[u].Parity, roomId));
            }
        }
        return result;
    }
}
