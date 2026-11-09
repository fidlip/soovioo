using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;

namespace Soovioo.Api.People;

/// <summary>"Mé děti" for a parent: their children, and who sees the
/// children's data — a pupil's visibility is the parents' call.</summary>
[Authorize(Roles = nameof(RoleKind.Parent))]
[ApiController]
[Route("api/my-children")]
public class MyChildrenController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MyChildDto>>> Get() => Ok(await ChildrenAsync());

    [HttpPut("{studentId:int}/visibility")]
    public async Task<ActionResult<IReadOnlyList<MyChildDto>>> SetVisibility(int studentId, SetVisibilityRequest request)
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await db.ChildIdsOf(me).ContainsAsync(studentId))
        {
            return NotFound();
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var personId = await db.Students.Where(s => s.Id == studentId).Select(s => s.PersonId).FirstAsync();
        var choices = await db.VisibilityChoices.Where(c => c.PersonId == personId).ToListAsync();
        var error = VisibilitySettings.Choose(
            db, await Visibility.LoadAsync(db, schoolId), [VisibilityOwner.Student], choices, request,
            () => new VisibilityChoice { SchoolId = schoolId, PersonId = personId });
        if (error is not null)
        {
            return BadRequest(error);
        }
        await db.SaveChangesAsync();

        return Ok(await ChildrenAsync());
    }

    /// <summary>The parent's choice applies right away (and replaces a
    /// proposal of the child's).</summary>
    [HttpPut("{studentId:int}/nickname")]
    public async Task<ActionResult<IReadOnlyList<MyChildDto>>> SetNickname(
        int studentId, SetNicknameRequest request, [FromServices] Nicknames nicknames)
    {
        if (await ChildAsync(studentId) is not { } student)
        {
            return NotFound();
        }
        var (value, error) = Nicknames.Normalize(request.Nickname);
        if (error is not null)
        {
            return this.FieldError("nickname", error);
        }
        await nicknames.SetAsync(student, value);
        await db.SaveChangesAsync();
        return Ok(await ChildrenAsync());
    }

    [HttpPost("{studentId:int}/nickname/approve")]
    public Task<ActionResult<IReadOnlyList<MyChildDto>>> ApproveNickname(int studentId, [FromServices] Nicknames nicknames) =>
        DecideAsync(studentId, true, nicknames);

    [HttpPost("{studentId:int}/nickname/reject")]
    public Task<ActionResult<IReadOnlyList<MyChildDto>>> RejectNickname(int studentId, [FromServices] Nicknames nicknames) =>
        DecideAsync(studentId, false, nicknames);

    private async Task<ActionResult<IReadOnlyList<MyChildDto>>> DecideAsync(int studentId, bool approve, Nicknames nicknames)
    {
        if (await ChildAsync(studentId) is not { } student)
        {
            return NotFound();
        }
        await nicknames.DecideAsync(student, approve);
        await db.SaveChangesAsync();
        return Ok(await ChildrenAsync());
    }

    private async Task<Evidence.StudentProfile?> ChildAsync(int studentId)
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.ChildIdsOf(me).ContainsAsync(studentId)
            ? await db.Students.FirstOrDefaultAsync(s => s.Id == studentId)
            : null;
    }

    private async Task<IReadOnlyList<MyChildDto>> ChildrenAsync()
    {
        var me = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var childIds = await db.ChildIdsOf(me).ToListAsync();
        var children = await db.Students
            .Where(s => childIds.Contains(s.Id))
            .OrderBy(s => s.Person!.FirstName)
            .Select(s => new
            {
                s.Id, s.PersonId, Name = s.Person!.FirstName + " " + s.Person!.LastName, ClassName = s.SchoolClass!.Name,
                s.Person.Nickname, s.Person.NicknameProposal,
            })
            .ToListAsync();
        var personIds = children.Select(c => c.PersonId).ToList();
        var choices = await db.VisibilityChoices.Where(c => personIds.Contains(c.PersonId)).ToListAsync();
        var visibility = await Visibility.LoadAsync(db, currentSchool.SchoolId!.Value);

        return children
            .Select(c => new MyChildDto(
                c.Id, c.Name, c.ClassName,
                VisibilitySettings.Of(
                    visibility, [VisibilityOwner.Student], choices.Where(ch => ch.PersonId == c.PersonId).ToList(), editable: true),
                c.Nickname, c.NicknameProposal))
            .ToList();
    }
}

/// <param name="NicknameProposal">The child's own, waiting for the parent's approval.</param>
public record MyChildDto(
    int StudentId, string Name, string ClassName, IReadOnlyList<VisibilitySettingDto> Visibility,
    string? Nickname = null, string? NicknameProposal = null);
