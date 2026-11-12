using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Hlavní výukový blok: the periods a class has lessons in every
/// day. The school's, a grade's in place of it, a class's in place of its
/// grade's; those two per timetable version, like the učební plán.</summary>
public record MainBlock(int From, int To)
{
    public static readonly MainBlock Default = new(1, 4);

    [JsonIgnore]
    public IEnumerable<int> Periods => Enumerable.Range(From, To - From + 1);

    /// <summary>"1.–4. hodina".</summary>
    public override string ToString() => From == To ? $"{From}. hodina" : $"{From}.–{To}. hodina";

    public static (string Field, string Message)? Error(int from, int to)
    {
        if (from is < 0 or > 15)
        {
            return ("from", "Blok může začínat 0. až 15. hodinou.");
        }
        if (to is < 0 or > 15)
        {
            return ("to", "Blok může končit 0. až 15. hodinou.");
        }
        return to < from ? ("to", "Blok nemůže končit dřív, než začíná.") : null;
    }

    public static async Task<MainBlock> OfSchoolAsync(AppDbContext db, int schoolId) =>
        await db.Schools.Where(s => s.Id == schoolId)
            .Select(s => new MainBlock(s.MainBlockFrom, s.MainBlockTo)).FirstOrDefaultAsync() ?? Default;

    /// <summary>Each class's block in the version, as inherited.</summary>
    public static async Task<Dictionary<int, MainBlock>> ForClassesAsync(AppDbContext db, TimetableVersion version)
    {
        var blocks = await VersionMainBlocks.LoadAsync(db, version);
        return (await db.SchoolClasses.Select(c => new { c.Id, c.Rocnik }).ToListAsync())
            .ToDictionary(c => c.Id, c => blocks.Of(c.Id, c.Rocnik));
    }
}

/// <summary>The school's block with the version's own of grades and classes.</summary>
public record VersionMainBlocks(
    MainBlock School, IReadOnlyDictionary<int, MainBlock> Grades, IReadOnlyDictionary<int, MainBlock> Classes)
{
    /// <summary>The grade's own, else the school's.</summary>
    public MainBlock OfGrade(int? grade) => grade is { } g && Grades.TryGetValue(g, out var own) ? own : School;

    public MainBlock Of(int classId, int? grade) => Classes.TryGetValue(classId, out var own) ? own : OfGrade(grade);

    public static async Task<VersionMainBlocks> LoadAsync(AppDbContext db, TimetableVersion version) => new(
        await MainBlock.OfSchoolAsync(db, version.SchoolId),
        await db.GradeMainBlocks.Where(g => g.TimetableVersionId == version.Id)
            .ToDictionaryAsync(g => g.Grade, g => new MainBlock(g.From, g.To)),
        await db.ClassMainBlocks.Where(c => c.TimetableVersionId == version.Id)
            .ToDictionaryAsync(c => c.SchoolClassId, c => new MainBlock(c.From, c.To)));
}

/// <summary>A grade's own <see cref="MainBlock"/> in a version, in place of the school's.</summary>
public class GradeMainBlock
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int Grade { get; set; }

    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>A class's own <see cref="MainBlock"/> in a version, in place of its grade's.</summary>
public class ClassMainBlock
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int TimetableVersionId { get; set; }
    public TimetableVersion? TimetableVersion { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>The school's <see cref="MainBlock"/> — read by everyone who
/// works on timetables, set with the bell schedule.</summary>
[Authorize]
[ApiController]
[Route("api/main-block")]
[RequirePermission(Permission.Timetable)]
[AuditArea("Rozvrh")]
public class MainBlockController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MainBlock>> Get() => await MainBlock.OfSchoolAsync(db, currentSchool.SchoolId!.Value);

    [HttpPut]
    [AuditAction("Nastaven hlavní blok školy")]
    public async Task<IActionResult> Set(MainBlock request)
    {
        if (MainBlock.Error(request.From, request.To) is var (field, message))
        {
            return this.FieldError(field, message);
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.MainBlockFrom = request.From;
        school.MainBlockTo = request.To;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

/// <param name="From">Null = as the level above.</param>
public record GradeMainBlockDto(int Grade, int? From, int? To);
