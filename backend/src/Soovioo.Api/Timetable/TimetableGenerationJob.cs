using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

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
        var kept = generation.Mode == TimetableGenerationMode.All ? entries.Where(e => e.Locked).ToList() : entries;
        var periods = await db.SchoolPeriods.Select(p => p.PeriodNumber).ToListAsync();
        if (periods.Count == 0)
        {
            periods = [.. Enumerable.Range(1, 8)];
        }
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id);
        var rooms = await db.Classrooms.OrderBy(r => r.Id).Select(r => new { r.Id, r.Name, r.Type, r.Capacity }).ToListAsync();
        var classSizes = await db.Students.GroupBy(s => s.SchoolClassId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        var messages = new List<string>();

        var demands = new List<TimetableGenerator.Demand>();
        foreach (var schoolClass in plan.Classes)
        {
            foreach (var row in schoolClass.Rows.Where(r => r.PlanHours > 0))
            {
                var has = kept.Where(e => e.SchoolClassId == schoolClass.Id && e.SubjectId == row.SubjectId)
                    .Sum(e => e.WeekParity == WeekParity.Every ? 1m : 0.5m);
                var hours = row.PlanHours!.Value - has;
                if (hours <= 0)
                {
                    continue;
                }
                var subject = subjects[row.SubjectId];
                var listed = subject.Classrooms.OrderBy(c => c.Position)
                    .SelectMany(c => c.ClassroomId is { } id ? rooms.Where(r => r.Id == id) : rooms.Where(r => r.Type == c.Type))
                    .DistinctBy(r => r.Id).ToList();
                // Only classrooms the whole class fits in.
                var size = classSizes.GetValueOrDefault(schoolClass.Id);
                var subjectRooms = listed.Where(r => r.Capacity is null || r.Capacity >= size).Select(r => r.Id).ToList();
                if (subject.Classrooms.Count > 0 && listed.Count == 0)
                {
                    messages.Add($"{subject.Name}: škola nemá učebnu potřebného typu, hodiny jsou bez učebny.");
                }
                else if (listed.Count > 0 && subjectRooms.Count == 0)
                {
                    messages.Add($"{schoolClass.Name} {subject.Abbreviation ?? subject.Name}: počet žáků {size} je větší než kapacita učeben předmětu ({string.Join(", ", listed.Select(r => $"{r.Name} {r.Capacity}"))}), hodiny jsou bez učebny.");
                }
                var weekly = (int)Math.Floor(hours);
                demands.Add(new TimetableGenerator.Demand(
                    schoolClass.Id, row.SubjectId, row.TeacherId, weekly, hours - weekly >= 0.5m ? 1 : 0, subjectRooms,
                    row.DoubleLessons));
            }
        }

        var teachers = await TeachersAsync(db, version.SchoolId, plan.Teachers.Select(t => t.Id).ToList());
        TimetableGenerator.Progress? latest = null;
        var solving = Task.Run(() => TimetableGenerator.Generate(new TimetableGenerator.Input(
            [.. TeacherProfile.Weekdays], periods, demands,
            [.. kept.Select(e => new TimetableGenerator.Kept(e.SchoolClassId, e.SubjectId, e.TeacherId, e.ClassroomId, e.DayOfWeek, e.PeriodNumber))],
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
        db.TimetableEntries.AddRange(result.Placements.Select(p => new TimetableEntry
        {
            SchoolId = version.SchoolId,
            TimetableVersionId = version.Id,
            SchoolClassId = p.ClassId,
            SubjectId = p.SubjectId,
            TeacherId = p.TeacherId,
            DayOfWeek = p.Day,
            PeriodNumber = p.Period,
            WeekParity = p.Parity,
            ClassroomId = p.ClassroomId,
        }));

        var classNames = plan.Classes.ToDictionary(c => c.Id, c => c.Name);
        messages.AddRange(result.Unplaced.Select(u =>
            $"{classNames[u.ClassId]} {subjects[u.SubjectId].Abbreviation ?? subjects[u.SubjectId].Name}: nevešlo se {TeachingPlanner.FormatHours(u.Hours)}."));
        var needsRoom = demands.Where(d => d.Rooms.Count > 0).Select(d => (d.ClassId, d.SubjectId)).ToHashSet();
        messages.AddRange(result.Placements
            .Where(p => p.ClassroomId is null && needsRoom.Contains((p.ClassId, p.SubjectId)))
            .GroupBy(p => p.SubjectId)
            .Select(g => $"{subjects[g.Key].Abbreviation ?? subjects[g.Key].Name}: nedostatek učeben, {TeachingPlanner.FormatHours(g.Sum(p => p.Parity == WeekParity.Every ? 1m : 0.5m))} bez učebny ({string.Join(", ", g.Select(p => classNames[p.ClassId]).Distinct().Order())}) — doplňte ji ručně."));
        if (demands.Any(d => d.TeacherId is null))
        {
            messages.Add("Některé předměty nemají učitele, jsou v rozvrhu bez něj.");
        }
        if (result.TimedOut)
        {
            messages.Add("Generátor nestihl rozvrh doladit v časovém limitu, uložil nejlepší nalezený.");
        }
        generation.Placed = result.Placements.Count;
        generation.Messages = [.. messages.Distinct()];
        var lessons = kept.Select(e => (e.SchoolClassId, e.TeacherId, e.DayOfWeek, e.PeriodNumber))
            .Concat(result.Placements.Select(p => (p.ClassId, p.TeacherId, p.Day, p.Period)))
            .ToList();
        generation.Notes = Notes(lessons, classNames, periods.Where(p => p >= 1).DefaultIfEmpty(periods.Min()).Min());
        if (Unmet(lessons, teachers, plan.Teachers.ToDictionary(t => t.Id, t => t.Name)) is { } unmet)
        {
            generation.Notes.Add(unmet);
        }
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

    /// <summary>"Nesplněná přání učitelů: Novák (volné dny, nejvýš 6 h denně)."</summary>
    private static string? Unmet(
        List<(int ClassId, int? TeacherId, DayOfWeek Day, int Period)> lessons,
        Dictionary<int, TimetableGenerator.Teacher> teachers, Dictionary<int, string> names)
    {
        var unmet = new List<string>();
        foreach (var (id, teacher) in teachers.OrderBy(t => names.GetValueOrDefault(t.Key)))
        {
            var byDay = lessons.Where(l => l.TeacherId == id).GroupBy(l => l.Day).ToList();
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
