using Google.OrTools.Sat;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Places lessons into the week with OR-Tools CP-SAT. Hard: a class
/// and a teacher at most once at a time, never when the teacher can't, a
/// block of two lessons in a row, at most a balanced number of lessons a day
/// for a class. Required, but reported rather than forced when impossible
/// (weighted): every lesson placed, a free classroom for each, lessons every
/// day, the class's main block (<see cref="MainBlock"/>) full of lessons
/// for all its pupils, no gaps for classes, a subject at most once a day,
/// no 0. hodina, elective lessons outside the lessons for everyone; then
/// the teachers' wishes (raději ne, free days, lessons and gaps a day); then
/// no longer days than the class's hours need. The search stops once those all hold
/// (or can't get better), then a short second one cuts teachers' gaps
/// without giving any of them up. A group's lesson takes all its classes;
/// the groups of a division are placed together, each with its teacher and
/// classroom.</summary>
public static class TimetableGenerator
{
    private const int MissingWeight = 1000;
    private const int NoRoomWeight = 200;

    /// <summary>A period of a class's <see cref="Timetable.MainBlock"/>
    /// without a lesson for all its pupils: above every wish and gap, below
    /// a lesson without a classroom.</summary>
    private const int MainBlockWeight = 100;
    private const int ClassGapWeight = 10;

    /// <summary>The pupils not in an elective lesson have a gap: per class,
    /// as much as the class's own.</summary>
    private const int ElectiveInsideWeight = ClassGapWeight;
    private const int ShortDayWeight = 20;
    private const int EarlyPeriodWeight = 8;
    private const int SubjectTwiceWeight = 6;
    private const int TeacherGapWeight = 2;
    private const int FreeDayWeight = 12;
    private const int TooManyWeight = 6;
    private const int TooFewWeight = 4;
    private const int TooManyGapsWeight = 4;
    private const int AvoidWeight = 3;

    /// <summary>A lesson leaving where it is costs less than the weakest
    /// teacher's wish, so a wish is worth moving one lesson, not two.</summary>
    private const int MoveWeight = 2;

    /// <summary>What a class's lesson in a period weighs: from the 5th period
    /// on (around noon) every next one costs more. Moving a lesson to a longer
    /// day costs less than the weakest teacher's wish, so a wish goes first.</summary>
    public static int LongDayWeight(int period) => period < 5 ? 0 : (period - 5) * 2 + 1;

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

        /// <summary>Classes it takes: of a group all of the group's — none of
        /// them has another lesson then.</summary>
        public IReadOnlyList<int> ClassIds { get; init; } = [ClassId];

        /// <summary>What's taught at once: one part, or a group each of a
        /// division, each with its teacher and classroom.</summary>
        public IReadOnlyList<Part> Parts { get; init; } = [new(null, TeacherId, Rooms ?? [])];

        /// <summary>At most once a day per class: the subject, a group or a division.</summary>
        public int Key { get; init; } = SubjectId;

        /// <summary>Where its lessons are now: leaving costs, new ones go
        /// anywhere. One per part at a time.</summary>
        public IReadOnlyList<Home> Homes { get; init; } = [];

        /// <summary>Classes not all of whose pupils have it: it belongs
        /// before or after the lessons all of them have.</summary>
        public IReadOnlyList<int> ElectiveClasses { get; init; } = [];

