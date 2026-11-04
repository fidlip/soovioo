using Google.OrTools.Sat;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Places lessons into the week with OR-Tools CP-SAT. Hard: a class
/// and a teacher at most once at a time, never when the teacher can't, a
/// block of two lessons in a row, at most a balanced number of lessons a day
/// for a class. Required, but reported rather than forced when impossible
/// (weighted): every lesson placed, a free classroom for each, lessons every
/// day, no gaps for classes (a day starts with the first lesson), a subject
/// at most once a day, no 0. hodina; then the teachers' wishes (raději ne,
/// free days, lessons and gaps a day). The search stops once those all hold
/// (or can't get better), then a short second one cuts teachers' gaps
/// without giving any of them up.</summary>
public static class TimetableGenerator
{
    private const int MissingWeight = 1000;
    private const int NoRoomWeight = 200;
    private const int LateStartWeight = 30;
    private const int ClassGapWeight = 10;
    private const int ShortDayWeight = 20;
    private const int EarlyPeriodWeight = 8;
    private const int SubjectTwiceWeight = 6;
    private const int TeacherGapWeight = 2;
    private const int FreeDayWeight = 12;
    private const int TooManyWeight = 6;
    private const int TooFewWeight = 4;
    private const int TooManyGapsWeight = 4;
    private const int AvoidWeight = 3;

    /// <param name="Weekly">Lessons every week.</param>
    /// <param name="Biweekly">Lessons every other week (0.5 h); placed in
    /// odd weeks and holding the slot in both.</param>
    /// <param name="Rooms">Classrooms it can have, first choice first; empty
    /// = none needed.</param>
    /// <param name="DoubleLessons">The weekly lessons in blocks of two in a
    /// row, an odd one left on its own.</param>
    public record Demand(
        int ClassId, int SubjectId, int? TeacherId, int Weekly, int Biweekly, IReadOnlyList<int>? Rooms = null,
        bool DoubleLessons = false)
    {
        public IReadOnlyList<int> Rooms { get; init; } = Rooms ?? [];
    }

    /// <summary>When a teacher can teach and what they wish for.</summary>
    public record Teacher(IReadOnlyList<AvailabilitySlot> Availability, int? FreeDays = null, TeacherWishes? Wishes = null);

    /// <summary>A lesson that stays where it is.</summary>
    public record Kept(int ClassId, int SubjectId, int? TeacherId, int? ClassroomId, DayOfWeek Day, int Period);

