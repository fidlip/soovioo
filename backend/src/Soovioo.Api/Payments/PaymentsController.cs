using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Payments;

[Authorize]
[ApiController]
[Route("api/payments")]
public class PaymentsController(AppDbContext db, ICurrentSchoolContext currentSchool) : ControllerBase
{
    /// <summary>Unscoped list + <see cref="PermissionAccess.StudentIdsAsync"/>
    /// filter, same shape as Homework/Grades. Staff with the whole school see every
    /// Payment; others only see a Payment at all if
    /// at least one Recipient row matches one of their accessible students —
    /// and even then, ONLY those matching rows are included. A Payment
    /// targeting a whole class must never leak a classmate's amount/VS/paid
    /// status to a parent who only has one child in that class.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PaymentDto>>> GetPayments()
    {
        var allowedStudentIds = await User.StudentIdsAsync(db, Permission.PaymentsRead);

        var payments = await db.Payments
            .Include(p => p.SchoolClasses)
            .Include(p => p.Parents)
            .Include(p => p.Students)
            .Include(p => p.Recipients).ThenInclude(r => r.Student)
            .OrderByDescending(p => p.DueDate)
            .ToListAsync();

        var me = CurrentUserId();
        var labels = await db.StudentLabelsAsync(User, payments.SelectMany(p => p.Recipients.Select(r => r.StudentId)));
        var result = new List<PaymentDto>();
        foreach (var payment in payments)
        {
            IEnumerable<PaymentRecipient> recipients = payment.Recipients;
            // Whoever created it (e.g. a parents' representative) sees all of it.
            var restricted = allowedStudentIds is not null && payment.CreatedByUserId != me;
            if (restricted)
            {
                recipients = recipients.Where(r => allowedStudentIds.Contains(r.StudentId));
            }

            var recipientDtos = recipients.OrderBy(r => r.InstallmentDueDate)
                .Select(r => ToRecipientDto(r) with { StudentLabel = labels.GetValueOrDefault(r.StudentId) })
                .ToList();
            if (restricted && recipientDtos.Count == 0)
            {
                // Nothing here is actually theirs — don't even show the
                // payment exists.
                continue;
            }

            result.Add(ToDto(payment, recipientDtos));
        }

        return Ok(result);
    }

    /// <summary>The school's bank account, for the frontend to build the
    /// client-side SPAYD QR string — needed by every role (a Parent/Student
    /// must be able to render their own payment's QR code), unlike
    /// <see cref="MySchoolController"/>'s Admin-only settings endpoints.</summary>
    [HttpGet("bank-account")]
    public async Task<ActionResult<PaymentBankAccountDto>> GetBankAccount()
    {
        var school = await db.Schools.FindAsync(currentSchool.SchoolId!.Value);
        return Ok(new PaymentBankAccountDto(school?.BankAccountNumber));
    }