        public IEnumerable<int> TeacherIds => Parts.Where(p => p.TeacherId is not null).Select(p => p.TeacherId!.Value).Distinct();
    }

    /// <param name="GroupId">Null = the whole class.</param>
    /// <param name="SubjectId">Null = the demand's.</param>
    public record Part(int? GroupId, int? TeacherId, IReadOnlyList<int> Rooms, int? SubjectId = null);

    /// <summary>A weekly lesson as it is now, see <see cref="Demand.Homes"/>.</summary>
    public record Home(DayOfWeek Day, int Period, int? GroupId = null, int? ClassroomId = null);

    /// <summary>When a teacher can teach and what they wish for.</summary>
    public record Teacher(IReadOnlyList<AvailabilitySlot> Availability, int? FreeDays = null, TeacherWishes? Wishes = null);

    /// <summary>A lesson that stays where it is; of a group one per class.</summary>
    /// <param name="Key">As <see cref="Demand.Key"/>; null = the subject.</param>
    /// <param name="Elective">Not all pupils of the class have it, see <see cref="Demand.ElectiveClasses"/>.</param>
    public record Kept(
        int ClassId, int SubjectId, int? TeacherId, int? ClassroomId, DayOfWeek Day, int Period, int? Key = null,
        int? GroupId = null, bool Elective = false);

    /// <param name="Teachers">Missing = free any time, no wishes.</param>
    public record Input(
        IReadOnlyList<DayOfWeek> Days,
        IReadOnlyList<int> Periods,
        IReadOnlyList<Demand> Demands,
        IReadOnlyList<Kept> KeptLessons,
        IReadOnlyDictionary<int, Teacher> Teachers,
        TimeSpan TimeLimit)
    {
        /// <summary>The first search stops when it hasn't improved for this
        /// long — when the requirements can't all hold, proving it can't get
        /// better can take far longer than the limit.</summary>
        public TimeSpan RequirementsStall { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>The second search stops when it hasn't improved for this
        /// long, or after <see cref="PolishLimit"/>.</summary>
        public TimeSpan PolishStall { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan PolishLimit { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>Gets the latest solution's figures as the search goes —
        /// from the solver's threads, at most about once a second.</summary>
        public Action<Progress>? OnProgress { get; init; }

        /// <summary>Each class's main block; missing = <see cref="Timetable.MainBlock.Default"/>.</summary>
        public IReadOnlyDictionary<int, MainBlock> MainBlocks { get; init; } = new Dictionary<int, MainBlock>();

        public MainBlock MainBlock(int classId) => MainBlocks.GetValueOrDefault(classId) ?? Timetable.MainBlock.Default;
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
        Phase Phase, int Lessons, int Total, int WithoutRoom, int ClassGaps, int MainBlockGaps, int TeacherGaps,
        double PolishShare = 0);

    /// <param name="GroupId">A group's lesson, for all its classes; <paramref name="ClassId"/> is the first.</param>
    public record Placement(
        int ClassId, int SubjectId, int? TeacherId, DayOfWeek Day, int Period, WeekParity Parity, int? ClassroomId,
        int? GroupId = null);

    /// <param name="Hours">Hours a week left out.</param>
    /// <param name="GroupId">Of a group or a division: its (first) group.</param>
    public record Unplaced(int ClassId, int SubjectId, int? TeacherId, decimal Hours, int? GroupId = null);

    /// <param name="Solved">False when the solver found nothing in time.</param>
    /// <param name="TimedOut">The time limit ran out before the requirements
    /// held, were shown impossible to meet better or stopped improving.</param>
    /// <param name="Penalty">What the requirements' breaches weigh,
    /// <paramref name="Bound"/> the least possible as far as the solver got.</param>
    public record Result(
        bool Solved, IReadOnlyList<Placement> Placements, IReadOnlyList<Unplaced> Unplaced,
        bool TimedOut = false, double Penalty = 0, double Bound = 0)
    {
        public IReadOnlyList<ShortBlock> ShortBlocks { get; init; } = [];
    }

    /// <summary>A class with fewer lessons for all its pupils than its main
    /// block's periods in the week.</summary>
    public record ShortBlock(int ClassId, int Lessons, int Needed);

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
            var availabilities = demand.TeacherIds
                .Select(t => input.Teachers.GetValueOrDefault(t)?.Availability ?? []).ToList();
            bool Free(DayOfWeek day, int period) =>
                availabilities.All(a => AvailabilitySlot.At(a, day, period) != AvailabilityKind.Unavailable)
                && demand.ClassIds.All(c => !classBusy.Contains((c, day, period)))
                && demand.TeacherIds.All(teacher => !teacherBusy.Contains((teacher, day, period)));
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
                    foreach (var availability in availabilities.Where(a => AvailabilitySlot.At(a, day, p) == AvailabilityKind.Avoid))
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

        // Leaving a lesson's place; the search starts from where they are.
        var homeHints = new List<BoolVar>();
        foreach (var demandUnits in units.Select((u, i) => (Unit: u, Index: i))
            .Where(p => p.Unit.Parity == WeekParity.Every && p.Unit.Demand.Homes.Count > 0).GroupBy(p => p.Unit.Demand))
        {
            var homes = demandUnits.Key.Homes.Select(h => (h.Day, h.Period)).ToHashSet();
            foreach (var (day, period) in homes)
            {
                objective.Add(MoveWeight);
                foreach (var (_, u) in demandUnits)
                {
                    foreach (var v in cover.GetValueOrDefault((u, day, period)) ?? [])
                    {
                        objective.AddTerm(v, -MoveWeight);
                    }
                }
            }
            foreach (var (unit, u) in demandUnits)
            {
                homeHints.AddRange(homes
                    .Where(h => x.ContainsKey((u, h.Day, h.Period)) && Covered(unit, h.Period).All(p => homes.Contains((h.Day, p))))
                    .Select(h => x[(u, h.Day, h.Period)]));
            }
        }
        foreach (var v in homeHints)
        {
            model.AddHint(v, true);
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

        var byClass = units.SelectMany((u, i) => u.Demand.ClassIds.Distinct().Select(c => (Key: c, i)))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToList());
        var byTeacher = units.SelectMany((u, i) => u.Demand.TeacherIds.Select(t => (Key: t, i)))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToList());
        // A group's kept lesson is one per class, each class once at a time.
        var keptSlots = kept.Select(k => (k.ClassId, Key: k.Key ?? k.SubjectId, k.Day, k.Period)).Distinct().ToList();

        var keptElective = kept.Where(k => k.Elective).Select(k => (k.ClassId, k.Day, k.Period)).ToHashSet();
        var keptForAll = kept.Where(k => !k.Elective).Select(k => (k.ClassId, k.Day, k.Period)).ToHashSet();

        // A class at most once at a time, gaps and the main block.
        var blockGaps = new List<IntVar>();
        var shortBlocks = new List<ShortBlock>();
        foreach (var (classId, unitIndexes) in byClass)
        {
            var keptCount = keptSlots.Where(k => k.ClassId == classId).Select(k => (k.Day, k.Period)).Distinct().Count();
            var weekly = unitIndexes.Sum(u => units[u].Count * units[u].Length) + keptCount;
            var perDay = (int)Math.Ceiling(weekly / (double)days.Count) + 1;
            var minPerDay = weekly / days.Count;
            objective.Add(-UnavoidableLongDays(weekly, days.Count, periods));
            var electiveUnits = unitIndexes.Where(u => units[u].Demand.ElectiveClasses.Contains(classId)).ToList();
            var forAllUnits = unitIndexes.Except(electiveUnits).ToList();
            var block = input.MainBlock(classId).Periods.Where(periods.Contains).ToList();
            var forAll = forAllUnits.Sum(u => units[u].Count * units[u].Length)
                + keptForAll.Count(k => k.ClassId == classId && block.Contains(k.Period));
            // What too few lessons leave empty is reported, not a breach to cut.
            if (forAll < block.Count * days.Count)
            {
                shortBlocks.Add(new ShortBlock(classId, forAll, block.Count * days.Count));
                objective.Add(-MainBlockWeight * (block.Count * days.Count - forAll));
            }
            foreach (var day in days)
            {
                var daySum = LinearExpr.NewBuilder();
                LinearExpr? previous = null;
                BoolVar? before = null;
                foreach (var period in periods)
                {
                    var busy = classBusy.Contains((classId, day, period)) ? 1 : 0;
                    var occupied = Occupied(unitIndexes, day, period, busy);
                    model.Add(occupied <= 1);
                    daySum.Add(occupied);
                    objective.AddTerm(occupied, LongDayWeight(period));
                    if (previous is not null)
                    {
                        // A lesson after a free period, with one before it.
                        var gap = model.NewBoolVar($"cs{classId}_{day}_{period}");
                        model.Add(gap >= occupied - previous + before! - 1);
                        objective.AddTerm(gap, ClassGapWeight);
                    }
                    // A lesson so far today.
                    var any = model.NewBoolVar($"cb{classId}_{day}_{period}");
                    model.Add(any >= occupied);
                    if (before is not null)
                    {
                        model.Add(any >= before);
                    }
                    if (block.Contains(period))
                    {
                        // An elective lesson leaves some pupils without one.
                        var uncovered = model.NewBoolVar($"mb{classId}_{day}_{period}");
                        model.Add(uncovered >= 1 - Occupied(forAllUnits, day, period, keptForAll.Contains((classId, day, period)) ? 1 : 0));
                        objective.AddTerm(uncovered, MainBlockWeight);
                        blockGaps.Add(uncovered);
                    }
                    previous = occupied;
                    before = any;
                }
                // Lessons that stay can already take more.
                model.Add(daySum <= Math.Max(perDay, periods.Count(p => classBusy.Contains((classId, day, p)))));
                var shortfall = model.NewIntVar(0, minPerDay, $"short{classId}_{day}");
                model.Add(shortfall >= minPerDay - daySum);
                objective.AddTerm(shortfall, ShortDayWeight);
            }

            // An elective lesson with a lesson for everyone before and after it.
            if (electiveUnits.Count > 0 || keptElective.Any(k => k.ClassId == classId))
            {
                foreach (var day in days)
                {
                    var before = new BoolVar[periods.Count];
                    var after = new BoolVar[periods.Count];
                    LinearExpr ForAll(int i) =>
                        Occupied(forAllUnits, day, periods[i], keptForAll.Contains((classId, day, periods[i])) ? 1 : 0);
                    for (var i = 0; i < periods.Count; i++)
                    {
                        before[i] = model.NewBoolVar($"eb{classId}_{day}_{i}");
                        if (i > 0)
                        {
                            model.Add(before[i] >= before[i - 1]);
                            model.Add(before[i] >= ForAll(i - 1));
                        }
                    }
                    for (var i = periods.Count - 1; i >= 0; i--)
                    {
                        after[i] = model.NewBoolVar($"ea{classId}_{day}_{i}");
                        if (i < periods.Count - 1)
                        {
                            model.Add(after[i] >= after[i + 1]);
                            model.Add(after[i] >= ForAll(i + 1));
                        }
                    }
                    for (var i = 0; i < periods.Count; i++)
                    {
                        var inside = model.NewBoolVar($"ei{classId}_{day}_{i}");
                        var elective = Occupied(electiveUnits, day, periods[i], keptElective.Contains((classId, day, periods[i])) ? 1 : 0);
                        model.Add(inside >= elective + before[i] + after[i] - 2);
                        objective.AddTerm(inside, ElectiveInsideWeight);
                    }
                }
            }

            // A subject at most once a day (or as evenly as its hours allow),
            // a block counting once.
            foreach (var subjectUnits in unitIndexes.GroupBy(u => units[u].Demand.Key))
            {
                var subjectId = subjectUnits.Key;
                var count = subjectUnits.Sum(u => units[u].Count)
                    + keptSlots.Count(k => k.ClassId == classId && k.Key == subjectId);
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
                        daySum.Add(keptSlots.Count(k => k.ClassId == classId && k.Key == subjectId && k.Day == day && k.Period == period));
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
        // A unit once per part that needs a classroom.
        var needs = units.SelectMany((u, i) => u.Demand.Parts.Where(p => p.Rooms.Count > 0).Select(p => (Unit: i, p.Rooms))).ToList();
        var roomSets = needs.Select(n => n.Rooms.Order().ToArray()).ToList();
        var pools = roomSets.Concat(Components(roomSets))
            .DistinctBy(r => string.Join(',', r)).ToList();
        foreach (var pool in pools)
        {
            var inPool = needs.Where(n => n.Rooms.All(pool.Contains)).Select(n => n.Unit).ToList();
            // A class has one lesson at a time, so its own homeroom never runs out.
            var classes = inPool.All(i => units[i].Demand.Parts.Count == 1 && units[i].Demand.ClassIds.Count == 1)
                ? inPool.Select(i => units[i].Demand.ClassId).Distinct().Count()
                : int.MaxValue;
            if (inPool.Sum(i => units[i].Count) <= 0)
            {
                continue;
            }
            foreach (var (day, period) in slots)
            {
                var free = pool.Count(r => !roomBusy.Contains((r, day, period)));
                if (free >= Math.Min(classes, inPool.Sum(i => cover.TryGetValue((i, day, period), out var vs) ? vs.Count : 0)))
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
        Progress Measure(CpSolverSolutionCallback at, Phase phase)
        {
            var lessons = x.Where(p => at.BooleanValue(p.Value))
                .SelectMany(p => Covered(units[p.Key.Unit], p.Key.Period)
                    .SelectMany(period => Lessons(units[p.Key.Unit].Demand, p.Key.Day, period)))
                .Concat(kept.Select(k => (k.ClassId, k.TeacherId, k.Day, k.Period)))
                .Distinct()
                .ToList();
            var classDays = lessons.GroupBy(l => (l.ClassId, l.Day)).ToList();
            var placedCount = x.Where(p => at.BooleanValue(p.Value)).Sum(p => units[p.Key.Unit].Length) + kept.Count;
            return new Progress(
                phase, placedCount, total, (int)overs.Sum(at.Value),
                classDays.Sum(d => Holes(d.Select(l => l.Period))),
                (int)blockGaps.Sum(at.Value),
                lessons.Where(l => l.TeacherId is not null).GroupBy(l => (l.TeacherId, l.Day)).Sum(d => Holes(d.Select(l => l.Period))));
        }

        // The requirements: until they hold, or can't get any better.
        model.Minimize(objective);
        var solver = new CpSolver { StringParameters = Parameters(input.TimeLimit) };
        CpSolverStatus status;
        using (var requirements = new Requirements(solver, input, at => Measure(at, Phase.Requirements)))
        {
            status = solver.Solve(model, requirements);
        }
        var timedOut = solver.WallTime() >= input.TimeLimit.TotalSeconds - 1;
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
        model.ClearHints();
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
            timedOut && penalty > 0 && penalty > bound, penalty, bound)
        {
            ShortBlocks = shortBlocks,
        };
    }

    /// <summary>The long days' weight of a class's week at best: as even as
    /// the hours allow, each day in its cheapest periods. It isn't a breach,
    /// so it doesn't keep the search from stopping at no breach at all.</summary>
    private static int UnavoidableLongDays(int weekly, int dayCount, IReadOnlyList<int> periods)
    {
        var cheapest = periods.Select(LongDayWeight).Order().ToList();
        int Day(int lessons) => cheapest.Take(lessons).Sum();
        var longer = weekly % dayCount;
        return longer * Day(weekly / dayCount + 1) + (dayCount - longer) * Day(weekly / dayCount);
    }

    private static IEnumerable<(int ClassId, int? TeacherId, DayOfWeek Day, int Period)> Lessons(Demand demand, DayOfWeek day, int period)
    {
        var teachers = demand.Parts.Select(p => p.TeacherId).Distinct().ToList();
        return demand.ClassIds.SelectMany(c => teachers.Select(t => (c, t, day, period)));
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

    /// <summary>Nothing better than no breach at all; stops too once it
    /// hasn't found anything better for <see cref="Input.RequirementsStall"/>.</summary>
    private sealed class Requirements : Reporting, IDisposable
    {
        private readonly Timer timer;
        private long improvedAt;

        public Requirements(CpSolver solver, Input input, Func<CpSolverSolutionCallback, Progress> measure)
            : base(measure, input.OnProgress)
        {
            timer = new Timer(_ =>
            {
                var at = Interlocked.Read(ref improvedAt);
                if (at > 0 && Environment.TickCount64 - at > input.RequirementsStall.TotalMilliseconds)
                {
                    solver.StopSearch();
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public override void OnSolutionCallback()
        {
            Interlocked.Exchange(ref improvedAt, Environment.TickCount64);
            var done = ObjectiveValue() <= 0;
            Report(force: done);
            if (done)
            {
                StopSearch();
            }
        }

        public new void Dispose()
        {
            timer.Dispose();
            base.Dispose();
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

    private static List<Unplaced> Missing(List<Unit> units, Func<int, int> placedCount) =>
        units.Select((u, i) => (u, Left: u.Count - placedCount(i)))
            .Where(p => p.Left > 0)
            .GroupBy(p => p.u.Demand)
            .Select(g => new Unplaced(g.Key.ClassId, g.Key.SubjectId, g.Key.TeacherId,
                g.Sum(p => p.u.Parity == WeekParity.Every ? p.Left * p.u.Length : p.Left * 0.5m), g.Key.Parts[0].GroupId))
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
    /// the first free one; none free = no classroom. A block stays in one.
    /// So does each group of a division. A lesson staying where it is keeps
    /// its classroom if it can still have it.</summary>
    private static List<Placement> AssignRooms(
        List<(int Unit, DayOfWeek Day, int[] Periods)> placed, List<Unit> units, Input input,
        HashSet<(int, DayOfWeek, int)> roomBusy)
    {
        var taken = new HashSet<(int, DayOfWeek, int)>(roomBusy);
        bool Free(int room, (int Unit, DayOfWeek Day, int[] Periods) l) => l.Periods.All(p => !taken.Contains((room, l.Day, p)));
        var result = new List<Placement>();
        void Place((int Unit, DayOfWeek Day, int[] Periods) lesson, Part part, int? roomId)
        {
            var demand = units[lesson.Unit].Demand;
            foreach (var period in lesson.Periods)
            {
                if (roomId is { } id)
                {
                    taken.Add((id, lesson.Day, period));
                }
                result.Add(new Placement(
                    demand.ClassId, part.SubjectId ?? demand.SubjectId, part.TeacherId, lesson.Day, period, units[lesson.Unit].Parity, roomId,
                    part.GroupId));
            }
        }
        var moving = new List<((int Unit, DayOfWeek Day, int[] Periods) Lesson, Part Part)>();
        foreach (var (lesson, part) in placed.SelectMany(p => units[p.Unit].Demand.Parts.Select(part => (p, part))))
        {
            var homeRooms = lesson.Periods
                .Select(p => units[lesson.Unit].Demand.Homes.FirstOrDefault(h => h.Day == lesson.Day && h.Period == p && h.GroupId == part.GroupId)?.ClassroomId)
                .Distinct().ToList();
            if (units[lesson.Unit].Parity == WeekParity.Every && homeRooms is [{ } room]
                && (part.Rooms.Count == 0 || part.Rooms.Contains(room)) && Free(room, lesson))
            {
                Place(lesson, part, room);
            }
            else
            {
                moving.Add((lesson, part));
            }
        }
        var groups = moving
            .GroupBy(p => (units[p.Lesson.Unit].Demand.ClassId, units[p.Lesson.Unit].Demand.SubjectId, p.Part))
            .OrderBy(g => g.Key.Part.Rooms.Count is var n && n == 0 ? int.MaxValue : n)
            .ThenByDescending(g => g.Sum(l => l.Lesson.Periods.Length));
        foreach (var group in groups)
        {
            var part = group.Key.Part;
            var lessons = group.Select(g => g.Lesson).ToList();
            var demand = units[lessons[0].Unit].Demand;
            var rooms = part.Rooms;
            var has = input.KeptLessons
                .Where(k => part.GroupId is { } groupId ? k.GroupId == groupId : k.GroupId is null && k.ClassId == demand.ClassId && k.SubjectId == demand.SubjectId)
                .Select(k => k.ClassroomId)
                .Concat(demand.Homes.Where(h => h.GroupId == part.GroupId).Select(h => h.ClassroomId))
                .ToList();
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
                Place(lesson, part, roomId);
            }
        }
        return result;
    }
}