    /// <param name="Teachers">Missing = free any time, no wishes.</param>
    public record Input(
        IReadOnlyList<DayOfWeek> Days,
        IReadOnlyList<int> Periods,
        IReadOnlyList<Demand> Demands,
        IReadOnlyList<Kept> KeptLessons,
        IReadOnlyDictionary<int, Teacher> Teachers,
        TimeSpan TimeLimit)
    {
        /// <summary>The second search stops when it hasn't improved for this
        /// long, or after <see cref="PolishLimit"/>.</summary>
        public TimeSpan PolishStall { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan PolishLimit { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Gets the latest solution's figures as the search goes —
        /// from the solver's threads, at most about once a second.</summary>
        public Action<Progress>? OnProgress { get; init; }
    }

    public enum Phase
    {
        /// <summary>Looking for a timetable that meets the requirements.</summary>
        Requirements,

        /// <summary>Cutting teachers' gaps.</summary>
        Polish,
    }

    /// <summary>The best timetable so far. <paramref name="Lessons"/> out of
    /// <paramref name="Total"/> placed (the kept ones included).</summary>
    /// <param name="PolishShare">0–1 of the second search's time — it ends
    /// at its limit, or earlier when it stalls.</param>
    public record Progress(
        Phase Phase, int Lessons, int Total, int WithoutRoom, int ClassGaps, int LateStarts, int TeacherGaps,
        double PolishShare = 0);

    public record Placement(
        int ClassId, int SubjectId, int? TeacherId, DayOfWeek Day, int Period, WeekParity Parity, int? ClassroomId);

    /// <param name="Hours">Hours a week left out.</param>
    public record Unplaced(int ClassId, int SubjectId, int? TeacherId, decimal Hours);

    /// <param name="Solved">False when the solver found nothing in time.</param>
    /// <param name="TimedOut">The time limit ran out before the requirements
    /// held or were shown impossible to meet better.</param>
    /// <param name="Penalty">What the requirements' breaches weigh,
    /// <paramref name="Bound"/> the least possible as far as the solver got.</param>
    public record Result(
        bool Solved, IReadOnlyList<Placement> Placements, IReadOnlyList<Unplaced> Unplaced,
        bool TimedOut = false, double Penalty = 0, double Bound = 0);

    /// <param name="Length">Lessons in a row: 2 for a block.</param>
    private record Unit(Demand Demand, WeekParity Parity, int Count, int Length = 1);

    public static Result Generate(Input input)
    {
        var days = input.Days;
        var periods = input.Periods.Order().ToList();
        var slots = (from d in days from p in periods select (Day: d, Period: p)).ToList();
        var units = input.Demands
            .SelectMany(d => d.DoubleLessons
                ? new[] { new Unit(d, WeekParity.Every, d.Weekly / 2, 2), new Unit(d, WeekParity.Every, d.Weekly % 2), new Unit(d, WeekParity.Odd, d.Biweekly) }
                : [new Unit(d, WeekParity.Every, d.Weekly), new Unit(d, WeekParity.Odd, d.Biweekly)])
            .Where(u => u.Count > 0)
            .ToList();
        if (units.Count == 0 || slots.Count == 0)
        {
            return new Result(true, [], Missing(units, _ => 0));
        }

        // A block takes the period and the one right after it.
        var next = periods.Zip(periods.Skip(1)).Where(p => p.Second == p.First + 1).ToDictionary(p => p.First, p => p.Second);
        int[] Covered(Unit unit, int period) => unit.Length == 2 ? [period, next[period]] : [period];

        var kept = input.KeptLessons;
        var classBusy = kept.Select(k => (k.ClassId, k.Day, k.Period)).ToHashSet();
        var teacherBusy = kept.Where(k => k.TeacherId is not null).Select(k => (k.TeacherId!.Value, k.Day, k.Period)).ToHashSet();
        var roomBusy = kept.Where(k => k.ClassroomId is not null).Select(k => (k.ClassroomId!.Value, k.Day, k.Period)).ToHashSet();
        Teacher? TeacherOf(Demand d) => d.TeacherId is { } t ? input.Teachers.GetValueOrDefault(t) : null;

        var model = new CpModel();
        var overs = new List<IntVar>();
        var objective = LinearExpr.NewBuilder();
        var teacherGaps = LinearExpr.NewBuilder();
        // Lessons of a unit starting at a slot, and those taking it.
        var x = new Dictionary<(int Unit, DayOfWeek Day, int Period), BoolVar>();
        var cover = new Dictionary<(int Unit, DayOfWeek Day, int Period), List<BoolVar>>();
        var missing = new IntVar[units.Count];
        for (var u = 0; u < units.Count; u++)
        {
            var unit = units[u];
            var demand = unit.Demand;
            var availability = TeacherOf(demand)?.Availability ?? [];
            bool Free(DayOfWeek day, int period) =>
                AvailabilitySlot.At(availability, day, period) != AvailabilityKind.Unavailable
                && !classBusy.Contains((demand.ClassId, day, period))
                && !(demand.TeacherId is { } teacher && teacherBusy.Contains((teacher, day, period)));
            var sum = LinearExpr.NewBuilder();
            foreach (var (day, period) in slots)
            {
                if ((unit.Length == 2 && !next.ContainsKey(period)) || !Covered(unit, period).All(p => Free(day, p)))
                {
                    continue;
                }
                var v = model.NewBoolVar($"x{u}_{day}_{period}");
                x[(u, day, period)] = v;
                sum.Add(v);
                foreach (var p in Covered(unit, period))
                {
                    (cover.TryGetValue((u, day, p), out var list) ? list : cover[(u, day, p)] = []).Add(v);
                    if (p < 1)
                    {
                        objective.AddTerm(v, EarlyPeriodWeight);
                    }
                    if (AvailabilitySlot.At(availability, day, p) == AvailabilityKind.Avoid)
                    {
                        objective.AddTerm(v, AvoidWeight);
                    }
                }
            }
            missing[u] = model.NewIntVar(0, unit.Count, $"missing{u}");
            sum.Add(missing[u]);
            model.Add(sum == unit.Count);
            objective.AddTerm(missing[u], MissingWeight * unit.Length);
        }

        LinearExpr Occupied(IEnumerable<int> unitIndexes, DayOfWeek day, int period, int fixedCount)
        {
            var b = LinearExpr.NewBuilder();
            foreach (var u in unitIndexes)
            {
                if (cover.TryGetValue((u, day, period), out var vs))
                {
                    foreach (var v in vs)
                    {
                        b.Add(v);
                    }
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
            var weekly = units.Where(u => u.Demand.ClassId == classId).Sum(u => u.Count * u.Length) + keptCount;
            var perDay = (int)Math.Ceiling(weekly / (double)days.Count) + 1;
            var minPerDay = weekly / days.Count;
            foreach (var day in days)
            {
                var daySum = LinearExpr.NewBuilder();
                var earlier = new List<LinearExpr>();
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
                        model.Add(start >= occupied - earlier[^1]);
                        objective.AddTerm(start, ClassGapWeight);
                        // ...or the day's first lesson, later than the first period.
                        var late = model.NewBoolVar($"late{classId}_{day}_{period}");
                        model.Add(late >= occupied - LinearExpr.Sum(earlier));
                        objective.AddTerm(late, LateStartWeight);
                    }
                    earlier.Add(occupied);
                }
                model.Add(daySum <= perDay);
                var shortfall = model.NewIntVar(0, minPerDay, $"short{classId}_{day}");
                model.Add(shortfall >= minPerDay - daySum);
                objective.AddTerm(shortfall, ShortDayWeight);
            }

            // A subject at most once a day (or as evenly as its hours allow),
            // a block counting once.
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
                        foreach (var u in subjectUnits)
                        {
                            if (x.TryGetValue((u, day, period), out var v))
                            {
                                daySum.Add(v);
                            }
                        }
                        daySum.Add(kept.Count(k => k.ClassId == classId && k.SubjectId == subjectId && k.Day == day && k.Period == period));
                    }
                    var excess = model.NewIntVar(0, periods.Count, $"twice{classId}_{subjectId}_{day}");
                    model.Add(excess >= daySum - allowed);
                    objective.AddTerm(excess, SubjectTwiceWeight);
                }
            }
        }