    /// <summary>Drives the recipient picker in the create dialog, and is
    /// also what <see cref="ValidatePaymentRequestAsync"/> checks submitted
    /// ids against — same split as <see cref="Events.EventsController"/>,
    /// minus the Teachers list entirely (a Payment never targets teachers).</summary>
    [HttpGet("recipient-options")]
    [RequirePermission(Permission.PaymentsWrite)]
    public async Task<ActionResult<PaymentRecipientOptionsDto>> GetRecipientOptions()
    {
        var schoolId = currentSchool.SchoolId!.Value;

        var myClassIds = await User.ClassIdsAsync(db, Permission.PaymentsWrite);
        if (myClassIds is null)
        {
            var classes = await db.SchoolClasses
                .OrderBy(c => c.Name)
                .Select(c => new PaymentSchoolClassOptionDto(c.Id, c.Name, c.Rocnik))
                .ToListAsync();
            var parents = await db.Users
                .Where(u => u.Roles.Any(r => r.Kind == RoleKind.Parent))
                .OrderBy(u => u.Username)
                .Select(u => new PaymentPersonOptionDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
                .ToListAsync();
            var students = await db.Students
                .OrderBy(s => s.Person!.LastName)
                .Select(s => new PaymentStudentOptionDto(s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, s.SchoolClass!.Name))
                .ToListAsync();
            return Ok(new PaymentRecipientOptionsDto(classes, parents, await LabelledAsync(students)));
        }

        var myClasses = await db.SchoolClasses
            .Where(c => myClassIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new PaymentSchoolClassOptionDto(c.Id, c.Name, c.Rocnik))
            .ToListAsync();
        var myStudentIds = await User.ClassStudentIdsAsync(db, Permission.PaymentsWrite) ?? [];
        var myStudents = await db.Students
            .Where(s => myStudentIds.Contains(s.Id))
            .OrderBy(s => s.Person!.LastName)
            .Select(s => new PaymentStudentOptionDto(s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, s.SchoolClass!.Name))
            .ToListAsync();
        var myParents = await db.ParentStudents
            .Where(ps => myStudentIds.Contains(ps.StudentId) && ps.ParentProfile!.Person!.UserId != null)
            .Select(ps => ps.ParentProfile!.Person!.User!)
            .Distinct()
            .OrderBy(u => u.Username)
            .Select(u => new PaymentPersonOptionDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
            .ToListAsync();

        return Ok(new PaymentRecipientOptionsDto(myClasses, myParents, await LabelledAsync(myStudents)));
    }

    private Task<List<PaymentStudentOptionDto>> LabelledAsync(List<PaymentStudentOptionDto> students) =>
        db.WithStudentLabelsAsync(User, students, s => s.Id, (s, label) => s with { Label = label });

    [HttpPost]
    [RequirePermission(Permission.PaymentsWrite)]
    public async Task<ActionResult<PaymentDto>> CreatePayment(CreatePaymentRequest request)
    {
        var validationError = await ValidatePaymentRequestAsync(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var schoolId = currentSchool.SchoolId!.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly? recurrenceEndDate = request.Recurrence == PaymentRecurrence.None
            ? null
            : request.RecurrenceEndDate ?? PaymentRecipientGenerator.DefaultSchoolYearEndDate(today);

        var payment = new Payment
        {
            SchoolId = schoolId,
            Title = request.Title,
            Description = request.Description,
            DueDate = request.DueDate,
            AmountType = request.AmountType,
            Amount = request.Amount,
            Recurrence = request.Recurrence,
            RecurrenceEndDate = recurrenceEndDate,
            Audience = request.Audience,
            CreatedByUserId = CurrentUserId(),
        };
        SetRecipientTargets(payment, request);

        await PaymentRecipientGenerator.GenerateBatchAsync(db, payment, request.DueDate);

        if (request.Recurrence != PaymentRecurrence.None)
        {
            var next = PaymentRecipientGenerator.Advance(request.DueDate, request.Recurrence);
            payment.NextOccurrenceDueDate = recurrenceEndDate is { } end && next > end ? null : next;
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // Student names for the DTO — re-load with the Student nav populated
        // (the recipients just added above have no nav fixed up in memory).
        var recipientIds = payment.Recipients.Select(r => r.Id).ToList();
        var recipientsWithStudents = await db.PaymentRecipients
            .Include(r => r.Student)
            .Where(r => recipientIds.Contains(r.Id))
            .ToListAsync();

        return CreatedAtAction(
            nameof(GetPayments), new { }, ToDto(payment, recipientsWithStudents.Select(ToRecipientDto).ToList()));
    }

    /// <summary>No Update endpoint — editing a Payment after installments
    /// are already frozen isn't specified by ROADMAP and would need
    /// conflict-resolution rules (what happens to already-generated
    /// recipients) that don't exist. Delete + recreate is the only path.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.PaymentsWrite)]
    public async Task<IActionResult> DeletePayment(int id)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null)
        {
            return NotFound();
        }
        if (payment.CreatedByUserId != CurrentUserId()
            && await User.ScopeAsync(db, Permission.PaymentsWrite) != PermissionScope.School)
        {
            return Forbid();
        }

        db.Payments.Remove(payment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Staff only, deliberately — a Parent/Student marking
    /// their own payment as paid would be a self-reported-fraud problem.
    /// There's no bank API integration (see ROADMAP); staff reconcile the
    /// school's bank statement by hand.</summary>
    [HttpPut("recipients/{id:int}/paid")]
    [RequirePermission(Permission.PaymentsWrite)]
    public async Task<ActionResult<PaymentRecipientDto>> SetRecipientPaid(int id, SetPaymentRecipientPaidRequest request)
    {
        var recipient = await db.PaymentRecipients.Include(r => r.Student).FirstOrDefaultAsync(r => r.Id == id);
        if (recipient is null)
        {
            return NotFound();
        }
        if (!await MayManageRecipientAsync(recipient))
        {
            return Forbid();
        }

        recipient.Paid = request.Paid;
        recipient.PaidAt = request.Paid ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync();

        return Ok(ToRecipientDto(recipient));
    }

    [HttpPut("recipients/{id:int}/variable-symbol")]
    [RequirePermission(Permission.PaymentsWrite)]
    public async Task<ActionResult<PaymentRecipientDto>> SetRecipientVariableSymbol(
        int id, SetPaymentRecipientVariableSymbolRequest request)
    {
        var recipient = await db.PaymentRecipients.Include(r => r.Student).FirstOrDefaultAsync(r => r.Id == id);
        if (recipient is null)
        {
            return NotFound();
        }
        if (!await MayManageRecipientAsync(recipient))
        {
            return Forbid();
        }

        recipient.VariableSymbol = request.VariableSymbol;
        await db.SaveChangesAsync();

        return Ok(ToRecipientDto(recipient));
    }

    /// <summary>Null means valid. Mirrors
    /// <see cref="Events.EventsController.ValidateEventRequestAsync"/>'s
    /// subset-check exactly — "cílení stejně jako u akcí" (ROADMAP) means the
    /// whole mechanism, including the same restriction on what a Teacher may
    /// target, not just the UI shape.</summary>
    private async Task<string?> ValidatePaymentRequestAsync(CreatePaymentRequest request)
    {
        if (request.Amount <= 0)
        {
            return "Amount must be greater than zero.";
        }
        if (request.Recurrence != PaymentRecurrence.None
            && request.RecurrenceEndDate is { } end && end < request.DueDate)
        {
            return "RecurrenceEndDate must not be before DueDate.";
        }
        if (request.Audience == PaymentAudience.Everyone
            && await User.ScopeAsync(db, Permission.PaymentsWrite) != PermissionScope.School)
        {
            return "Platbu celé škole může založit jen ten, kdo má platby pro celou školu.";
        }
        if (request.Audience != PaymentAudience.Selected)
        {
            return null;
        }

        var allowed = await GetAllowedRecipientIdsAsync();
        if ((request.SchoolClassIds ?? []).Any(x => !allowed.ClassIds.Contains(x)))
        {
            return "One or more schoolClassIds are outside what you may target.";
        }
        if ((request.ParentUserIds ?? []).Any(x => !allowed.ParentUserIds.Contains(x)))
        {
            return "One or more parentUserIds are outside what you may target.";
        }
        if ((request.StudentIds ?? []).Any(x => !allowed.StudentIds.Contains(x)))
        {
            return "One or more studentIds are outside what you may target.";
        }
        return null;
    }

    private async Task<AllowedRecipientIds> GetAllowedRecipientIdsAsync()
    {
        var schoolId = currentSchool.SchoolId!.Value;

        var myClassIds = await User.ClassIdsAsync(db, Permission.PaymentsWrite);
        if (myClassIds is null)
        {
            var classIds = await db.SchoolClasses.Where(c => c.SchoolId == schoolId).Select(c => c.Id).ToListAsync();
            var parentIds = await db.Users
                .Where(u => u.SchoolId == schoolId && u.Roles.Any(r => r.Kind == RoleKind.Parent)).Select(u => u.Id).ToListAsync();
            var studentIds = await db.Students.Where(s => s.SchoolId == schoolId).Select(s => s.Id).ToListAsync();
            return new AllowedRecipientIds(classIds.ToHashSet(), parentIds.ToHashSet(), studentIds.ToHashSet());
        }

        var myStudentIds = await User.ClassStudentIdsAsync(db, Permission.PaymentsWrite) ?? [];
        var myParentIds = await db.ParentStudents
            .Where(ps => myStudentIds.Contains(ps.StudentId)).ParentAccountLinks().Select(l => l.UserId).Distinct().ToListAsync();
        return new AllowedRecipientIds(myClassIds, myParentIds.ToHashSet(), myStudentIds);
    }

    private void SetRecipientTargets(Payment payment, CreatePaymentRequest request)
    {
        payment.SchoolClasses.Clear();
        payment.Parents.Clear();
        payment.Students.Clear();
        if (request.Audience != PaymentAudience.Selected)
        {
            return;
        }

        var schoolId = currentSchool.SchoolId!.Value;
        foreach (var id in request.SchoolClassIds ?? [])
        {
            payment.SchoolClasses.Add(new PaymentSchoolClass { SchoolClassId = id, SchoolId = schoolId });
        }
        foreach (var id in request.ParentUserIds ?? [])
        {
            payment.Parents.Add(new PaymentParent { UserId = id, SchoolId = schoolId });
        }
        foreach (var id in request.StudentIds ?? [])
        {
            payment.Students.Add(new PaymentStudent { StudentId = id, SchoolId = schoolId });
        }
    }

    /// <summary>A pupil within reach, or any recipient of one's own payment.</summary>
    private async Task<bool> MayManageRecipientAsync(PaymentRecipient recipient) =>
        await db.Payments.AnyAsync(p => p.Id == recipient.PaymentId && p.CreatedByUserId == CurrentUserId())
        || await User.ReachesStudentAsync(db, Permission.PaymentsWrite, recipient.StudentId);

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static PaymentDto ToDto(Payment p, IReadOnlyList<PaymentRecipientDto> recipients) => new(
        p.Id, p.Title, p.Description, p.DueDate, p.AmountType, p.Amount, p.Recurrence, p.RecurrenceEndDate, p.Audience,
        p.SchoolClasses.Select(c => c.SchoolClassId).ToList(),
        p.Parents.Select(pa => pa.UserId).ToList(),
        p.Students.Select(s => s.StudentId).ToList(),
        recipients);

    private static PaymentRecipientDto ToRecipientDto(PaymentRecipient r) => new(
        r.Id,
        r.StudentId,
        r.Student!.Person!.FullName,
        r.Amount,
        r.VariableSymbol,
        r.VariableSymbol ?? r.Student.DefaultVariableSymbol ?? "",
        r.InstallmentDueDate,
        r.Paid,
        r.PaidAt);

    private record AllowedRecipientIds(HashSet<int> ClassIds, HashSet<int> ParentUserIds, HashSet<int> StudentIds);
}

public record CreatePaymentRequest(
    string Title,
    string? Description,
    DateOnly DueDate,
    PaymentAmountType AmountType,
    decimal Amount,
    PaymentRecurrence Recurrence,
    PaymentAudience Audience,
    DateOnly? RecurrenceEndDate = null,
    IReadOnlyList<int>? SchoolClassIds = null,
    IReadOnlyList<int>? ParentUserIds = null,
    IReadOnlyList<int>? StudentIds = null);

public record SetPaymentRecipientPaidRequest(bool Paid);

public record SetPaymentRecipientVariableSymbolRequest(string? VariableSymbol);

public record PaymentDto(
    int Id,
    string Title,
    string? Description,
    DateOnly DueDate,
    PaymentAmountType AmountType,
    decimal Amount,
    PaymentRecurrence Recurrence,
    DateOnly? RecurrenceEndDate,
    PaymentAudience Audience,
    IReadOnlyList<int> SchoolClassIds,
    IReadOnlyList<int> ParentUserIds,
    IReadOnlyList<int> StudentIds,
    IReadOnlyList<PaymentRecipientDto> Recipients);

public record PaymentRecipientDto(
    int Id,
    int StudentId,
    string StudentName,
    decimal Amount,
    string? VariableSymbol,
    string EffectiveVariableSymbol,
    DateOnly InstallmentDueDate,
    bool Paid,
    DateTimeOffset? PaidAt,
    NameLabel? StudentLabel = null);

public record PaymentRecipientOptionsDto(
    IEnumerable<PaymentSchoolClassOptionDto> Classes,
    IEnumerable<PaymentPersonOptionDto> Parents,
    IEnumerable<PaymentStudentOptionDto> Students);

public record PaymentSchoolClassOptionDto(int Id, string Name, int? Rocnik);
public record PaymentPersonOptionDto(int Id, string Username, string Name);
public record PaymentStudentOptionDto(
    int Id, string FirstName, string LastName, int SchoolClassId, string SchoolClassName, NameLabel? Label = null);
public record PaymentBankAccountDto(string? BankAccountNumber);
