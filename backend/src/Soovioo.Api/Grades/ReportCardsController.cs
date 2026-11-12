using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Grades;

/// <summary>Vysvědčení of a half-year. A report card grade is suggested from
/// the average until a teacher who grades the pupil in the subject changes
/// or confirms it; a confirmed one only the school's management (Zápis
/// známek for the whole school) may reopen. Families see report card grades
/// and the PDF once the half-year's report card is issued, and only
/// confirmed ones.</summary>
[Authorize]
[ApiController]
[Route("api/report-cards")]
[AuditArea("Známky")]
public class ReportCardsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    private const string Confirmed = "Známka na vysvědčení je potvrzená — změnit ji jde až po znovuotevření vedením školy.";

    [HttpGet]
    public async Task<ActionResult<List<ReportCardDto>>> GetReportCards(
        int schoolYearStartYear, SchoolHalfYear halfYear, int? schoolClassId = null, int? studentId = null, int? subjectId = null)
    {
        if (await StudentIdsAsync(schoolClassId, studentId) is not { } ids)
        {
            return BadRequest("Vyberte třídu nebo žáka.");
        }

        var cards = await ReportCards.BuildAsync(db, ids, schoolYearStartYear, halfYear);
        var issuedOn = HalfYears.IssuedOn(schoolYearStartYear, halfYear);
        var family = !User.IsStaff();
        var issued = db.Today >= issuedOn;
        var shown = !family || issued;
        var reach = family ? [] : await User.StudentReachAsync(db, Permission.GradesWrite);
        var mayReopen = !family && await User.ScopeAsync(db, Permission.GradesWrite) == PermissionScope.School;

        var whole = await WholeAsync();

        bool MayEdit(int student, int subject) =>
            reach is null || reach.Any(r => r.StudentId == student && (r.SubjectIds is null || r.SubjectIds.Contains(subject)));

        // Outside their own class a teacher sees only the subjects they grade.
        cards = [.. cards
            .Select(card => whole(card.Student)
                ? card
                : card with { Subjects = [.. card.Subjects.Where(s => MayEdit(card.Student.Id, s.SubjectId))] })
            .Where(card => card.Subjects.Count > 0 || whole(card.Student))];

        return Ok(cards.Select(card => new ReportCardDto(
                card.Student.Id,
                card.Student.Person!.FullName,
                card.Student.Gender,
                card.Student.SchoolClassId,
                card.Student.SchoolClass!.Name,
                card.Behavior,
                card.ExcusedHours,
                card.UnexcusedHours,
                shown && whole(card.Student) ? card.Result(halfYear) : null,
                issuedOn,
                issued,
                whole(card.Student),
                [.. card.Subjects
                    .Where(s => subjectId is null || s.SubjectId == subjectId)
                    .Select(s =>
                    {
                        var visible = !family || (shown && s.Confirmed);
                        var mayEdit = !family && MayEdit(card.Student.Id, s.SubjectId);
                        return new ReportCardSubjectDto(
                            s.SubjectId,
                            s.SubjectName,
                            s.Average,
                            s.GradeCount,
                            family ? null : s.Suggested,
                            visible ? s.Kind : null,
                            visible ? s.Value : null,
                            !family && s.Stored is not null && !s.Confirmed,
                            visible && s.Confirmed,
                            visible ? s.Stored?.ConfirmedByName : null,
                            visible ? s.Stored?.ConfirmedAt : null,
                            mayEdit,
                            mayEdit && !s.Confirmed,
                            mayReopen && s.Confirmed);
                    })]))
            .ToList());
    }

    /// <summary>Classes whose report cards the caller sees, theirs as
    /// třídní marked. <c>All</c> = the whole school (a class must then be
    /// picked); otherwise all of them may be shown at once.</summary>
    [HttpGet("classes")]
    public async Task<ActionResult<ReportCardClassesDto>> GetClasses()
    {
        var allowed = await User.StudentIdsAsync(db, Permission.GradesRead);
        var query = db.SchoolClasses.AsQueryable();
        if (allowed is not null)
        {
            query = query.Where(c => c.Students.Any(s => allowed.Contains(s.Id)));
        }
        var userId = User.UserId();
        var classes = await query
            .Select(c => new ReportCardClassDto(c.Id, c.Name, c.ClassTeacherId == userId))
            .ToListAsync();
        return Ok(new ReportCardClassesDto(
            allowed is null,
            [.. classes.OrderBy(c => c.Name, StringComparer.Create(SchoolFormat.Culture, true))]));
    }

    [HttpPut("grades")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Upravena známka na vysvědčení")]
    public async Task<IActionResult> SetGrade(SetReportCardGradeRequest request)
    {
        if (request.Kind == ReportCardGradeKind.Graded ? request.Value is not (>= 1 and <= 5) : request.Value is not null)
        {
            return BadRequest("Neplatná známka na vysvědčení.");
        }
        if (await CheckAsync(request.StudentId, request.SubjectId) is { } error)
        {
            return error;
        }

        var grade = await db.ReportCardGrades.FindAsync(
            request.StudentId, request.SubjectId, request.SchoolYearStartYear, request.HalfYear);
        if (grade?.ConfirmedAt is not null)
        {
            return Conflict(Confirmed);
        }
        var userName = await UserNameAsync();
        if (grade is null)
        {
            grade = new ReportCardGrade
            {
                StudentId = request.StudentId,
                SubjectId = request.SubjectId,
                SchoolYearStartYear = request.SchoolYearStartYear,
                HalfYear = request.HalfYear,
                SchoolId = currentSchool.SchoolId!.Value,
                SetByName = userName,
            };
            db.ReportCardGrades.Add(grade);
        }
        grade.Kind = request.Kind;
        grade.Value = request.Value;
        grade.SetByName = userName;
        grade.SetAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Back to the suggestion from the average.</summary>
    [HttpDelete("grades")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Známka na vysvědčení vrácena na návrh")]
    public async Task<IActionResult> ResetGrade(int studentId, int subjectId, int schoolYearStartYear, SchoolHalfYear halfYear)
    {
        if (await CheckAsync(studentId, subjectId) is { } error)
        {
            return error;
        }
        var grade = await db.ReportCardGrades.FindAsync(studentId, subjectId, schoolYearStartYear, halfYear);
        if (grade?.ConfirmedAt is not null)
        {
            return Conflict(Confirmed);
        }
        if (grade is not null)
        {
            db.ReportCardGrades.Remove(grade);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>Confirms each grade as it stands (changed or suggested);
    /// ones without any value (no grades in the half-year) are skipped.</summary>
    [HttpPost("grades/confirm")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Potvrzeny známky na vysvědčení")]
    public async Task<ActionResult<ConfirmReportCardGradesResult>> ConfirmGrades(ConfirmReportCardGradesRequest request)
    {
        foreach (var (studentId, subjectId) in request.Grades.Select(g => (g.StudentId, g.SubjectId)).Distinct())
        {
            if (!await User.ReachesStudentSubjectAsync(db, Permission.GradesWrite, studentId, subjectId))
            {
                return Forbid();
            }
        }

        var cards = (await ReportCards.BuildAsync(
                db, [.. request.Grades.Select(g => g.StudentId).Distinct()], request.SchoolYearStartYear, request.HalfYear))
            .ToDictionary(c => c.Student.Id);
        var confirmed = 0;
        var skipped = 0;
        var now = DateTimeOffset.UtcNow;
        var userName = await UserNameAsync();
        foreach (var key in request.Grades.Distinct())
        {
            var subject = cards.GetValueOrDefault(key.StudentId)?.Subjects.FirstOrDefault(s => s.SubjectId == key.SubjectId);
            if (subject is null || subject.Kind is not { } kind)
            {
                skipped++;
                continue;
            }
            if (subject.Confirmed)
            {
                continue;
            }
            var grade = subject.Stored;
            if (grade is null)
            {
                grade = new ReportCardGrade
                {
                    StudentId = key.StudentId,
                    SubjectId = key.SubjectId,
                    SchoolYearStartYear = request.SchoolYearStartYear,
                    HalfYear = request.HalfYear,
                    SchoolId = currentSchool.SchoolId!.Value,
                    Kind = kind,
                    Value = subject.Value,
                    SetByName = userName,
                    SetAt = now,
                };
                db.ReportCardGrades.Add(grade);
            }
            grade.ConfirmedByName = userName;
            grade.ConfirmedAt = now;
            confirmed++;
        }
        await db.SaveChangesAsync();
        return Ok(new ConfirmReportCardGradesResult(confirmed, skipped));
    }

    [HttpPost("grades/reopen")]
    [RequirePermission(Permission.GradesWrite)]
    [AuditAction("Znovu otevřena známka na vysvědčení")]
    public async Task<IActionResult> ReopenGrade(ReportCardGradeRequest request)
    {
        if (await User.ScopeAsync(db, Permission.GradesWrite) != PermissionScope.School)
        {
            return Forbid();
        }
        var grade = await db.ReportCardGrades.FindAsync(
            request.StudentId, request.SubjectId, request.SchoolYearStartYear, request.HalfYear);
        if (grade is null)
        {
            return NotFound();
        }
        grade.ConfirmedAt = null;
        grade.ConfirmedByName = null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>One pupil's report card, or the class's — a page each.</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(
        int schoolYearStartYear, SchoolHalfYear halfYear, int? schoolClassId = null, int? studentId = null)
    {
        if (await StudentIdsAsync(schoolClassId, studentId) is not { } ids)
        {
            return BadRequest("Vyberte třídu nebo žáka.");
        }
        var issuedOn = HalfYears.IssuedOn(schoolYearStartYear, halfYear);
        if (!User.IsStaff() && db.Today < issuedOn)
        {
            return BadRequest($"Vysvědčení bude k dispozici od {SchoolFormat.Date(issuedOn)}.");
        }

        var cards = await ReportCards.BuildAsync(db, ids, schoolYearStartYear, halfYear);
        if (cards.Count == 0)
        {
            return NotFound();
        }
        var whole = await WholeAsync();
        if (!cards.All(c => whole(c.Student)))
        {
            return Forbid();
        }
        var school = currentSchool.SchoolId is { } schoolId ? await db.Schools.FindAsync(schoolId) : null;
        var classIds = cards.Select(c => c.Student.SchoolClassId).Distinct().ToList();
        var classTeachers = await db.SchoolClasses
            .Where(c => classIds.Contains(c.Id) && c.ClassTeacher != null)
            .Select(c => new { c.Id, c.ClassTeacher!.Person!.FirstName, c.ClassTeacher.Person.LastName })
            .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim());

        var pdf = ReportCardDocument.Generate(school, cards, classTeachers, schoolYearStartYear, halfYear);
        var name = studentId is not null ? cards[0].Student.Person!.LastName : cards[0].Student.SchoolClass!.Name;
        var kind = halfYear == SchoolHalfYear.First ? "vypis-z-vysvedceni" : "podklady-k-vysvedceni";
        return File(pdf, "application/pdf", $"{kind}-{name}.pdf");
    }

    /// <summary>Whether the caller sees a pupil's whole report card: the
    /// family, whoever sees grades of the whole school, the třídní; other
    /// teachers only the subjects they grade.</summary>
    private async Task<Func<StudentProfile, bool>> WholeAsync()
    {
        if (!User.IsStaff() || await User.ScopeAsync(db, Permission.GradesRead) == PermissionScope.School)
        {
            return _ => true;
        }
        var own = (await db.GetOwnClassIdsAsClassTeacherAsync(User.UserId())).ToHashSet();
        return student => own.Contains(student.SchoolClassId);
    }

    private async Task<string> UserNameAsync()
    {
        var userId = User.UserId();
        var user = await db.Users.Include(u => u.Person).FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? User.Identity?.Name ?? "Neznámý uživatel" : PersonNames.Display(user);
    }

    /// <summary>Pupils of the class / the pupil, as far as the caller sees
    /// grades; null when neither is given and the caller sees the whole
    /// school.</summary>
    private async Task<List<int>?> StudentIdsAsync(int? schoolClassId, int? studentId)
    {
        var allowed = await User.StudentIdsAsync(db, Permission.GradesRead);
        if (allowed is null && schoolClassId is null && studentId is null)
        {
            return null;
        }
        var query = db.Students.AsQueryable();
        if (allowed is not null)
        {
            query = query.Where(s => allowed.Contains(s.Id));
        }
        if (schoolClassId is not null)
        {
            query = query.Where(s => s.SchoolClassId == schoolClassId);
        }
        if (studentId is not null)
        {
            query = query.Where(s => s.Id == studentId);
        }
        return await query.Select(s => s.Id).ToListAsync();
    }

    private async Task<IActionResult?> CheckAsync(int studentId, int subjectId)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId) || !await db.Subjects.AnyAsync(s => s.Id == subjectId))
        {
            return BadRequest("Neznámý žák nebo předmět.");
        }
        if ((await db.CoveredSubjectIdsAsync()).Contains(subjectId))
        {
            return BadRequest("Předmět se učí ve skupinách — známku dejte z předmětu skupiny.");
        }
        return await User.ReachesStudentSubjectAsync(db, Permission.GradesWrite, studentId, subjectId) ? null : Forbid();
    }
}

/// <param name="Suggested">Rounded average; for staff only.</param>
/// <param name="Kind">With <paramref name="Value"/>: the grade on the report
/// card — changed, confirmed or the suggestion. Null without any, and for
/// families until it is confirmed and issued.</param>
/// <param name="Changed">Changed by a teacher, not confirmed yet.</param>
/// <param name="Mine">The caller grades the pupil in the subject, confirmed
/// or not.</param>
public record ReportCardSubjectDto(
    int SubjectId,
    string SubjectName,
    double? Average,
    int GradeCount,
    int? Suggested,
    ReportCardGradeKind? Kind,
    int? Value,
    bool Changed,
    bool Confirmed,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt,
    bool Mine,
    bool CanEdit,
    bool CanReopen);

/// <param name="Result">Once every subject is confirmed (for families once
/// issued); not for <paramref name="Whole"/> false.</param>
/// <param name="Whole">All the pupil's subjects are here — false for a
/// teacher outside their own class, who sees only the subjects they grade
/// and can't print the report card.</param>
public record ReportCardDto(
    int StudentId,
    string StudentName,
    Gender? Gender,
    int SchoolClassId,
    string SchoolClassName,
    BehaviorClassificationValue Behavior,
    int ExcusedHours,
    int UnexcusedHours,
    ReportCardResult? Result,
    DateOnly IssuedOn,
    bool Issued,
    bool Whole,
    IReadOnlyList<ReportCardSubjectDto> Subjects);

public record SetReportCardGradeRequest(
    int StudentId, int SubjectId, int SchoolYearStartYear, SchoolHalfYear HalfYear, ReportCardGradeKind Kind, int? Value);

public record ReportCardGradeRequest(int StudentId, int SubjectId, int SchoolYearStartYear, SchoolHalfYear HalfYear);

/// <param name="Own">The caller is its třídní.</param>
public record ReportCardClassDto(int Id, string Name, bool Own);

public record ReportCardClassesDto(bool All, IReadOnlyList<ReportCardClassDto> Classes);

public record ReportCardGradeKey(int StudentId, int SubjectId);

public record ConfirmReportCardGradesRequest(int SchoolYearStartYear, SchoolHalfYear HalfYear, IReadOnlyList<ReportCardGradeKey> Grades);

public record ConfirmReportCardGradesResult(int Confirmed, int Skipped);
