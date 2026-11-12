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
    private const string SummaryConfirmed = "Souhrnné hodnocení je potvrzené — změnit ho jde až po znovuotevření vedením školy.";
    private const int MaxText = 20000;
    private const string TooLong = "Slovní hodnocení je příliš dlouhé.";

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
        var mayWriteGrades = !family && await User.HasPermissionAsync(db, Permission.GradesWrite);
        var behaviorReach = family ? [] : await User.StudentIdsAsync(db, Permission.BehaviorClassTeacher);

        var whole = await WholeAsync();

        bool MayEdit(int student, int subject) =>
            reach is null || reach.Any(r => r.StudentId == student && (r.SubjectIds is null || r.SubjectIds.Contains(subject)));

        // Outside their own class a teacher sees only the subjects they grade.
        cards = [.. cards
            .Select(card => whole(card.Student)
                ? card
                : card with { Subjects = [.. card.Subjects.Where(s => MayEdit(card.Student.Id, s.SubjectId))] })
            .Where(card => card.Subjects.Count > 0 || whole(card.Student))];

        return Ok(cards.Select(card =>
            {
                var summarised = card.Summarised(halfYear);
                var summaryVisible = !family || (shown && card.SummaryConfirmed);
                var verbalBehavior = card.BehaviorMode == AssessmentMode.Verbal;
                return new ReportCardDto(
                    card.Student.Id,
                    card.Student.Person!.FullName,
                    card.Student.Gender,
                    card.Student.SchoolClassId,
                    card.Student.SchoolClass!.Name,
                    family && verbalBehavior ? null : card.Behavior,
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
                            var hideValue = family && s.Mode == AssessmentMode.Verbal;
                            return new ReportCardSubjectDto(
                                s.SubjectId,
                                s.SubjectName,
                                s.Average,
                                s.GradeCount,
                                family ? null : s.Suggested,
                                visible ? s.Kind : null,
                                visible && !hideValue ? s.Value : null,
                                !family && s.Changed && !s.Confirmed,
                                visible && s.Confirmed,
                                visible ? s.Stored?.ConfirmedByName : null,
                                visible ? s.Stored?.ConfirmedAt : null,
                                mayEdit,
                                mayEdit && !s.Confirmed,
                                mayReopen && s.Confirmed,
                                s.Mode,
                                visible ? s.VerbalText : null,
                                !family && s.Complete);
                        })],
                    card.BehaviorMode,
                    family && !shown ? null : card.Note?.BehaviorText,
                    !family && card.BehaviorMode != AssessmentMode.Classification
                        && (behaviorReach is null || behaviorReach.Contains(card.Student.Id)),
                    card.HasVerbal(halfYear),
                    whole(card.Student) && card.SummaryAllowed(halfYear)
                        ? new ReportCardSummaryDto(
                            summaryVisible && summarised ? card.Note!.SummaryText : null,
                            summaryVisible && summarised ? card.Note!.SummaryResult : null,
                            summaryVisible && card.SummaryConfirmed,
                            summaryVisible ? card.Note?.SummaryConfirmedByName : null,
                            summaryVisible ? card.Note?.SummaryConfirmedAt : null,
                            mayWriteGrades && !card.SummaryConfirmed,
                            mayReopen && card.SummaryConfirmed)
                        : null);
            })
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
            if (string.IsNullOrWhiteSpace(grade.VerbalText))
            {
                db.ReportCardGrades.Remove(grade);
            }
            else
            {
                grade.Kind = null;
                grade.Value = null;
            }
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>The slovní hodnocení of a subject assessed slovně (or a
    /// stupeň with it); the grade itself stays as it is — the suggestion
    /// too.</summary>
    [AuditAction("Upraveno slovní hodnocení na vysvědčení")]
    [HttpPut("grades/text")]
    [RequirePermission(Permission.GradesWrite)]
    public async Task<IActionResult> SetGradeText(SetReportCardTextRequest request)
    {
        if (request.Text is { Length: > MaxText })
        {
            return BadRequest(TooLong);
        }
        if (await CheckAsync(request.StudentId, request.SubjectId) is { } error)
        {
            return error;
        }
        var student = await db.Students.Include(s => s.SchoolClass).FirstAsync(s => s.Id == request.StudentId);
        if ((await AssessmentModes.LoadAsync(db, [student.SchoolId])).For(student, request.SubjectId).Mode == AssessmentMode.Classification)
        {
            return BadRequest("Předmět se u žáka hodnotí jen klasifikací.");
        }

        var grade = await db.ReportCardGrades.FindAsync(
            request.StudentId, request.SubjectId, request.SchoolYearStartYear, request.HalfYear);
        if (grade?.ConfirmedAt is not null)
        {
            return Conflict(Confirmed);
        }
        var text = Clean(request.Text);
        var userName = await UserNameAsync();
        if (grade is null)
        {
            if (text is null)
            {
                return NoContent();
            }
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
        grade.VerbalText = text;
        grade.SetByName = userName;
        grade.SetAt = DateTimeOffset.UtcNow;
        if (grade.Kind is null && text is null)
        {
            db.ReportCardGrades.Remove(grade);
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The slovní hodnocení of behaviour — the třídní's (its
    /// stupeň is the behaviour classification on Chování).</summary>
    [AuditAction("Upraveno slovní hodnocení chování na vysvědčení")]
    [HttpPut("behavior-text")]
    [RequirePermission(Permission.BehaviorClassTeacher)]
    public async Task<IActionResult> SetBehaviorText(SetReportCardNoteTextRequest request)
    {
        if (request.Text is { Length: > MaxText })
        {
            return BadRequest(TooLong);
        }
        var student = await db.Students.Include(s => s.SchoolClass).FirstOrDefaultAsync(s => s.Id == request.StudentId);
        if (student is null)
        {
            return BadRequest("Neznámý žák.");
        }
        if (!await User.ReachesStudentAsync(db, Permission.BehaviorClassTeacher, student.Id))
        {
            return Forbid();
        }
        if ((await AssessmentModes.LoadAsync(db, [student.SchoolId])).ForBehavior(student).Mode == AssessmentMode.Classification)
        {
            return BadRequest("Chování se u žáka hodnotí jen klasifikací.");
        }
        var note = await NoteAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear);
        note.BehaviorText = Clean(request.Text);
        await SaveNoteAsync(note);
        return NoContent();
    }

    /// <summary>The souhrnné slovní hodnocení of the first half and the
    /// overall result chosen with it; an empty text removes it and the
    /// subjects count again.</summary>
    [AuditAction("Upraveno souhrnné hodnocení na vysvědčení")]
    [HttpPut("summary")]
    [RequirePermission(Permission.GradesWrite)]
    public async Task<IActionResult> SetSummary(SetReportCardSummaryRequest request)
    {
        if (request.Text is { Length: > MaxText })
        {
            return BadRequest(TooLong);
        }
        if (request.Result is { } result && !Enum.IsDefined(result))
        {
            return BadRequest("Neznámé celkové hodnocení.");
        }
        if (await CheckSummaryAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear) is { } error)
        {
            return error;
        }
        var note = await NoteAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear);
        if (note.SummaryConfirmedAt is not null)
        {
            return Conflict(SummaryConfirmed);
        }
        note.SummaryText = Clean(request.Text);
        note.SummaryResult = note.SummaryText is null ? null : request.Result;
        await SaveNoteAsync(note);
        return NoContent();
    }

    [AuditAction("Potvrzeno souhrnné hodnocení na vysvědčení")]
    [HttpPost("summary/confirm")]
    [RequirePermission(Permission.GradesWrite)]
    public async Task<IActionResult> ConfirmSummary(ReportCardNoteRequest request)
    {
        if (await CheckSummaryAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear) is { } error)
        {
            return error;
        }
        var note = await db.ReportCardNotes.FindAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear);
        if (note is not { SummaryText: not null, SummaryResult: not null })
        {
            return BadRequest("Souhrnné hodnocení potřebuje text i celkové hodnocení.");
        }
        if (note.SummaryConfirmedAt is null)
        {
            note.SummaryConfirmedByName = await UserNameAsync();
            note.SummaryConfirmedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    [AuditAction("Znovu otevřeno souhrnné hodnocení na vysvědčení")]
    [HttpPost("summary/reopen")]
    [RequirePermission(Permission.GradesWrite)]
    public async Task<IActionResult> ReopenSummary(ReportCardNoteRequest request)
    {
        if (await User.ScopeAsync(db, Permission.GradesWrite) != PermissionScope.School)
        {
            return Forbid();
        }
        var note = await db.ReportCardNotes.FindAsync(request.StudentId, request.SchoolYearStartYear, request.HalfYear);
        if (note is null)
        {
            return NotFound();
        }
        note.SummaryConfirmedAt = null;
        note.SummaryConfirmedByName = null;
        await db.SaveChangesAsync();
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
            if (subject is null || !subject.Complete)
            {
                skipped++;
                continue;
            }
            var kind = subject.Kind!.Value;
            var value = subject.Value;
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
                    Value = value,
                    SetByName = userName,
                    SetAt = now,
                };
                db.ReportCardGrades.Add(grade);
            }
            else if (grade.Kind is null)
            {
                grade.Kind = kind;
                grade.Value = value;
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
        int schoolYearStartYear, SchoolHalfYear halfYear, int? schoolClassId = null, int? studentId = null,
        ReportCardPrint form = ReportCardPrint.Extract)
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

        if (form == ReportCardPrint.Verbal)
        {
            cards = [.. cards.Where(c => c.HasVerbal(halfYear))];
            if (cards.Count == 0)
            {
                return BadRequest("Nikdo tu není hodnocen slovně.");
            }
        }
        var pdf = form == ReportCardPrint.Verbal
            ? VerbalReportCardDocument.Generate(school, cards, schoolYearStartYear, halfYear)
            : ReportCardDocument.Generate(school, cards, classTeachers, schoolYearStartYear, halfYear);
        var name = studentId is not null ? cards[0].Student.Person!.LastName : cards[0].Student.SchoolClass!.Name;
        var kind = form == ReportCardPrint.Verbal ? "vysvedceni-list-a-b"
            : halfYear == SchoolHalfYear.First ? "vypis-z-vysvedceni" : "podklady-k-vysvedceni";
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

    private async Task<ReportCardNote> NoteAsync(int studentId, int schoolYearStartYear, SchoolHalfYear halfYear)
    {
        var note = await db.ReportCardNotes.FindAsync(studentId, schoolYearStartYear, halfYear);
        if (note is null)
        {
            note = new ReportCardNote
            {
                StudentId = studentId,
                SchoolYearStartYear = schoolYearStartYear,
                HalfYear = halfYear,
                SchoolId = currentSchool.SchoolId!.Value,
                SetByName = "",
            };
            db.ReportCardNotes.Add(note);
        }
        return note;
    }

    private async Task SaveNoteAsync(ReportCardNote note)
    {
        if (note is { BehaviorText: null, SummaryText: null })
        {
            if (db.Entry(note).State != EntityState.Added)
            {
                db.ReportCardNotes.Remove(note);
            }
            else
            {
                db.Entry(note).State = EntityState.Detached;
            }
        }
        else
        {
            note.SetByName = await UserNameAsync();
            note.SetAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Whoever sees the pupil's whole report card and writes
    /// grades (the třídní, the management), when the pupil is assessed only
    /// slovně in the first half.</summary>
    private async Task<IActionResult?> CheckSummaryAsync(int studentId, int schoolYearStartYear, SchoolHalfYear halfYear)
    {
        if (!await User.ReachesStudentAsync(db, Permission.GradesWrite, studentId))
        {
            return Forbid();
        }
        var card = (await ReportCards.BuildAsync(db, [studentId], schoolYearStartYear, halfYear)).SingleOrDefault();
        if (card is null)
        {
            return BadRequest("Neznámý žák.");
        }
        if (!(await WholeAsync())(card.Student))
        {
            return Forbid();
        }
        return card.SummaryAllowed(halfYear)
            ? null
            : BadRequest("Souhrnné hodnocení jde jen v 1. pololetí u žáka hodnoceného slovně ve všech předmětech.");
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().ReplaceLineEndings("\n");

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
    bool CanReopen,
    AssessmentMode Mode = AssessmentMode.Classification,
    string? VerbalText = null,
    bool Complete = false);

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
    BehaviorClassificationValue? Behavior,
    int ExcusedHours,
    int UnexcusedHours,
    ReportCardResult? Result,
    DateOnly IssuedOn,
    bool Issued,
    bool Whole,
    IReadOnlyList<ReportCardSubjectDto> Subjects,
    AssessmentMode BehaviorMode = AssessmentMode.Classification,
    string? BehaviorText = null,
    bool CanEditBehaviorText = false,
    bool HasVerbal = false,
    ReportCardSummaryDto? Summary = null);

/// <summary>The souhrnné slovní hodnocení — only where it's allowed (first
/// half, every subject slovně) and the caller sees the whole report card.
/// Families get the text and result once confirmed and issued.</summary>
public record ReportCardSummaryDto(
    string? Text,
    ReportCardResult? Result,
    bool Confirmed,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt,
    bool CanEdit,
    bool CanReopen);

/// <summary>Extract = výpis z vysvědčení (1. pololetí) or the particulars
/// (2. pololetí); Verbal = vysvědčení list A (slovní hodnocení) and list B
/// of the pupils assessed slovně.</summary>
public enum ReportCardPrint
{
    Extract,
    Verbal,
}

public record SetReportCardTextRequest(
    int StudentId, int SubjectId, int SchoolYearStartYear, SchoolHalfYear HalfYear, string? Text);

public record SetReportCardNoteTextRequest(int StudentId, int SchoolYearStartYear, SchoolHalfYear HalfYear, string? Text);

public record SetReportCardSummaryRequest(
    int StudentId, int SchoolYearStartYear, SchoolHalfYear HalfYear, string? Text, ReportCardResult? Result);

public record ReportCardNoteRequest(int StudentId, int SchoolYearStartYear, SchoolHalfYear HalfYear);

public record SetReportCardGradeRequest(
    int StudentId, int SubjectId, int SchoolYearStartYear, SchoolHalfYear HalfYear, ReportCardGradeKind Kind, int? Value);

public record ReportCardGradeRequest(int StudentId, int SubjectId, int SchoolYearStartYear, SchoolHalfYear HalfYear);

/// <param name="Own">The caller is its třídní.</param>
public record ReportCardClassDto(int Id, string Name, bool Own);

public record ReportCardClassesDto(bool All, IReadOnlyList<ReportCardClassDto> Classes);

public record ReportCardGradeKey(int StudentId, int SubjectId);

public record ConfirmReportCardGradesRequest(int SchoolYearStartYear, SchoolHalfYear HalfYear, IReadOnlyList<ReportCardGradeKey> Grades);

public record ConfirmReportCardGradesResult(int Confirmed, int Skipped);
