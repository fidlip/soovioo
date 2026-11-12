using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Behavior;

[Authorize]
[ApiController]
[Route("api")]
[AuditArea("Chování")]
public class BehaviorController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <summary>Unscoped, like GET /api/grades — Teacher/Admin see every
    /// record in the school, Parent/Student only their own accessible
    /// student(s)' records.</summary>
    [HttpGet("behavior-records")]
    public async Task<ActionResult<IEnumerable<BehaviorRecordDto>>> GetBehaviorRecords()
    {
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.BehaviorRead);

        var query = db.BehaviorRecords.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(r => allowedStudentIds.Contains(r.StudentId));
        }

        var records = await query
            .OrderByDescending(r => r.Date)
            .Select(r => new BehaviorRecordDto(
                r.Id, r.StudentId, r.Student!.Person!.FirstName + " " + r.Student.Person!.LastName,
                r.Type, r.Text, r.Date, r.RecordedByName, r.CreatedAt, r.ReadAt))
            .ToListAsync();

        return Ok(await db.WithStudentLabelsAsync(User, records, r => r.StudentId, (r, label) => r with { StudentLabel = label }));
    }

    /// <summary>Same shared-per-record semantics as MessagesController.MarkRead
    /// — any caller who may see this record may also mark it read, not just
    /// the targeted student's own Parent/Student account (mirrors the same
    /// simplification Message.ReadAt already makes).</summary>
    [HttpPost("behavior-records/{id:int}/read")]
    [AuditAction("Potvrzeno přečtení záznamu chování")]
    public async Task<IActionResult> MarkBehaviorRecordRead(int id)
    {
        var record = await db.BehaviorRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (!await User.ReachesStudentAsync(db, Permission.BehaviorRead, record.StudentId))
        {
            return Forbid();
        }

        record.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Pochvala/Poznámka, napomenutí/důtka třídního učitele and
    /// ředitele each have their own permission (by default: any teacher,
    /// the class teacher, Admin).</summary>
    [HttpPost("behavior-records")]
    [Authorize(Roles = nameof(RoleKind.Staff))]
    [AuditAction("Zapsán záznam chování")]
    public async Task<ActionResult<BehaviorRecordDto>> CreateBehaviorRecord(CreateBehaviorRecordRequest request)
    {
        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Neznámý žák.");
        }

        if (!await CanRecordTypeAsync(request.Type, student.Id))
        {
            return Forbid();
        }

        var record = new BehaviorRecord
        {
            StudentId = request.StudentId,
            Type = request.Type,
            Text = request.Text,
            Date = request.Date,
            RecordedByName = User.Identity?.Name ?? "Neznámý uživatel",
            SchoolId = currentSchool.SchoolId!.Value,
        };
        db.BehaviorRecords.Add(record);
        await db.SaveChangesAsync();

        var dto = new BehaviorRecordDto(
            record.Id, record.StudentId, student.Person!.FirstName + " " + student.Person!.LastName,
            record.Type, record.Text, record.Date, record.RecordedByName, record.CreatedAt, record.ReadAt);
        return CreatedAtAction(nameof(GetBehaviorRecords), dto);
    }

    /// <summary>Same per-type rule as creating it — not scoped to "did I
    /// record this one" (same convention as MessagesController.DeleteMessage,
    /// which any Teacher/Admin may delete regardless of sender).</summary>
    [HttpDelete("behavior-records/{id:int}")]
    [Authorize(Roles = nameof(RoleKind.Staff))]
    [AuditAction("Smazán záznam chování")]
    public async Task<IActionResult> DeleteBehaviorRecord(int id)
    {
        var record = await db.BehaviorRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        var student = await db.Students.FindAsync(record.StudentId);
        if (student is null || !await CanRecordTypeAsync(record.Type, student.Id))
        {
            return Forbid();
        }

        db.BehaviorRecords.Remove(record);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("behavior-classifications")]
    public async Task<ActionResult<IEnumerable<BehaviorClassificationDto>>> GetBehaviorClassifications()
    {
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.BehaviorRead);

        var query = db.BehaviorClassifications.AsQueryable();
        if (allowedStudentIds is not null)
        {
            query = query.Where(c => allowedStudentIds.Contains(c.StudentId));
        }

        var classifications = await query
            .OrderByDescending(c => c.SchoolYearStartYear).ThenByDescending(c => c.HalfYear)
            .Select(c => new BehaviorClassificationDto(
                c.StudentId, c.Student!.Person!.FirstName + " " + c.Student.Person!.LastName,
                c.SchoolYearStartYear, c.HalfYear, c.Value, c.SetByName, c.SetAt))
            .ToListAsync();

        return Ok(await db.WithStudentLabelsAsync(User, classifications, c => c.StudentId, (c, label) => c with { StudentLabel = label }));
    }

    /// <summary>Upsert — setting it again for the same (student, year, half)
    /// replaces the value, no history kept. VelmiDobre goes with the class
    /// teacher's measures, Uspokojive/Neuspokojive with the director's.</summary>
    [HttpPut("behavior-classifications")]
    [Authorize(Roles = nameof(RoleKind.Staff))]
    [AuditAction("Nastavena klasifikace chování")]
    public async Task<ActionResult<BehaviorClassificationDto>> SetClassification(SetBehaviorClassificationRequest request)
    {
        var student = await db.Students.FindAsync(request.StudentId);
        if (student is null)
        {
            return BadRequest("Neznámý žák.");
        }

        var permission = request.Value == BehaviorClassificationValue.VelmiDobre
            ? Permission.BehaviorClassTeacher
            : Permission.BehaviorDirector;
        if (!await User.ReachesStudentAsync(db, permission, student.Id))
        {
            return Forbid();
        }

        var existing = await db.BehaviorClassifications.FindAsync(
            request.StudentId, request.SchoolYearStartYear, request.HalfYear);
        var setByName = User.Identity?.Name ?? "Neznámý uživatel";
        if (existing is null)
        {
            existing = new BehaviorClassification
            {
                StudentId = request.StudentId,
                SchoolYearStartYear = request.SchoolYearStartYear,
                HalfYear = request.HalfYear,
                SchoolId = currentSchool.SchoolId!.Value,
                Value = request.Value,
                SetByName = setByName,
            };
            db.BehaviorClassifications.Add(existing);
        }
        else
        {
            existing.Value = request.Value;
            existing.SetByName = setByName;
            existing.SetAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();

        var dto = new BehaviorClassificationDto(
            existing.StudentId, student.Person!.FirstName + " " + student.Person!.LastName, existing.SchoolYearStartYear,
            existing.HalfYear, existing.Value, existing.SetByName, existing.SetAt);
        return Ok(dto);
    }

    private async Task<bool> CanRecordTypeAsync(BehaviorRecordType type, int studentId) =>
        await User.ReachesStudentAsync(db, type switch
        {
            BehaviorRecordType.Pochvala or BehaviorRecordType.Poznamka => Permission.BehaviorPraise,
            BehaviorRecordType.NapomenutiTridnihoUcitele or BehaviorRecordType.DutkaTridnihoUcitele => Permission.BehaviorClassTeacher,
            _ => Permission.BehaviorDirector,
        }, studentId);
}

public record CreateBehaviorRecordRequest(int StudentId, BehaviorRecordType Type, string Text, DateOnly Date);

public record BehaviorRecordDto(
    int Id,
    int StudentId,
    string StudentName,
    BehaviorRecordType Type,
    string Text,
    DateOnly Date,
    string RecordedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    NameLabel? StudentLabel = null);

public record SetBehaviorClassificationRequest(
    int StudentId, int SchoolYearStartYear, SchoolHalfYear HalfYear, BehaviorClassificationValue Value);

public record BehaviorClassificationDto(
    int StudentId,
    string StudentName,
    int SchoolYearStartYear,
    SchoolHalfYear HalfYear,
    BehaviorClassificationValue Value,
    string SetByName,
    DateTimeOffset SetAt,
    NameLabel? StudentLabel = null);
