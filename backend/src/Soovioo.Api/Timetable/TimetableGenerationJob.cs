using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Timetable;

/// <summary>Runs a <see cref="TimetableGeneration"/> (a Hangfire job): what
/// the version's plan and přidělení want and its timetable lacks goes to
/// <see cref="TimetableGenerator"/>, the result into the version.</summary>
public class TimetableGenerationJob(DbContextOptions<AppDbContext> options, ILogger<TimetableGenerationJob> logger)
{
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    public async Task RunAsync(int generationId, int schoolId)
    {
        await using var db = new AppDbContext(options, new FixedSchoolContext(schoolId));
        var generation = await db.TimetableGenerations.Include(g => g.TimetableVersion)
            .FirstOrDefaultAsync(g => g.Id == generationId);
        if (generation is null || generation.Status != TimetableGenerationStatus.Queued)
        {
            return;
        }
        generation.Status = TimetableGenerationStatus.Running;
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
        await db.SaveChangesAsync();
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
        var rooms = await db.Classrooms.OrderBy(r => r.Id).Select(r => new { r.Id, r.Type }).ToListAsync();
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
                var subjectRooms = subject.Classrooms.OrderBy(c => c.Position)
                    .SelectMany(c => c.ClassroomId is { } id ? [id] : rooms.Where(r => r.Type == c.Type).Select(r => r.Id))
                    .Distinct().ToList();
                if (subject.Classrooms.Count > 0 && subjectRooms.Count == 0)
                {
                    messages.Add($"{subject.Name}: škola nemá učebnu potřebného typu, hodiny jsou bez učebny.");
                }
                var weekly = (int)Math.Floor(hours);
                demands.Add(new TimetableGenerator.Demand(
                    schoolClass.Id, row.SubjectId, row.TeacherId, weekly, hours - weekly >= 0.5m ? 1 : 0, subjectRooms));
            }
        }

        var teacherDays = plan.Teachers.ToDictionary(t => t.Id, t => (IReadOnlyCollection<DayOfWeek>)t.TeachingDays);
        var result = TimetableGenerator.Generate(new TimetableGenerator.Input(
            [.. TeacherProfile.Weekdays], periods, demands,
            [.. kept.Select(e => new TimetableGenerator.Kept(e.SchoolClassId, e.SubjectId, e.TeacherId, e.ClassroomId, e.DayOfWeek, e.PeriodNumber))],
            teacherDays, timeLimit));
        logger?.LogInformation("Timetable generation {Id}: penalty {Objective}, bound {Bound}",
            generation.Id, result.Objective, result.Bound);
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
        generation.Placed = result.Placements.Count;
        generation.Messages = [.. messages.Distinct()];
    }
}