        // A teacher at most once at a time, few blocks a day and the wishes.
        foreach (var (teacherId, unitIndexes) in byTeacher)
        {
            var teacher = input.Teachers.GetValueOrDefault(teacherId);
            var wishes = teacher?.Wishes ?? new TeacherWishes();
            var teaches = new List<BoolVar>();
            foreach (var day in days)
            {
                LinearExpr? previous = null;
                var occupied = new List<LinearExpr>();
                foreach (var period in periods)
                {
                    var busy = teacherBusy.Contains((teacherId, day, period)) ? 1 : 0;
                    var now = Occupied(unitIndexes, day, period, busy);
                    model.Add(now <= 1);
                    var start = model.NewBoolVar($"ts{teacherId}_{day}_{period}");
                    model.Add(start >= now - (previous ?? LinearExpr.Constant(0)));
                    teacherGaps.AddTerm(start, TeacherGapWeight);
                    previous = now;
                    occupied.Add(now);
                }
                var daySum = LinearExpr.Sum(occupied);
                if (teacher?.FreeDays is > 0 || wishes.MinLessonsPerDay is not null)
                {
                    var on = model.NewBoolVar($"on{teacherId}_{day}");
                    foreach (var now in occupied)
                    {
                        model.Add(on >= now);
                    }
                    teaches.Add(on);
                    if (wishes.MinLessonsPerDay is { } min)
                    {
                        var few = model.NewIntVar(0, min, $"few{teacherId}_{day}");
                        model.Add(few >= min * on - daySum);
                        objective.AddTerm(few, TooFewWeight);
                    }
                }
                if (wishes.MaxLessonsPerDay is { } max)
                {
                    var many = model.NewIntVar(0, periods.Count, $"many{teacherId}_{day}");
                    model.Add(many >= daySum - max);
                    objective.AddTerm(many, TooManyWeight);
                }
                if (wishes.MaxGapsPerDay is { } maxGaps)
                {
                    // A free period with a lesson before and after it.
                    var holes = LinearExpr.NewBuilder();
                    var before = new BoolVar[periods.Count];
                    var after = new BoolVar[periods.Count];
                    for (var i = 0; i < periods.Count; i++)
                    {
                        before[i] = model.NewBoolVar($"tb{teacherId}_{day}_{i}");
                        if (i > 0)
                        {
                            model.Add(before[i] >= before[i - 1]);
                            model.Add(before[i] >= occupied[i - 1]);
                        }
                    }
                    for (var i = periods.Count - 1; i >= 0; i--)
                    {
                        after[i] = model.NewBoolVar($"ta{teacherId}_{day}_{i}");
                        if (i < periods.Count - 1)
                        {
                            model.Add(after[i] >= after[i + 1]);
                            model.Add(after[i] >= occupied[i + 1]);
                        }
                    }
                    for (var i = 1; i < periods.Count - 1; i++)
                    {
                        var hole = model.NewBoolVar($"th{teacherId}_{day}_{i}");
                        model.Add(hole >= before[i] + after[i] - 1 - occupied[i]);
                        holes.Add(hole);
                    }
                    var gaps = model.NewIntVar(0, periods.Count, $"tg{teacherId}_{day}");
                    model.Add(gaps >= holes - maxGaps);
                    objective.AddTerm(gaps, TooManyGapsWeight);
                }
            }
            if (teacher?.FreeDays is { } freeDays and > 0)
            {
                var lacking = model.NewIntVar(0, freeDays, $"free{teacherId}");
                model.Add(lacking >= freeDays - days.Count + LinearExpr.Sum(teaches));
                objective.AddTerm(lacking, FreeDayWeight);
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
                if (free >= inPool.Sum(i => cover.TryGetValue((i, day, period), out var vs) ? vs.Count : 0))
                {
                    continue;
                }
                var over = model.NewIntVar(0, inPool.Count, $"noroom{string.Join('_', pool)}_{day}_{period}");
                overs.Add(over);
                model.Add(over >= Occupied(inPool, day, period, -free));
                objective.AddTerm(over, NoRoomWeight);
            }
        }

