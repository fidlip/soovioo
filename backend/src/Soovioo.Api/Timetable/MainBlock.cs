using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Timetable;

/// <summary>Hlavní výukový blok: the periods a class has lessons in every
/// day. The school's, a grade's in place of it, a class's in place of its
/// grade's.</summary>
public record MainBlock(int From, int To)
{
    public static readonly MainBlock Default = new(1, 4);

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

    /// <summary>Each class's block, as inherited.</summary>
    public static async Task<Dictionary<int, MainBlock>> ForClassesAsync(AppDbContext db, int schoolId)
    {
        var school = await db.Schools.Where(s => s.Id == schoolId)
            .Select(s => new MainBlock(s.MainBlockFrom, s.MainBlockTo)).FirstOrDefaultAsync() ?? Default;
        var grades = await db.GradeMainBlocks.ToDictionaryAsync(g => g.Grade, g => new MainBlock(g.From, g.To));
        var classes = await db.SchoolClasses
            .Select(c => new { c.Id, c.Rocnik, c.MainBlockFrom, c.MainBlockTo }).ToListAsync();
        return classes.ToDictionary(c => c.Id, c =>
            c is { MainBlockFrom: { } from, MainBlockTo: { } to } ? new MainBlock(from, to)
            : c.Rocnik is { } grade && grades.TryGetValue(grade, out var ofGrade) ? ofGrade
            : school);
    }
}

/// <summary>A grade's own <see cref="MainBlock"/>, in place of the school's.</summary>
public class GradeMainBlock
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int Grade { get; set; }

    public int From { get; set; }
    public int To { get; set; }
}

/// <summary>The school's, grades' and classes' <see cref="MainBlock"/> —
/// read by everyone who works on timetables, set with the bell schedule.</summary>
[Authorize]
[ApiController]
[Route("api/main-blocks")]
[RequirePermission(Permission.Timetable)]
public class MainBlocksController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MainBlocksDto>> Get()
    {
        var school = await db.Schools.Where(s => s.Id == currentSchool.SchoolId!.Value)
            .Select(s => new MainBlock(s.MainBlockFrom, s.MainBlockTo)).FirstOrDefaultAsync() ?? MainBlock.Default;
        var own = await db.GradeMainBlocks.ToDictionaryAsync(g => g.Grade);
        var classes = await db.SchoolClasses.OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Rocnik, c.MainBlockFrom, c.MainBlockTo }).ToListAsync();
        var grades = classes.Where(c => c.Rocnik is not null).Select(c => c.Rocnik!.Value).Concat(own.Keys)
            .Distinct().Order()
            .Select(g => own.TryGetValue(g, out var block)
                ? new GradeMainBlockDto(g, block.From, block.To)
                : new GradeMainBlockDto(g, null, null))
            .ToList();
        return new MainBlocksDto(
            school.From, school.To, grades,
            [.. classes.Select(c => new ClassMainBlockDto(c.Id, c.Name, c.Rocnik, c.MainBlockFrom, c.MainBlockTo))]);
    }

    [HttpPut("school")]
    public async Task<IActionResult> SetSchool(SetMainBlockRequest request)
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

    [HttpPut("grades/{grade:int}")]
    public async Task<IActionResult> SetGrade(int grade, SetMainBlockRequest request)
    {
        if (grade is < 1 or > 13)
        {
            return BadRequest("Ročník může být 1 až 13.");
        }
        if (MainBlock.Error(request.From, request.To) is var (field, message))
        {
            return this.FieldError(field, message);
        }
        var block = await db.GradeMainBlocks.FirstOrDefaultAsync(g => g.Grade == grade);
        if (block is null)
        {
            block = new GradeMainBlock { SchoolId = currentSchool.SchoolId!.Value, Grade = grade };
            db.GradeMainBlocks.Add(block);
        }
        block.From = request.From;
        block.To = request.To;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Back to the school's.</summary>
    [HttpDelete("grades/{grade:int}")]
    public async Task<IActionResult> ClearGrade(int grade)
    {
        await db.DeleteAsync(db.GradeMainBlocks.Where(g => g.Grade == grade));
        return NoContent();
    }

    [HttpPut("classes/{classId:int}")]
    public Task<IActionResult> SetClass(int classId, SetMainBlockRequest request) =>
        MainBlock.Error(request.From, request.To) is var (field, message)
            ? Task.FromResult<IActionResult>(this.FieldError(field, message))
            : SetClassAsync(classId, request);

    /// <summary>Back to its grade's (or the school's).</summary>
    [HttpDelete("classes/{classId:int}")]
    public Task<IActionResult> ClearClass(int classId) => SetClassAsync(classId, null);

    private async Task<IActionResult> SetClassAsync(int classId, SetMainBlockRequest? request)
    {
        if (await db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == classId) is not { } schoolClass)
        {
            return NotFound();
        }
        schoolClass.MainBlockFrom = request?.From;
        schoolClass.MainBlockTo = request?.To;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record SetMainBlockRequest(int From, int To);

/// <param name="From">Null = as the school's.</param>
public record GradeMainBlockDto(int Grade, int? From, int? To);

/// <param name="From">Null = as its grade's (or the school's).</param>
public record ClassMainBlockDto(int Id, string Name, int? Grade, int? From, int? To);

public record MainBlocksDto(int From, int To, List<GradeMainBlockDto> Grades, List<ClassMainBlockDto> Classes);
