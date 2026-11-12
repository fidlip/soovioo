using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.People;

/// <summary>What the generator tries to give a teacher on a day with
/// lessons — the teacher's own, otherwise the school's. Null = no wish.</summary>
public record TeacherWishes(int? MinLessonsPerDay = null, int? MaxLessonsPerDay = null, int? MaxGapsPerDay = null)
{
    public TeacherWishes Or(TeacherWishes school) => new(
        MinLessonsPerDay ?? school.MinLessonsPerDay,
        MaxLessonsPerDay ?? school.MaxLessonsPerDay,
        MaxGapsPerDay ?? school.MaxGapsPerDay);

    public static (string Field, string Message)? Error(int? min, int? max, int? gaps)
    {
        if (min is < 1 or > 12)
        {
            return ("minLessonsPerDay", "Nejméně hodin denně může být 1 až 12.");
        }
        if (max is < 1 or > 12)
        {
            return ("maxLessonsPerDay", "Nejvýš hodin denně může být 1 až 12.");
        }
        if (min > max)
        {
            return ("maxLessonsPerDay", "Nejvýš hodin denně nemůže být méně než nejméně.");
        }
        if (gaps is < 0 or > 10)
        {
            return ("maxGapsPerDay", "Oken denně může být 0 až 10.");
        }
        return null;
    }
}

/// <summary>The school's default <see cref="TeacherWishes"/> — read by
/// everyone who edits a teacher, set with the bell schedule.</summary>
[Authorize]
[ApiController]
[Route("api/teacher-wishes")]
[AuditArea("Rozvrh")]
public class TeacherWishesController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeacherWishes>> Get() => Ok(await SchoolAsync(db, currentSchool.SchoolId!.Value));

    [HttpPut]
    [RequirePermission(Permission.Timetable)]
    [AuditAction("Nastavena výchozí přání učitelů k rozvrhu")]
    public async Task<ActionResult<TeacherWishes>> Set(TeacherWishes request)
    {
        if (TeacherWishes.Error(request.MinLessonsPerDay, request.MaxLessonsPerDay, request.MaxGapsPerDay) is var (field, message))
        {
            return this.FieldError(field, message);
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        school.TeacherMinLessonsPerDay = request.MinLessonsPerDay;
        school.TeacherMaxLessonsPerDay = request.MaxLessonsPerDay;
        school.TeacherMaxGapsPerDay = request.MaxGapsPerDay;
        await db.SaveChangesAsync();
        return Ok(request);
    }

    public static async Task<TeacherWishes> SchoolAsync(AppDbContext db, int schoolId) =>
        await db.Schools.Where(s => s.Id == schoolId)
            .Select(s => new TeacherWishes(s.TeacherMinLessonsPerDay, s.TeacherMaxLessonsPerDay, s.TeacherMaxGapsPerDay))
            .FirstOrDefaultAsync() ?? new TeacherWishes();
}
