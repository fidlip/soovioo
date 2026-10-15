using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;

namespace Sova.Api.Evidence;

[Authorize]
[ApiController]
[Route("api/subjects")]
public class SubjectsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Subject>>> GetSubjects()
    {
        return Ok(await db.Subjects.OrderBy(s => s.Name).ToListAsync());
    }

    [HttpPost]
    [Authorize(Roles = "Teacher,Admin")]
    public async Task<ActionResult<Subject>> AddSubject(CreateSubjectRequest request)
    {
        var subject = new Subject { Name = request.Name };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetSubjects), new { id = subject.Id }, subject);
    }
}

public record CreateSubjectRequest(string Name);