        var total = units.Sum(u => u.Count * u.Length) + kept.Count;
        var first = periods.Where(p => p >= 1).DefaultIfEmpty(periods[0]).Min();
        Progress Measure(CpSolverSolutionCallback at, Phase phase)
        {
            var lessons = x.Where(p => at.BooleanValue(p.Value))
                .SelectMany(p => Covered(units[p.Key.Unit], p.Key.Period)
                    .Select(period => (units[p.Key.Unit].Demand.ClassId, units[p.Key.Unit].Demand.TeacherId, p.Key.Day, Period: period)))
                .Concat(kept.Select(k => (k.ClassId, k.TeacherId, k.Day, k.Period)))
                .ToList();
            var classDays = lessons.GroupBy(l => (l.ClassId, l.Day)).ToList();
            return new Progress(
                phase, lessons.Count, total, (int)overs.Sum(at.Value),
                classDays.Sum(d => Holes(d.Select(l => l.Period))),
                classDays.Count(d => d.Min(l => l.Period) > first),
                lessons.Where(l => l.TeacherId is not null).GroupBy(l => (l.TeacherId, l.Day)).Sum(d => Holes(d.Select(l => l.Period))));
        }

        // The requirements: until they hold, or can't get any better.
        model.Minimize(objective);
        var solver = new CpSolver { StringParameters = Parameters(input.TimeLimit) };
        var status = solver.Solve(model, new Requirements(at => Measure(at, Phase.Requirements), input.OnProgress));
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
        {
            return new Result(false, [], Missing(units, _ => 0), TimedOut: status == CpSolverStatus.Unknown);
        }
        var penalty = solver.ObjectiveValue;
        var bound = solver.BestObjectiveBound;
        var x1 = x.ToDictionary(p => p.Key, p => solver.BooleanValue(p.Value));
        var missing1 = missing.Select(m => solver.Value(m)).ToArray();

