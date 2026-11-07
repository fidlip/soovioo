using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

public record RoomInfo(int Id, string Name, ClassroomType? Type, int? Capacity);

/// <summary>Runs a <see cref="TimetableGeneration"/> (a Hangfire job): what
/// the version's plan and přidělení want and its timetable lacks goes to
/// <see cref="TimetableGenerator"/>, the result into the version.</summary>
public class TimetableGenerationJob(
    DbContextOptions<AppDbContext> options, ILogger<TimetableGenerationJob> logger, TimeProvider clock)
{
    /// <summary>Normally the requirements hold well before; running out of
    /// it is an error.</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(5);

    public const string Queue = "timetable";

    /// <summary>One at a time (<see cref="Queue"/> has a single worker): a
    /// run takes all the cores it gets and a few hundred MB.</summary>
    [Hangfire.Queue(Queue)]
    public async Task RunAsync(int generationId, int schoolId)
    {
        await using var db = new AppDbContext(options, new FixedSchoolContext(schoolId));
        var generation = await db.TimetableGenerations.Include(g => g.TimetableVersion)
            .FirstOrDefaultAsync(g => g.Id == generationId);
        // Running = the server stopped during it and Hangfire runs it again.
        if (generation is null || generation.Status is TimetableGenerationStatus.Done or TimetableGenerationStatus.Failed)
        {
            return;
        }
        generation.Status = TimetableGenerationStatus.Running;
        generation.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        try
        {
            await GenerateAsync(db, generation, TimeLimit, logger);
            generation.Status = TimetableGenerationStatus.Done;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Timetable generation {Id} failed", generationId);
            db.ChangeTracker.Clear();
            db.Attach(generation);
            generation.Status = TimetableGenerationStatus.Failed;
            generation.Messages = ["Generování selhalo, zkuste to znovu."];
        }
        generation.FinishedAt = DateTimeOffset.UtcNow;
        await NotifyAsync(db, generation);
        await db.SaveChangesAsync();
    }

    /// <summary>To whoever started it, opening the result — read already
    /// when they watched it end (see TimetableGenerationController.Seen).</summary>
    private async Task NotifyAsync(AppDbContext db, TimetableGeneration generation)
    {
        if (generation.CreatedByUserId is not { } userId)
        {
            return;
        }
        var name = generation.TimetableVersion?.Name
            ?? await db.TimetableVersions.Where(v => v.Id == generation.TimetableVersionId).Select(v => v.Name).FirstAsync();
        var (title, body) = generation.Status == TimetableGenerationStatus.Failed
            ? ($"Generování rozvrhu „{name}“ selhalo", generation.Messages.FirstOrDefault())
            : generation.Messages.Count == 0
                ? ($"Rozvrh „{name}“ je vygenerovaný", "Splňuje všechny požadavky.")
                : ($"Rozvrh „{name}“ je vygenerovaný, něco je potřeba upravit", generation.Messages[0]);
        await new Notifier(db, clock).NotifyAsync(
            generation.SchoolId, [userId], NotificationType.Timetable, title, body, "timetable", generation.TimetableVersionId);
    }

    public static async Task GenerateAsync(
        AppDbContext db, TimetableGeneration generation, TimeSpan timeLimit, ILogger? logger = null)
    {
        var version = generation.TimetableVersion ?? await db.TimetableVersions.FirstAsync(v => v.Id == generation.TimetableVersionId);
        if (version.Status != TimetableVersionStatus.Draft)
        {
            generation.Messages = ["Generovat jde jen do konceptu."];
            return;
        }

        var plan = await TeachingPlan.LoadAsync(db, version);
        var entries = await db.TimetableEntries.Where(e => e.TimetableVersionId == version.Id).ToListAsync();
        var kept = generation.Mode == TimetableGenerationMode.Fill ? entries : entries.Where(e => e.Locked).ToList();
        var periods = await db.SchoolPeriods.Select(p => p.PeriodNumber).ToListAsync();
        if (periods.Count == 0)
        {
            periods = [.. Enumerable.Range(1, 8)];
        }
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id);
        var rooms = await RoomsAsync(db);
        var homerooms = await db.SchoolClasses.Where(c => c.HomeroomId != null)
            .ToDictionaryAsync(c => c.Id, c => c.HomeroomId!.Value);
        var classSizes = await db.Students.GroupBy(s => s.SchoolClassId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        var messages = new List<string>();

        // Classrooms it can have, first choice first, that the pupils fit in.
        List<int> RoomsFor(Subject subject, int? fixedRoom, int? homeroom, int size, string who, bool group = false)
        {
            var listed = ListedRooms(subject, fixedRoom, homeroom, rooms);
            var fitting = listed.Where(r => r.Capacity is null || r.Capacity >= size).Select(r => r.Id).ToList();
            if (fixedRoom is null && subject.Classrooms.Count > 0 && listed.Count == 0)
            {
                messages.Add(subject.Classrooms.Any(c => c.Type == ClassroomType.Homeroom)
                    ? $"{who} {subject.Abbreviation ?? subject.Name}: {(group ? "skupina nemá kmenovou učebnu (je z více tříd nebo jejich třída žádnou nemá)" : "třída nemá kmenovou učebnu")}, hodiny jsou bez učebny."
                    : $"{subject.Name}: škola nemá učebnu potřebného typu, hodiny jsou bez učebny.");
            }
            else if (listed.Count > 0 && fitting.Count == 0)
            {
                messages.Add($"{who} {subject.Abbreviation ?? subject.Name}: počet žáků {size} je větší než kapacita {(fixedRoom is null ? "učeben předmětu" : "učebny")} ({string.Join(", ", listed.Select(r => $"{r.Name} {r.Capacity}"))}), hodiny jsou bez učebny.");
            }
            return fitting;
        }
        static decimal HoursOf(IEnumerable<TimetableEntry> lessons) => TimetableUpdate.HoursOf(lessons);

        var teachers = await TeachersAsync(db, version.SchoolId, plan.Teachers.Select(t => t.Id).ToList());
        var teacherNames = plan.Teachers.ToDictionary(t => t.Id, t => t.Name);
        var update = generation.Mode == TimetableGenerationMode.Update
            ? new TimetableUpdate(generation.Moves, generation.Mismatches, entries, teachers, teacherNames, rooms.ToDictionary(r => r.Id, r => r.Name))
            : null;

        var demands = new List<TimetableGenerator.Demand>();
        foreach (var schoolClass in plan.Classes)
        {
            foreach (var row in schoolClass.Rows.Where(r => r.PlanHours > 0 && r.Groups.Count == 0))
            {
                int? homeroom = homerooms.TryGetValue(schoolClass.Id, out var ownRoom) ? ownRoom : null;
                // Only classrooms the whole class fits in.
                List<int> SubjectRooms() => RoomsFor(subjects[row.SubjectId], null, homeroom, classSizes.GetValueOrDefault(schoolClass.Id), schoolClass.Name);
                var subjectRooms = update is null ? null : SubjectRooms();
                IReadOnlyList<TimetableGenerator.Home> homes = [];
                decimal? toPlace = null;
                bool Own(TimetableEntry e) => e.SchoolClassId == schoolClass.Id && e.SubjectId == row.SubjectId && e.StudentGroupId is null;
                if (update is not null)
                {
                    var sorted = update.Sort(
                        $"{schoolClass.Name} {subjects[row.SubjectId].Abbreviation ?? subjects[row.SubjectId].Name}",
                        entries.Where(e => !e.Locked && Own(e)).ToList(),
                        row.PlanHours!.Value - HoursOf(kept.Where(Own)),
                        [new(null, row.TeacherId, subjectRooms!, [schoolClass.Id])]);
                    kept.AddRange(sorted.Kept);
                    homes = sorted.Homes;
                    toPlace = sorted.Hours;
                }
                var hours = toPlace ?? row.PlanHours!.Value - HoursOf(kept.Where(Own));
                if (hours <= 0)
                {
                    continue;
                }
                subjectRooms ??= SubjectRooms();
                var weekly = (int)Math.Floor(hours);
                demands.Add(new TimetableGenerator.Demand(
                    schoolClass.Id, row.SubjectId, row.TeacherId, weekly, hours - weekly >= 0.5m ? 1 : 0, subjectRooms,
                    row.DoubleLessons)
                {
                    Homes = homes,
                });
            }
        }

        // A group alone, or all groups of a division at once.
        var groups = await db.StudentGroups.Where(g => g.TimetableVersionId == version.Id)
            .Select(g => new { g.Id, g.ClassroomId, g.GroupDivisionId, Members = g.Members.Count })
            .ToDictionaryAsync(g => g.Id);
        var groupRows = plan.Groups.Where(g => groups.ContainsKey(g.Id)).ToList();
        int KeyOf(TeachingGroupDto g) => g.DivisionId is { } d ? -1_000_000 - d : -g.Id;
        var groupKeys = groupRows.ToDictionary(g => g.Id, KeyOf);
        var groupNames = groupRows.ToDictionary(g => g.Id, g => g.DivisionName ?? g.Name);
        var groupClasses = groupRows.ToDictionary(g => g.Id, g => g.ClassIds);
        var electives = await ElectivesAsync(db, groupRows, classSizes);
        foreach (var together in groupRows.GroupBy(KeyOf))
        {
            var lead = together.First();
            var ids = together.Select(g => g.Id).ToHashSet();
            List<TimetableGenerator.Part> Parts() => together.Select(g =>
            {
                int? homeroom = g.ClassIds.Count == 1 && homerooms.TryGetValue(g.ClassIds[0], out var room) ? room : null;
                return new TimetableGenerator.Part(
                    g.Id, g.TeacherId, RoomsFor(subjects[g.SubjectId], groups[g.Id].ClassroomId, homeroom, groups[g.Id].Members, g.Name, group: true),
                    g.SubjectId);
            }).ToList();
            var parts = update is null ? null : Parts();
            IReadOnlyList<TimetableGenerator.Home> homes = [];
            decimal? toPlace = null;
            bool Own(TimetableEntry e) => e.StudentGroupId is { } id && ids.Contains(id);
            if (update is not null)
            {
                var sorted = update.Sort(
                    groupNames[lead.Id], entries.Where(e => !e.Locked && Own(e)).ToList(),
                    lead.PlanHours - HoursOf(kept.Where(Own)),
                    [.. together.Select((g, i) => new TimetableUpdate.Wanted(g.Id, g.TeacherId, parts![i].Rooms, g.ClassIds))]);
                kept.AddRange(sorted.Kept);
                homes = sorted.Homes;
                toPlace = sorted.Hours;
            }
            var hours = toPlace ?? lead.PlanHours - HoursOf(kept.Where(Own));
            if (hours <= 0)
            {
                continue;
            }
            parts ??= Parts();
            if (together.Where(g => g.TeacherId is not null).GroupBy(g => g.TeacherId).FirstOrDefault(g => g.Count() > 1) is { } twice)
            {
                messages.Add($"{lead.DivisionName}: {twice.First().TeacherName} učí {string.Join(" i ", twice.Select(g => g.Name))} ve stejnou dobu, přidělte jiného učitele.");
            }
            var weekly = (int)Math.Floor(hours);
            demands.Add(new TimetableGenerator.Demand(
                lead.ClassIds[0], lead.SubjectId, lead.TeacherId, weekly, hours - weekly >= 0.5m ? 1 : 0, parts[0].Rooms,
                lead.DoubleLessons)
            {
                ClassIds = [.. together.SelectMany(g => g.ClassIds).Distinct()],
                Parts = parts,
                Key = KeyOf(lead),
                Homes = homes,
                ElectiveClasses = [.. together.SelectMany(g => g.ClassIds.Where(c => electives.Contains((g.Id, c)))).Distinct()],
            });
        }

        if (update is not null)
        {
            kept.AddRange(update.Unplanned(e => $"{plan.Classes.First(c => c.Id == e.SchoolClassId).Name} {subjects[e.SubjectId].Abbreviation ?? subjects[e.SubjectId].Name}"));
            messages.AddRange(update.Messages);
        }
        TimetableGenerator.Progress? latest = null;
        var solving = Task.Run(() => TimetableGenerator.Generate(new TimetableGenerator.Input(
            [.. TeacherProfile.Weekdays], periods, demands,
            [.. kept.Select(e => new TimetableGenerator.Kept(
                e.SchoolClassId, e.SubjectId, e.TeacherId, e.ClassroomId, e.DayOfWeek, e.PeriodNumber,
                e.StudentGroupId is { } g && groupKeys.TryGetValue(g, out var key) ? key : e.StudentGroupId is { } other ? -other : null,
                e.StudentGroupId,
                e.StudentGroupId is { } elective && electives.Contains((elective, e.SchoolClassId))))],
            teachers, timeLimit)
        {
            OnProgress = p => Volatile.Write(ref latest, p),
        }));
        // The best timetable so far, for the progress dialog.
        TimetableGenerator.Progress? written = null;
        while (await Task.WhenAny(solving, Task.Delay(TimeSpan.FromSeconds(2))) != solving)
        {
            var progress = Volatile.Read(ref latest);
            if (progress is not null && progress != written && db.Database.IsRelational())
            {
                await db.TimetableGenerations.Where(g => g.Id == generation.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(g => g.Progress, progress));
                written = progress;
            }
        }
        var result = await solving;
        generation.Progress = Volatile.Read(ref latest);
        logger?.LogInformation("Timetable generation {Id}: penalty {Penalty}, bound {Bound}",
            generation.Id, result.Penalty, result.Bound);
        if (result.TimedOut)
        {
            logger?.LogError("Timetable generation {Id} of version {VersionId} ran out of {Limit} (penalty {Penalty}, bound {Bound})",
                generation.Id, version.Id, timeLimit, result.Penalty, result.Bound);
        }
        if (!result.Solved)
        {
            generation.Messages = [.. messages.Distinct(), "Rozvrh se nepodařilo sestavit v časovém limitu, verze zůstala beze změny."];
            return;
        }

        if (generation.Mode == TimetableGenerationMode.All)
        {
            db.TimetableEntries.RemoveRange(entries.Where(e => !e.Locked));
        }
        var added = result.Placements.SelectMany(p =>
            (p.GroupId is { } groupId ? groupClasses[groupId] : [p.ClassId]).Select(classId => new TimetableEntry
            {
                SchoolId = version.SchoolId,
                TimetableVersionId = version.Id,
                SchoolClassId = classId,
                StudentGroupId = p.GroupId,
                SubjectId = p.SubjectId,
                TeacherId = p.TeacherId,
                DayOfWeek = p.Day,
                PeriodNumber = p.Period,
                WeekParity = p.Parity,
                ClassroomId = p.ClassroomId,
            })).ToList();

        var classNames = plan.Classes.ToDictionary(c => c.Id, c => c.Name);
        string Who(int classId, int? groupId) => groupId is { } g ? groupNames[g] : classNames[classId];
        TimetableUpdate.Changes? changes = null;
        if (update is not null)
        {
            changes = TimetableUpdate.Apply(db, entries.Where(e => !e.Locked).Except(kept).ToList(), added, e =>
                e.StudentGroupId is { } g && groupNames.TryGetValue(g, out var name)
                    ? name
                    : $"{classNames.GetValueOrDefault(e.SchoolClassId)} {subjects[e.SubjectId].Abbreviation ?? subjects[e.SubjectId].Name}");
        }
        else
        {
            db.TimetableEntries.AddRange(added);
        }
        messages.AddRange(result.Unplaced.Select(u => u.GroupId is { } groupId
            ? $"{groupNames[groupId]}: nevešlo se {TeachingPlanner.FormatHours(u.Hours)}."
            : $"{classNames[u.ClassId]} {subjects[u.SubjectId].Abbreviation ?? subjects[u.SubjectId].Name}: nevešlo se {TeachingPlanner.FormatHours(u.Hours)}."));
        var needsRoom = demands.SelectMany(d => d.Parts.Where(p => p.Rooms.Count > 0).Select(p => (p.GroupId, d.ClassId, p.SubjectId ?? d.SubjectId)))
            .ToHashSet();
        messages.AddRange(result.Placements
            .Where(p => p.ClassroomId is null && needsRoom.Contains((p.GroupId, p.ClassId, p.SubjectId)))
            .GroupBy(p => p.SubjectId)
            .Select(g => $"{subjects[g.Key].Abbreviation ?? subjects[g.Key].Name}: nedostatek učeben, {TeachingPlanner.FormatHours(g.Sum(p => p.Parity == WeekParity.Every ? 1m : 0.5m))} bez učebny ({string.Join(", ", g.Select(p => Who(p.ClassId, p.GroupId)).Distinct().Order())}) — doplňte ji ručně."));
        if (demands.Any(d => d.Parts.Any(p => p.TeacherId is null)))
        {
            messages.Add("Některé předměty nemají učitele, jsou v rozvrhu bez něj.");
        }
        if (result.TimedOut)
        {
            messages.Add("Generátor nestihl rozvrh doladit v časovém limitu, uložil nejlepší nalezený.");
        }
        var lessons = kept.Concat(added).Select(e => (e.SchoolClassId, e.TeacherId, e.DayOfWeek, e.PeriodNumber))
            .Distinct()
            .ToList();
        var first = periods.Where(p => p >= 1).DefaultIfEmpty(periods.Min()).Min();
        messages.AddRange(LateBecauseOfTeachers(lessons, kept, demands, teachers, classNames, teacherNames, first));
        generation.Placed = changes?.Count ?? result.Placements.Count;
        generation.Messages = [.. messages.Distinct()];
        generation.Notes = [.. changes?.Notes ?? [], .. Assess([.. kept, .. added], electives, classNames, teachers, teacherNames, first)];
    }

    /// <summary>Groups and the classes not all of whose pupils are in them or,
    /// in a division, in any of its groups. A group on its own with no pupils
    /// yet counts; a division with none says nothing yet.</summary>
    internal static async Task<HashSet<(int GroupId, int ClassId)>> ElectivesAsync(
        AppDbContext db, List<TeachingGroupDto> groups, Dictionary<int, int> classSizes)
    {
        var ids = groups.Select(g => g.Id).ToList();
        var members = await db.StudentGroupMembers.Where(m => ids.Contains(m.StudentGroupId))
            .Select(m => new { m.StudentGroupId, m.Student!.SchoolClassId }).ToListAsync();
        var electives = new HashSet<(int, int)>();
        foreach (var together in groups.GroupBy(g => g.DivisionId is { } d ? -d : g.Id))
        {
            var groupIds = together.Select(g => g.Id).ToHashSet();
            foreach (var classId in together.SelectMany(g => g.ClassIds).Distinct())
            {
                var count = members.Count(m => groupIds.Contains(m.StudentGroupId) && m.SchoolClassId == classId);
                var inDivision = together.First().DivisionId is not null;
                if (count < classSizes.GetValueOrDefault(classId) ? !inDivision || count > 0 : !inDivision && count == 0)
                {
                    electives.UnionWith(together.Where(g => g.ClassIds.Contains(classId)).Select(g => (g.Id, classId)));
                }
            }
        }
        return electives;
    }

    /// <summary>How a timetable looks: gaps and late starts, electives left
    /// in between, wishes not met.</summary>
    internal static List<string> Assess(
        IReadOnlyCollection<TimetableEntry> entries, HashSet<(int GroupId, int ClassId)> electives,
        Dictionary<int, string> classNames, Dictionary<int, TimetableGenerator.Teacher> teachers,
        Dictionary<int, string> teacherNames, int first)
    {
        var lessons = entries.Select(e => (e.SchoolClassId, e.TeacherId, e.DayOfWeek, e.PeriodNumber)).Distinct().ToList();
        var notes = Notes(lessons, classNames, first);
        if (ElectivesInside(entries, electives, classNames) is { } inside)
        {
            notes.Add(inside);
        }
        if (Unmet(lessons, teachers, teacherNames) is { } unmet)
        {
            notes.Add(unmet);
        }
        return notes;
    }

    /// <summary>The school's classrooms, as the generator chooses from them.</summary>
    public static Task<List<RoomInfo>> RoomsAsync(AppDbContext db) =>
        db.Classrooms.OrderBy(r => r.Id).Select(r => new RoomInfo(r.Id, r.Name, r.Type, r.Capacity)).ToListAsync();

    /// <summary>The classrooms a lesson can have, first choice first: a
    /// group's own, the subject's list, or else the class's homeroom.</summary>
    public static List<RoomInfo> ListedRooms(Subject subject, int? fixedRoom, int? homeroom, IReadOnlyList<RoomInfo> rooms)
    {
        var wanted = fixedRoom is { } roomId ? [new SubjectClassroom { ClassroomId = roomId }]
            : subject.Classrooms.Count > 0
                ? subject.Classrooms.OrderBy(c => c.Position).ToList()
                : [new SubjectClassroom { Type = ClassroomType.Homeroom }];
        return wanted
            .SelectMany(c => c.ClassroomId is { } id ? rooms.Where(r => r.Id == id)
                : c.Type == ClassroomType.Homeroom ? rooms.Where(r => r.Id == homeroom)
                : rooms.Where(r => r.Type == c.Type))
            .DistinctBy(r => r.Id).ToList();
    }

    /// <summary>"Volitelné předměty uprostřed dne: 9.A st, 9.B pá."</summary>
    private static string? ElectivesInside(
        IEnumerable<TimetableEntry> lessons, HashSet<(int GroupId, int ClassId)> electives, Dictionary<int, string> classNames)
    {
        var inside = lessons.GroupBy(e => (e.SchoolClassId, e.DayOfWeek))
            .Where(day =>
            {
                bool Elective(TimetableEntry e) => e.StudentGroupId is { } g && electives.Contains((g, e.SchoolClassId));
                var forAll = day.Where(e => !Elective(e)).Select(e => e.PeriodNumber).ToList();
                return forAll.Count > 0 && day.Any(e => Elective(e) && e.PeriodNumber > forAll.Min() && e.PeriodNumber < forAll.Max());
            })
            .OrderBy(d => classNames.GetValueOrDefault(d.Key.SchoolClassId)).ThenBy(d => d.Key.DayOfWeek)
            .Select(d => $"{classNames.GetValueOrDefault(d.Key.SchoolClassId)} {Short(d.Key.DayOfWeek)}")
            .ToList();
        return inside.Count == 0 ? null : $"Volitelné předměty uprostřed dne: {string.Join(", ", inside)}.";
    }

    /// <summary>Availability and wishes of the teachers, the school's where
    /// a teacher has none of their own.</summary>
    public static async Task<Dictionary<int, TimetableGenerator.Teacher>> TeachersAsync(
        AppDbContext db, int schoolId, List<int> teacherIds)
    {
        var school = await TeacherWishesController.SchoolAsync(db, schoolId);
        var profiles = await db.Users.Where(u => teacherIds.Contains(u.Id) && u.Person!.TeacherProfile != null)
            .Select(u => new { u.Id, Profile = u.Person!.TeacherProfile! })
            .ToListAsync();
        var own = profiles.ToDictionary(p => p.Id, p => p.Profile);
        return teacherIds.ToDictionary(id => id, id => own.GetValueOrDefault(id) is { } t
            ? new TimetableGenerator.Teacher(t.Availability, t.FreeDays,
                new TeacherWishes(t.MinLessonsPerDay, t.MaxLessonsPerDay, t.MaxGapsPerDay).Or(school))
            : new TimetableGenerator.Teacher([], null, school));
    }

    /// <summary>"1.A: první hodinu může mít jen 1 den v týdnu, Ivana Blažková
    /// (19 h z 20 h) ji učit nemůže; začíná proto později 4 dny." — a class
    /// starting late because its teachers can't teach the first period.</summary>
    private static IEnumerable<string> LateBecauseOfTeachers(
        List<(int ClassId, int? TeacherId, DayOfWeek Day, int Period)> lessons, List<TimetableEntry> kept,
        List<TimetableGenerator.Demand> demands, Dictionary<int, TimetableGenerator.Teacher> teachers,
        Dictionary<int, string> classNames, Dictionary<int, string> teacherNames, int first)
    {
        var days = TeacherProfile.Weekdays;
        bool CanFirst(int teacherId, DayOfWeek day) =>
            !teachers.TryGetValue(teacherId, out var t)
            || AvailabilitySlot.At(t.Availability, day, first) != AvailabilityKind.Unavailable;
        bool AllCanFirst(TimetableGenerator.Demand d, DayOfWeek day) => d.TeacherIds.All(t => CanFirst(t, day));
        foreach (var byClass in lessons.GroupBy(l => l.ClassId).OrderBy(g => classNames.GetValueOrDefault(g.Key)))
        {
            var late = byClass.GroupBy(l => l.Day).Count(d => d.Min(l => l.Period) > first);
            var own = demands.Where(d => d.ClassIds.Contains(byClass.Key)).ToList();
            if (late == 0 || own.Count == 0)
            {
                continue;
            }
            // Each day needs one lesson in the first period of someone who can then.
            var startsKept = kept.Where(e => e.SchoolClassId == byClass.Key && e.PeriodNumber == first)
                .Select(e => e.DayOfWeek).Distinct().Count();
            var possible = Math.Min(days.Length, startsKept + own.Sum(d => Math.Min(d.Weekly, days.Count(day => AllCanFirst(d, day)))));
            var blocking = own.SelectMany(d => d.TeacherIds.Where(t => days.Any(day => !CanFirst(t, day))).Select(t => (Teacher: t, d.Weekly)))
                .GroupBy(x => x.Teacher)
                .Select(g => $"{teacherNames.GetValueOrDefault(g.Key)} ({g.Sum(x => x.Weekly)} h z {own.Sum(d => d.Weekly)} h)")
                .ToList();
            if (possible >= days.Length || blocking.Count == 0)
            {
                continue;
            }
            yield return $"{classNames.GetValueOrDefault(byClass.Key)}: první hodinu "
                + (possible == 0 ? "nemůže mít ani jeden den" : $"může mít jen {DayCount(possible)}")
                + $" v týdnu, {string.Join(", ", blocking)} ji učit {(blocking.Count == 1 ? "nemůže" : "nemohou")}; začíná proto později {DayCount(late)}.";
        }
    }

    /// <summary>"1 den", "3 dny", "5 dní".</summary>
    private static string DayCount(int n) => n switch
    {
        1 => "1 den",
        >= 2 and <= 4 => $"{n} dny",
        _ => $"{n} dní",
    };

    /// <summary>"Nesplněná přání učitelů: Novák (volné dny, nejvýš 6 h denně)."</summary>
    private static string? Unmet(
        List<(int ClassId, int? TeacherId, DayOfWeek Day, int Period)> lessons,
        Dictionary<int, TimetableGenerator.Teacher> teachers, Dictionary<int, string> names)
    {
        var unmet = new List<string>();
        foreach (var (id, teacher) in teachers.OrderBy(t => names.GetValueOrDefault(t.Key)))
        {
            var byDay = lessons.Where(l => l.TeacherId == id).DistinctBy(l => (l.Day, l.Period)).GroupBy(l => l.Day).ToList();
            var wishes = teacher.Wishes ?? new TeacherWishes();
            var missed = new List<string>();
            if (byDay.Any(d => d.Any(l => AvailabilitySlot.At(teacher.Availability, d.Key, l.Period) == AvailabilityKind.Avoid)))
            {
                missed.Add("učí, kdy raději ne");
            }
            if (teacher.FreeDays is { } free && TeacherProfile.Weekdays.Length - byDay.Count < free)
            {
                missed.Add($"volné dny {free}");
            }
            if (wishes.MinLessonsPerDay is { } min && byDay.Any(d => d.Count() < min))
            {
                missed.Add($"nejméně {min} h denně");
            }
            if (wishes.MaxLessonsPerDay is { } max && byDay.Any(d => d.Count() > max))
            {
                missed.Add($"nejvýš {max} h denně");
            }
            if (wishes.MaxGapsPerDay is { } gaps && byDay.Any(d => TimetableGenerator.Holes(d.Select(l => l.Period)) > gaps))
            {
                missed.Add($"nejvýš {gaps} {(gaps is >= 2 and <= 4 ? "okna" : gaps == 1 ? "okno" : "oken")} denně");
            }
            if (missed.Count > 0)
            {
                unmet.Add($"{names.GetValueOrDefault(id)} ({string.Join(", ", missed)})");
            }
        }
        return unmet.Count == 0 ? null : $"Nesplněná přání učitelů: {string.Join("; ", unmet)}.";
    }

    /// <summary>"Okna tříd: 1 (6.B út)" — gaps and late starts of classes,
    /// gaps of teachers.</summary>
    private static List<string> Notes(
        List<(int ClassId, int? TeacherId, DayOfWeek Day, int Period)> lessons, Dictionary<int, string> classNames, int first)
    {
        string Where(IEnumerable<(int ClassId, DayOfWeek Day)> days) => string.Join(", ", days
            .OrderBy(d => classNames.GetValueOrDefault(d.ClassId)).ThenBy(d => d.Day)
            .Select(d => $"{classNames.GetValueOrDefault(d.ClassId)} {Short(d.Day)}"));

        var classDays = lessons.GroupBy(l => (l.ClassId, l.Day)).ToList();
        var gaps = classDays.Where(d => TimetableGenerator.Holes(d.Select(l => l.Period)) > 0).Select(d => d.Key).ToList();
        var late = classDays.Where(d => d.Min(l => l.Period) > first).Select(d => d.Key).ToList();
        var teacherGaps = lessons.Where(l => l.TeacherId is not null).GroupBy(l => (l.TeacherId, l.Day))
            .Sum(d => TimetableGenerator.Holes(d.Select(l => l.Period)));
        return
        [
            gaps.Count == 0 ? "Třídy nemají okna." : $"Okna tříd: {gaps.Count} ({Where(gaps)}).",
            late.Count == 0 ? "Všechny třídy začínají první hodinou." : $"Později než první hodinou začíná: {Where(late)}.",
            $"Okna učitelů: {teacherGaps} h týdně.",
        ];
    }

    private static string Short(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "po",
        DayOfWeek.Tuesday => "út",
        DayOfWeek.Wednesday => "st",
        DayOfWeek.Thursday => "čt",
        DayOfWeek.Friday => "pá",
        DayOfWeek.Saturday => "so",
        _ => "ne",
    };
}