        // Then teachers' gaps, keeping the requirements as they are.
        model.Add(objective <= (long)penalty);
        foreach (var (key, v) in x)
        {
            model.AddHint(v, x1[key]);
        }
        model.Minimize(teacherGaps);
        var polish = new CpSolver { StringParameters = Parameters(input.PolishLimit) };
        using (var stall = new Polish(polish, input, at => Measure(at, Phase.Polish)))
        {
            if (polish.Solve(model, stall) is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
            {
                x1 = x.ToDictionary(p => p.Key, p => polish.BooleanValue(p.Value));
                missing1 = missing.Select(m => polish.Value(m)).ToArray();
            }
        }

        var placed = x1.Where(p => p.Value).Select(p => (p.Key.Unit, p.Key.Day, Covered(units[p.Key.Unit], p.Key.Period))).ToList();
        return new Result(true, AssignRooms(placed, units, input, roomBusy),
            Missing(units, u => units[u].Count - (int)missing1[u]),
            penalty > 0 && penalty > bound, penalty, bound);
    }

    /// <summary>Free periods between a day's first and last lesson.</summary>
    public static int Holes(IEnumerable<int> periods)
    {
        var taken = periods.Distinct().Order().ToList();
        return taken.Count == 0 ? 0 : taken[^1] - taken[0] + 1 - taken.Count;
    }

    private static string Parameters(TimeSpan limit) =>
        $"max_time_in_seconds:{limit.TotalSeconds:0.#} num_workers:{Workers}";

    /// <summary>Reports a solution at most once a second.</summary>
    private abstract class Reporting(Func<CpSolverSolutionCallback, Progress> measure, Action<Progress>? report)
        : CpSolverSolutionCallback
    {
        private long reportedAt;
        protected Progress? Last { get; private set; }

        protected void Report(bool force = false)
        {
            if (report is null || (!force && Environment.TickCount64 - reportedAt < 1000))
            {
                return;
            }
            reportedAt = Environment.TickCount64;
            Last = measure(this);
            report(Last);
        }
    }

    /// <summary>Nothing better than no breach at all.</summary>
    private sealed class Requirements(Func<CpSolverSolutionCallback, Progress> measure, Action<Progress>? report)
        : Reporting(measure, report)
    {
        public override void OnSolutionCallback()
        {
            var done = ObjectiveValue() <= 0;
            Report(force: done);
            if (done)
            {
                StopSearch();
            }
        }
    }

    /// <summary>Stops once it hasn't found anything better for a while;
    /// reports the share of its time used every second.</summary>
    private sealed class Polish : Reporting, IDisposable
    {
        private readonly Timer timer;
        private readonly long startedAt = Environment.TickCount64;
        private long improvedAt = Environment.TickCount64;

        public Polish(CpSolver solver, Input input, Func<CpSolverSolutionCallback, Progress> measure)
            : base(measure, input.OnProgress)
        {
            timer = new Timer(_ =>
            {
                var now = Environment.TickCount64;
                var stalled = now - Interlocked.Read(ref improvedAt);
                if (Last is { } last)
                {
                    input.OnProgress?.Invoke(last with
                    {
                        PolishShare = Math.Min(1, Math.Max(
                            (now - startedAt) / input.PolishLimit.TotalMilliseconds,
                            stalled / input.PolishStall.TotalMilliseconds)),
                    });
                }
                if (stalled > input.PolishStall.TotalMilliseconds)
                {
                    solver.StopSearch();
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public override void OnSolutionCallback()
        {
            Interlocked.Exchange(ref improvedAt, Environment.TickCount64);
            Report(force: true);
        }

        public new void Dispose()
        {
            timer.Dispose();
            base.Dispose();
        }
    }

    /// <summary>CP-SAT runs differently set up searches side by side, one per
    /// core. At least 8 even on fewer cores (they take turns): a single one
    /// finds no timetable within the limit at all.</summary>
    private static int Workers => Math.Clamp(Environment.ProcessorCount, 8, 16);

    private static Dictionary<int, List<int>> Group(IEnumerable<Unit> units, Func<Unit, int> key) =>
        units.Select((u, i) => (Key: key(u), i)).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToList());

    private static Dictionary<int, List<int>> Group(IEnumerable<int> unitIndexes, Func<int, int> key) =>
        unitIndexes.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());

    private static List<Unplaced> Missing(List<Unit> units, Func<int, int> placedCount) =>
        units.Select((u, i) => (u, Left: u.Count - placedCount(i)))
            .Where(p => p.Left > 0)
            .GroupBy(p => p.u.Demand)
            .Select(g => new Unplaced(g.Key.ClassId, g.Key.SubjectId, g.Key.TeacherId,
                g.Sum(p => p.u.Parity == WeekParity.Every ? p.Left * p.u.Length : p.Left * 0.5m)))
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
    /// the first free one; none free = no classroom. A block stays in one.</summary>
    private static List<Placement> AssignRooms(
        List<(int Unit, DayOfWeek Day, int[] Periods)> placed, List<Unit> units, Input input,
        HashSet<(int, DayOfWeek, int)> roomBusy)
    {
        var taken = new HashSet<(int, DayOfWeek, int)>(roomBusy);
        bool Free(int room, (int Unit, DayOfWeek Day, int[] Periods) l) => l.Periods.All(p => !taken.Contains((room, l.Day, p)));
        var result = new List<Placement>();
        var groups = placed
            .GroupBy(p => (units[p.Unit].Demand.ClassId, units[p.Unit].Demand.SubjectId))
            .OrderBy(g => units[g.First().Unit].Demand.Rooms.Count is var n && n == 0 ? int.MaxValue : n)
            .ThenByDescending(g => g.Sum(l => l.Periods.Length));
        foreach (var lessons in groups)
        {
            var demand = units[lessons.First().Unit].Demand;
            var rooms = demand.Rooms;
            var has = input.KeptLessons.Where(k => k.ClassId == demand.ClassId && k.SubjectId == demand.SubjectId)
                .Select(k => k.ClassroomId).ToList();
            int? home = rooms.Count == 0 ? null : rooms
                .Select((r, i) => (r, i))
                .OrderByDescending(p => lessons.Count(l => Free(p.r, l)))
                .ThenByDescending(p => has.Count(k => k == p.r))
                .ThenBy(p => p.i)
                .First().r;
            foreach (var lesson in lessons)
            {
                var roomId = home is { } h && Free(h, lesson)
                    ? h
                    : rooms.Where(r => Free(r, lesson)).Select(r => (int?)r).FirstOrDefault();
                foreach (var period in lesson.Periods)
                {
                    if (roomId is { } id)
                    {
                        taken.Add((id, lesson.Day, period));
                    }
                    result.Add(new Placement(
                        demand.ClassId, demand.SubjectId, demand.TeacherId, lesson.Day, period, units[lesson.Unit].Parity, roomId));
                }
            }
        }
        return result;
    }
}
