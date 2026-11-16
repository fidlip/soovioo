using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>Support tickets (<see cref="SupportTicket"/>) and their threads:
/// the school's admins ask, answer and allow entering; one SuperAdmin takes
/// care of a ticket — changes its state, asks to enter and enters — the
/// others can write to it.</summary>
[Authorize]
[ApiController]
[AuditArea("Podpora")]
public class SupportTicketsController(
    AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier, SupportPlanClient plans,
    SupportSessionService supportSessions, IOptions<SupportOptions> options, TimeProvider clock)
    : ControllerBase
{
    public const int ListLength = 100;

    // ---- The school ----

    /// <summary>The school's tickets, newest first.</summary>
    [HttpGet("api/my-school/support-tickets")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<IReadOnlyList<SupportTicketDto>>> GetSchoolTickets() =>
        Ok(await ListAsync(db.SupportTickets.Where(t => t.SchoolId == currentSchool.SchoolId!.Value)
            .OrderByDescending(t => t.CreatedAt)));

    /// <summary>The school's support plan, for the sidebar.</summary>
    [HttpGet("api/my-school/support-plan")]
    [RequirePermission(Permission.Administration)]
    public async Task<SupportPlanDto> GetSchoolPlan() =>
        await plans.GetPlanAsync(await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value));

    /// <summary>With its thread, without the SuperAdmins' internal entries.</summary>
    [HttpGet("api/my-school/support-tickets/{id:int}")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SupportTicketDetailDto>> GetSchoolTicket(int id)
    {
        var ticket = await SchoolTicketAsync(id);
        return ticket is null ? NotFound() : Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>Due by the school's support plan; the SuperAdmins hear of it.
    /// <see cref="CreateSchoolTicketRequest.AllowAccess"/> allows entering for
    /// <see cref="SupportOptions.ApprovalMinutes"/> (with <see cref="SupportAccess.Approval"/>).</summary>
    [HttpPost("api/my-school/support-tickets")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Vytvořen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CreateSchoolTicket(CreateSchoolTicketRequest request)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        if (ValidateTicket(request.Title, request.Description) is { } invalid)
        {
            return invalid;
        }
        var school = await db.Schools.FirstAsync(s => s.Id == currentSchool.SchoolId!.Value);
        var me = await MeAsync();
        var ticket = await NewTicketAsync(school, me, request.Title!, request.Description, bySupport: false);
        if (request.AllowAccess && school.SupportAccess == SupportAccess.Approval)
        {
            AllowAccess(ticket, me);
        }
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, $"Nový požadavek ({school.Name}): {ticket.Title}", ticket.Description);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>An answer to the support; a ticket waiting for the school
    /// goes back to <see cref="SupportTicketStatus.Assigned"/>.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/messages")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Odpověď k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> AddSchoolMessage(int id, SupportTicketMessageRequest request)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        if (ValidateText(request.Text) is { } invalid)
        {
            return invalid;
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (!ticket.IsOpen)
        {
            return BadRequest(ticket.Status == SupportTicketStatus.Resolved
                ? "Požadavek je vyřešený — pokud není, vraťte ho k řešení."
                : "Požadavek už je uzavřený.");
        }
        var me = await MeAsync();
        AddEntry(ticket, SupportTicketEntryKind.Message, me, text: request.Text!.Trim());
        if (ticket.Status == SupportTicketStatus.WaitingForSchool)
        {
            SetStatus(ticket, SupportTicketStatus.Assigned, me);
        }
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, AnswerTitle(ticket), request.Text);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>A message of the school changed — by its author or whoever
    /// shares a role with them, while the ticket is open.</summary>
    [HttpPut("api/my-school/support-tickets/{id:int}/entries/{entryId:int}")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Upravena zpráva k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> EditSchoolEntry(
        int id, int entryId, EditSupportTicketEntryRequest request)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        return ticket is null ? NotFound() : await EditEntryAsync(ticket, entryId, request.Text, bySupport: false);
    }

    /// <summary>Like <see cref="EditSchoolEntry"/>; the SuperAdmins still
    /// see it was there.</summary>
    [HttpDelete("api/my-school/support-tickets/{id:int}/entries/{entryId:int}")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Smazána zpráva k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> DeleteSchoolEntry(int id, int entryId)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        return ticket is null ? NotFound() : await DeleteEntryAsync(ticket, entryId, bySupport: false);
    }

    /// <summary>The school withdraws a ticket not closed yet, saying why;
    /// entering for it ends.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/cancel")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Zrušen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CancelTicket(int id, CancelSupportTicketRequest request)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        if (ValidateText(request.Reason, "reason", "Napište, proč požadavek rušíte.") is { } invalid)
        {
            return invalid;
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (!ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený.");
        }
        var me = await MeAsync();
        var reason = request.Reason!.Trim();
        ticket.StatusBeforeCancel = ticket.Status;
        var cancelled = SetStatus(ticket, SupportTicketStatus.Cancelled, me, reason);
        await CloseAccessAsync(ticket, cancelled.At);
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, $"Škola zrušila požadavek ({ticket.School!.Name}): {ticket.Title}", reason);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>"Uzavřít" — the school confirms the ticket resolved
    /// (<see cref="MayConfirmAsync"/>); its assignee hears of it.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/close")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Uzavřen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CloseTicket(int id)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (ticket.Status != SupportTicketStatus.Resolved)
        {
            return BadRequest("Uzavřít jde jen vyřešený požadavek.");
        }
        if (!await MayConfirmAsync(ticket))
        {
            return AuthorOnly(ticket);
        }
        SetStatus(ticket, SupportTicketStatus.Closed, await MeAsync());
        await db.SaveChangesAsync();
        await NotifyAssigneeAsync(ticket, $"Škola potvrdila vyřešení ({ticket.School!.Name}): {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>"Vrátit k řešení" — not resolved after all: back to its
    /// assignee (<see cref="SupportTicketStatus.Assigned"/>), or to everyone
    /// when nobody has it; with a message saying why.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/return")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Požadavek na podporu vrácen k řešení")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReturnTicket(int id, ReturnSupportTicketRequest request)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        if (ValidateText(request.Text, "text", "Napište, proč požadavek vracíte k řešení.") is { } invalid)
        {
            return invalid;
        }
        var text = request.Text!.Trim();
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (ticket.Status != SupportTicketStatus.Resolved)
        {
            return BadRequest("Vrátit k řešení jde jen vyřešený požadavek.");
        }
        if (!await MayConfirmAsync(ticket))
        {
            return AuthorOnly(ticket);
        }
        var me = await MeAsync();
        SetStatus(ticket, ticket.AssigneeUserId is null ? SupportTicketStatus.New : SupportTicketStatus.Assigned, me);
        AddEntry(ticket, SupportTicketEntryKind.Message, me, text: text);
        await db.SaveChangesAsync();
        await NotifySupportAsync(
            ticket, $"Škola vrátila požadavek k řešení ({ticket.School!.Name}): {ticket.Title}", text);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>"Znovu otevřít" a closed or cancelled ticket — a mistake,
    /// till <see cref="SupportTicket.ReopenableUntil"/>: closed goes back to
    /// <see cref="SupportTicketStatus.Resolved"/> (its 30 days start again),
    /// cancelled to the status it had. Whoever may close or cancel it.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/reopen")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Znovu otevřen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReopenTicket(int id)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (!(ticket.ReopenableUntil > clock.GetUtcNow()))
        {
            return BadRequest(ticket.Status is SupportTicketStatus.Closed or SupportTicketStatus.Cancelled
                ? "Lhůta pro znovuotevření uplynula — založte nový požadavek."
                : "Požadavek není uzavřený.");
        }
        var cancelled = ticket.Status == SupportTicketStatus.Cancelled;
        if (!cancelled && !await MayConfirmAsync(ticket))
        {
            return AuthorOnly(ticket);
        }
        var status = cancelled
            ? ticket.StatusBeforeCancel ?? (ticket.AssigneeUserId is null ? SupportTicketStatus.New : SupportTicketStatus.Assigned)
            : SupportTicketStatus.Resolved;
        ticket.StatusBeforeCancel = null;
        SetStatus(ticket, status, await MeAsync());
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, $"Škola znovu otevřela požadavek ({ticket.School!.Name}): {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>Lets the support in for this ticket for
    /// <see cref="SupportOptions.ApprovalMinutes"/> (again).</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/grant-access")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Povolen vstup podpory k požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> GrantAccess(int id)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        if (!ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený.");
        }
        if (ticket.School!.SupportAccess != SupportAccess.Approval)
        {
            return BadRequest(ticket.School.SupportAccess == SupportAccess.Never
                ? "Škola vstup podpory nepovoluje — změňte to v nastavení podpory."
                : "Podpora smí do školy vstoupit kdykoliv, povolení není potřeba.");
        }
        var me = await MeAsync();
        AllowAccess(ticket, me);
        await db.SaveChangesAsync();
        if (ticket.AssigneeUserId is { } assignee)
        {
            await notifier.NotifyAsync(
                ticket.SchoolId, [assignee], NotificationType.SupportTicket,
                $"Škola povolila vstup ({ticket.School.Name}): {ticket.Title}", null, SuperAdminPage, ticket.Id);
            await db.SaveChangesAsync();
        }
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>Takes the allowing back; the sessions entered for the
    /// ticket end right away (<see cref="SupportSessionGuardMiddleware"/>).</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/revoke-access")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Odebráno povolení vstupu podpory k požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> RevokeAccess(int id)
    {
        if (InSupportMode())
        {
            return SupportModeRefused();
        }
        var ticket = await SchoolTicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        var now = clock.GetUtcNow();
        if (!(ticket.AccessUntil > now))
        {
            return BadRequest("Vstup už povolený není.");
        }
        var me = await MeAsync();
        ticket.AccessUntil = now;
        var revoked = AddEntry(ticket, SupportTicketEntryKind.AccessRevoked, me);
        // Ended by it — in the thread, within the visit.
        await EndSessionsAsync(ticket.Id, revoked.At);
        await db.SaveChangesAsync();
        if (ticket.AssigneeUserId is { } assignee)
        {
            await notifier.NotifyAsync(
                ticket.SchoolId, [assignee], NotificationType.SupportTicket,
                $"Škola odebrala povolení vstupu ({ticket.School!.Name}): {ticket.Title}", null, SuperAdminPage,
                ticket.Id);
            await db.SaveChangesAsync();
        }
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    // ---- The SuperAdmins ----

    /// <summary>All schools' tickets: open (the default, oldest due first),
    /// mine / unassigned (open ones), or all (newest first).</summary>
    [HttpGet("api/support-tickets")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<SupportTicketDto>>> GetTickets(
        SupportTicketFilter filter = SupportTicketFilter.Open, int? schoolId = null)
    {
        var query = db.SupportTickets.AsQueryable();
        if (schoolId is not null)
        {
            query = query.Where(t => t.SchoolId == schoolId);
        }
        if (filter == SupportTicketFilter.All)
        {
            return Ok(await ListAsync(query.OrderByDescending(t => t.CreatedAt)));
        }
        query = query.Where(t => t.Status != SupportTicketStatus.Resolved && t.Status != SupportTicketStatus.Cancelled
                                 && t.Status != SupportTicketStatus.Closed);
        var me = CurrentUserId();
        query = filter switch
        {
            SupportTicketFilter.Mine => query.Where(t => t.AssigneeUserId == me),
            SupportTicketFilter.Unassigned => query.Where(t => t.AssigneeUserId == null),
            _ => query,
        };
        return Ok(await ListAsync(query.OrderBy(t => t.DueDate).ThenBy(t => t.CreatedAt)));
    }

    /// <summary>With the whole thread, internal entries included.</summary>
    [HttpGet("api/support-tickets/{id:int}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<SupportTicketDetailDto>> GetTicket(int id)
    {
        var ticket = await TicketAsync(id);
        return ticket is null ? NotFound() : Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>Opened by a SuperAdmin for a school — they take care of it;
    /// the school's admins hear of it.</summary>
    [HttpPost("api/support-tickets")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora založila požadavek")]
    public async Task<ActionResult<SupportTicketDetailDto>> CreateTicket(CreateSupportTicketRequest request)
    {
        if (ValidateTicket(request.Title, request.Description) is { } invalid)
        {
            return invalid;
        }
        var school = await db.Schools.FindAsync(request.SchoolId);
        if (school is null)
        {
            return this.FieldError("schoolId", "Škola neexistuje.");
        }
        AuditContext.Of(HttpContext).SchoolId = school.Id;
        var me = await MeAsync();
        var ticket = await NewTicketAsync(school, me, request.Title!, request.Description, bySupport: true);
        Assign(ticket, me, me);
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"Podpora založila požadavek: {ticket.Title}", ticket.Description);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Převzít" a ticket nobody takes care of; with
    /// <see cref="AssignSupportTicketRequest.UserId"/> of another SuperAdmin
    /// "Předat" — that one takes care of it and hears of it. With
    /// <see cref="AssignSupportTicketRequest.TakeOverFrom"/> "Převzít od" its
    /// assignee: they hear of it and their visits of the school end.</summary>
    [HttpPost("api/support-tickets/{id:int}/assign")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora převzala požadavek")]
    public async Task<ActionResult<SupportTicketDetailDto>> AssignTicket(
        int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AssignSupportTicketRequest? request = null)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (!ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený.");
        }
        var me = await MeAsync();
        if (ticket.AssigneeUserId is { } current)
        {
            if (current == me.Id || request?.TakeOverFrom != current || request.UserId is not null)
            {
                return BadRequest($"Požadavek už řeší {ticket.AssigneeName}.");
            }
            var from = ticket.AssigneeName;
            Assign(ticket, me, me, from);
            await EndSessionsAsync(ticket.Id, clock.GetUtcNow());
            await db.SaveChangesAsync();
            await notifier.NotifyAsync(
                ticket.SchoolId, [current], NotificationType.SupportTicket,
                $"Požadavek vám převzal(a) {ticket.AssigneeName} ({ticket.School!.Name}): {ticket.Title}", null,
                SuperAdminPage, ticket.Id);
            await db.SaveChangesAsync();
            await NotifySchoolAsync(ticket, $"Požadavek převzal(a) {ticket.AssigneeName}: {ticket.Title}", null);
            return Ok(await DetailAsync(ticket, withInternal: true));
        }
        var assignee = me;
        if (request?.UserId is { } userId && userId != me.Id)
        {
            var other = await db.Users.IgnoreQueryFilters().Include(u => u.Person)
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsSuperAdmin && u.SchoolId == null);
            if (other is null)
            {
                return BadRequest("Vybraný superadmin neexistuje.");
            }
            assignee = other;
        }
        Assign(ticket, assignee, me);
        await db.SaveChangesAsync();
        if (assignee != me)
        {
            await notifier.NotifyAsync(
                ticket.SchoolId, [assignee.Id], NotificationType.SupportTicket,
                $"Předán vám požadavek ({ticket.School!.Name}): {ticket.Title}", null, SuperAdminPage, ticket.Id);
            await db.SaveChangesAsync();
        }
        await NotifySchoolAsync(ticket, $"Požadavek převzal(a) {ticket.AssigneeName}: {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Přerušit práci" — back to the pool with a summary of the
    /// work so far (internal); the other SuperAdmins hear of it.</summary>
    [HttpPost("api/support-tickets/{id:int}/release")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora přerušila práci na požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReleaseTicket(int id, ReleaseSupportTicketRequest request)
    {
        if (ValidateText(request.Summary, "summary", "Shrňte dosavadní práci.") is { } invalid)
        {
            return invalid;
        }
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (AssigneeOnly(ticket) is { } refused)
        {
            return refused;
        }
        var me = await MeAsync();
        AddEntry(ticket, SupportTicketEntryKind.Released, me, text: request.Summary!.Trim(), @internal: true);
        ticket.AssigneeUserId = null;
        ticket.AssigneeName = null;
        ticket.AccessRequestedAt = null;
        ticket.Status = SupportTicketStatus.New;
        ticket.StatusChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        await NotifySupportAsync(
            ticket, $"Požadavek vrácen k převzetí ({ticket.School!.Name}): {ticket.Title}", request.Summary,
            exceptUserId: me.Id);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>The assignee's: Přiřazeno, Řeší se, Čeká na školu, Vyřešeno
    /// (which ends entering for it); the school's admins hear of it.</summary>
    [HttpPost("api/support-tickets/{id:int}/status")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Změněn stav požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> SetTicketStatus(int id, SetSupportTicketStatusRequest request)
    {
        if (request.Status is not (SupportTicketStatus.Assigned or SupportTicketStatus.InProgress
            or SupportTicketStatus.WaitingForSchool or SupportTicketStatus.Resolved))
        {
            return BadRequest("Tento stav nastavit nelze.");
        }
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (AssigneeOnly(ticket) is { } refused)
        {
            return refused;
        }
        if (ticket.Status == request.Status)
        {
            return Ok(await DetailAsync(ticket, withInternal: true));
        }
        var changed = SetStatus(ticket, request.Status, await MeAsync());
        if (request.Status == SupportTicketStatus.Resolved)
        {
            await CloseAccessAsync(ticket, changed.At);
        }
        await db.SaveChangesAsync();
        await NotifySchoolAsync(
            ticket, $"{StatusTitle(request.Status)}: {ticket.Title}",
            ticket.AutoClosesAt is { } closes
                ? $"Potvrďte vyřešení, nebo požadavek vraťte k řešení. Jinak se {SchoolClock.Local(closes):d. M. yyyy} uzavře sám."
                : null);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Vrátit k řešení" by any SuperAdmin — resolved too early:
    /// back to its assignee; an optional internal note says why. The
    /// school's admins hear of it.</summary>
    [HttpPost("api/support-tickets/{id:int}/return")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora vrátila požadavek k řešení")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReturnTicketBySupport(
        int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReturnSupportTicketRequest? request = null)
    {
        var text = request?.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && ValidateText(text) is { } invalid)
        {
            return invalid;
        }
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (ticket.Status != SupportTicketStatus.Resolved)
        {
            return BadRequest("Vrátit k řešení jde jen vyřešený požadavek.");
        }
        var me = await MeAsync();
        SetStatus(ticket, ticket.AssigneeUserId is null ? SupportTicketStatus.New : SupportTicketStatus.Assigned, me);
        if (!string.IsNullOrEmpty(text))
        {
            AddEntry(ticket, SupportTicketEntryKind.Note, me, text: text, @internal: true);
        }
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"Podpora vrátila požadavek k řešení: {ticket.Title}", null);
        if (ticket.AssigneeUserId != me.Id)
        {
            await NotifyAssigneeAsync(
                ticket, $"Požadavek vrácen k řešení ({ticket.School!.Name}): {ticket.Title}", text);
        }
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>Any SuperAdmin: a message to the school (its admins hear of
    /// it) or an internal note (the assignee hears of it). Notes also on a
    /// closed ticket.</summary>
    [HttpPost("api/support-tickets/{id:int}/messages")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Zpráva podpory k požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> AddMessage(int id, SupportTicketMessageRequest request)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        return await AddMessageAsync(ticket, request);
    }

    private async Task<ActionResult<SupportTicketDetailDto>> AddMessageAsync(
        SupportTicket ticket, SupportTicketMessageRequest request)
    {
        if (ValidateText(request.Text) is { } invalid)
        {
            return invalid;
        }
        if (!request.Internal && !ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený — zbývá interní poznámka.");
        }
        var me = await MeAsync();
        var text = request.Text!.Trim();
        var entry = AddEntry(
            ticket, request.Internal ? SupportTicketEntryKind.Note : SupportTicketEntryKind.Message, me,
            text: text, @internal: request.Internal);
        await db.SaveChangesAsync();
        await NotifyOfAsync(ticket, entry);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>Any SuperAdmin changes a message or note of the support —
    /// a message to the school only while the ticket is open.</summary>
    [HttpPut("api/support-tickets/{id:int}/entries/{entryId:int}")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora upravila zprávu k požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> EditEntry(
        int id, int entryId, EditSupportTicketEntryRequest request)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        return await EditEntryAsync(ticket, entryId, request.Text, bySupport: true);
    }

    /// <summary>Any SuperAdmin deletes a message or note of the support —
    /// the school stops seeing it, the SuperAdmins still see it was there.</summary>
    [HttpDelete("api/support-tickets/{id:int}/entries/{entryId:int}")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora smazala zprávu k požadavku")]
    public async Task<ActionResult<SupportTicketDetailDto>> DeleteEntry(int id, int entryId)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        return await DeleteEntryAsync(ticket, entryId, bySupport: true);
    }

    /// <summary>Any SuperAdmin makes a message of the support an internal
    /// note, or a note a message to the school (only while the ticket is
    /// open; the school's admins hear of it as of a new message).</summary>
    [HttpPost("api/support-tickets/{id:int}/entries/{entryId:int}/internal")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Podpora změnila zprávu na interní poznámku nebo naopak")]
    public async Task<ActionResult<SupportTicketDetailDto>> SetEntryInternal(
        int id, int entryId, SetSupportTicketEntryInternalRequest request)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        return await SetInternalAsync(ticket, entryId, request.Internal);
    }

    // ---- A SuperAdmin in the school (support mode) ----

    /// <summary>The ticket the support session is for, its whole thread
    /// included — to read and write to it without leaving the school.</summary>
    [HttpGet("api/support-sessions/current/ticket")]
    public async Task<ActionResult<SupportTicketDetailDto>> GetSessionTicket()
    {
        var ticket = await SessionTicketAsync();
        return ticket is null ? NotFound() : Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>Like <see cref="AddMessage"/>, from inside the school.</summary>
    [HttpPost("api/support-sessions/current/ticket/messages")]
    [AuditAction("Zpráva podpory k požadavku (ze školy)")]
    public async Task<ActionResult<SupportTicketDetailDto>> AddSessionMessage(SupportTicketMessageRequest request)
    {
        var ticket = await SessionTicketAsync();
        return ticket is null ? NotFound() : await AddMessageAsync(ticket, request);
    }

    /// <summary>Like <see cref="SetEntryInternal"/>, from inside the school.</summary>
    [HttpPost("api/support-sessions/current/ticket/entries/{entryId:int}/internal")]
    [AuditAction("Podpora změnila zprávu na interní poznámku nebo naopak (ze školy)")]
    public async Task<ActionResult<SupportTicketDetailDto>> SetSessionEntryInternal(
        int entryId, SetSupportTicketEntryInternalRequest request)
    {
        var ticket = await SessionTicketAsync();
        return ticket is null ? NotFound() : await SetInternalAsync(ticket, entryId, request.Internal);
    }

    /// <summary>Like <see cref="EditEntry"/>, from inside the school.</summary>
    [HttpPut("api/support-sessions/current/ticket/entries/{entryId:int}")]
    [AuditAction("Podpora upravila zprávu k požadavku (ze školy)")]
    public async Task<ActionResult<SupportTicketDetailDto>> EditSessionEntry(
        int entryId, EditSupportTicketEntryRequest request)
    {
        var ticket = await SessionTicketAsync();
        return ticket is null ? NotFound() : await EditEntryAsync(ticket, entryId, request.Text, bySupport: true);
    }

    /// <summary>Like <see cref="DeleteEntry"/>, from inside the school.</summary>
    [HttpDelete("api/support-sessions/current/ticket/entries/{entryId:int}")]
    [AuditAction("Podpora smazala zprávu k požadavku (ze školy)")]
    public async Task<ActionResult<SupportTicketDetailDto>> DeleteSessionEntry(int entryId)
    {
        var ticket = await SessionTicketAsync();
        return ticket is null ? NotFound() : await DeleteEntryAsync(ticket, entryId, bySupport: true);
    }

    /// <summary>The assignee asks the school's admins to allow entering
    /// (<see cref="SupportAccess.Approval"/>).</summary>
    [HttpPost("api/support-tickets/{id:int}/request-access")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Žádost podpory o vstup do školy")]
    public async Task<ActionResult<SupportTicketDetailDto>> RequestAccess(int id)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (AssigneeOnly(ticket) is { } refused)
        {
            return refused;
        }
        if (ticket.School!.SupportAccess != SupportAccess.Approval)
        {
            return BadRequest(ticket.School.SupportAccess == SupportAccess.Never
                ? "Škola vstup podpory nepovoluje."
                : "Škola povolení nevyžaduje, můžete rovnou vstoupit.");
        }
        var now = clock.GetUtcNow();
        if (ticket.AccessUntil > now)
        {
            return BadRequest("Vstup už je povolený.");
        }
        ticket.AccessRequestedAt = now;
        AddEntry(ticket, SupportTicketEntryKind.AccessRequested, await MeAsync());
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"Podpora žádá o vstup do školy: {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Vstoupit do školy" — the assignee, as far as the school
    /// lets them in; the ticket is being worked on then. Returns the one-time
    /// code for the school's page.</summary>
    [HttpPost("api/support-tickets/{id:int}/enter")]
    [Authorize(Roles = "SuperAdmin")]
    [AuditAction("Zahájen režim podpory")]
    public async Task<ActionResult<SupportSessionStartDto>> Enter(int id)
    {
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (AssigneeOnly(ticket) is { } refused)
        {
            return refused;
        }
        var school = ticket.School!;
        if (school.SupportAccess == SupportAccess.Never)
        {
            return BadRequest("Škola vstup podpory nepovoluje.");
        }
        if (school.SupportAccess == SupportAccess.Approval && !(ticket.AccessUntil > clock.GetUtcNow()))
        {
            return BadRequest("Vstup musí nejdřív povolit škola.");
        }
        var me = await MeAsync();
        var inProgress = ticket.Status == SupportTicketStatus.InProgress
            ? null
            : SetStatus(ticket, SupportTicketStatus.InProgress, me);
        var session = new SupportSession
        {
            SuperAdminUserId = me.Id,
            SuperAdminUsername = me.Username,
            SchoolId = school.Id,
            TicketId = ticket.Id,
        };
        var code = await supportSessions.StartAsync(session);
        if (inProgress is not null)
        {
            // Entering made it — the thread shows it with the visit.
            inProgress.SessionId = session.Id;
            await db.SaveChangesAsync();
        }
        var audit = AuditContext.Of(HttpContext);
        audit.SupportSessionId = session.Id;
        audit.SupportBy = me.Username;
        return Ok(new SupportSessionStartDto(session.Id, code, school.Slug, school.Name, session.ExpiresAt));
    }

    // ---- Helpers ----

    private const string SuperAdminPage = "superadmin/support";

    private Task<SupportTicket?> TicketAsync(int id) =>
        db.SupportTickets.Include(t => t.School).FirstOrDefaultAsync(t => t.Id == id);

    private Task<SupportTicket?> SchoolTicketAsync(int id) =>
        db.SupportTickets.Include(t => t.School)
            .FirstOrDefaultAsync(t => t.Id == id && t.SchoolId == currentSchool.SchoolId);

    private async Task<ActionResult<SupportTicketDetailDto>> SetInternalAsync(
        SupportTicket ticket, int entryId, bool @internal)
    {
        var (entry, refused) = await ChangeableAsync(ticket, entryId, bySupport: true);
        if (entry is null)
        {
            return refused!;
        }
        if (entry.Internal == @internal)
        {
            return Ok(await DetailAsync(ticket, withInternal: true));
        }
        if (!@internal && !ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený — škole už psát nejde.");
        }
        await ReviseAsync(entry, @internal ? SupportTicketEntryChange.MadeInternal : SupportTicketEntryChange.MadePublic);
        await UnnotifyAsync(ticket, entry);
        entry.Internal = @internal;
        entry.Kind = @internal ? SupportTicketEntryKind.Note : SupportTicketEntryKind.Message;
        await db.SaveChangesAsync();
        if (!@internal)
        {
            await NotifyOfAsync(ticket, entry);
        }
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    private async Task<ActionResult<SupportTicketDetailDto>> EditEntryAsync(
        SupportTicket ticket, int entryId, string? text, bool bySupport)
    {
        if (ValidateText(text) is { } invalid)
        {
            return invalid;
        }
        var (entry, refused) = await ChangeableAsync(ticket, entryId, bySupport);
        if (entry is null)
        {
            return refused!;
        }
        if (!entry.Internal && !ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený.");
        }
        var after = text!.Trim();
        if (after != entry.Text)
        {
            await ReviseAsync(entry, SupportTicketEntryChange.Edited);
            await UnnotifyAsync(ticket, entry);
            entry.Text = after;
            await db.SaveChangesAsync();
            await NotifyOfAsync(ticket, entry);
        }
        return Ok(await DetailAsync(ticket, withInternal: bySupport));
    }

    private async Task<ActionResult<SupportTicketDetailDto>> DeleteEntryAsync(
        SupportTicket ticket, int entryId, bool bySupport)
    {
        var (entry, refused) = await ChangeableAsync(ticket, entryId, bySupport);
        if (entry is null)
        {
            return refused!;
        }
        entry.DeletedAt = await ReviseAsync(entry, SupportTicketEntryChange.Deleted);
        await UnnotifyAsync(ticket, entry);
        return Ok(await DetailAsync(ticket, withInternal: bySupport));
    }

    /// <summary>A message or note of the caller's side not deleted yet: of
    /// the support for a SuperAdmin, of the school for its author or whoever
    /// shares a role with them.</summary>
    private async Task<(SupportTicketEntry? Entry, ActionResult? Refused)> ChangeableAsync(
        SupportTicket ticket, int entryId, bool bySupport)
    {
        var entry = await db.SupportTicketEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TicketId == ticket.Id && e.DeletedAt == null
                                      && (e.Kind == SupportTicketEntryKind.Message
                                          || e.Kind == SupportTicketEntryKind.Note));
        if (entry is null || entry.BySupport != bySupport)
        {
            return (null, NotFound());
        }
        if (!bySupport && !await SharesRoleAsync(entry.AuthorUserId))
        {
            return (null, BadRequest("Měnit jde jen vlastní zprávy a zprávy kolegů se stejnou rolí."));
        }
        return (entry, null);
    }

    private async Task<bool> SharesRoleAsync(int? authorId)
    {
        var me = CurrentUserId();
        if (authorId == me)
        {
            return true;
        }
        if (authorId is null)
        {
            return false;
        }
        var mine = await RoleIdsAsync(me);
        return (await RoleIdsAsync(authorId.Value)).Overlaps(mine);
    }

    private async Task<HashSet<int>> RoleIdsAsync(int userId) =>
        (await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Roles.Select(r => r.Id))
            .ToListAsync())
        .ToHashSet();

    /// <summary>Records the change with the text before it; returns its moment.</summary>
    private async Task<DateTimeOffset> ReviseAsync(SupportTicketEntry entry, SupportTicketEntryChange change)
    {
        var me = await MeAsync();
        var now = clock.GetUtcNow();
        db.SupportTicketEntryRevisions.Add(new SupportTicketEntryRevision
        {
            EntryId = entry.Id,
            SchoolId = entry.SchoolId,
            At = now,
            Change = change,
            AuthorUserId = me.Id,
            AuthorName = PersonNames.Display(me),
            Text = entry.Text,
        });
        entry.EditedAt = now;
        return now;
    }

    /// <summary>Null outside support mode, or for a session without a ticket.</summary>
    private async Task<SupportTicket?> SessionTicketAsync()
    {
        if (!int.TryParse(User.FindFirstValue(SupportClaims.SessionId), out var sessionId))
        {
            return null;
        }
        var ticketId = await db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.Id == sessionId).Select(s => s.TicketId).FirstOrDefaultAsync();
        return ticketId is null
            ? null
            : await db.SupportTickets.IgnoreQueryFilters().Include(t => t.School)
                .FirstOrDefaultAsync(t => t.Id == ticketId);
    }

    private Task<User> MeAsync()
    {
        var id = CurrentUserId();
        return db.Users.IgnoreQueryFilters().Include(u => u.Person).FirstAsync(u => u.Id == id);
    }

    /// <summary>Closing, returning or reopening a resolved ticket is up to
    /// its author — or any of the school's admins when the support opened
    /// it or the author is no longer the school's admin.</summary>
    private async Task<bool> MayConfirmAsync(SupportTicket ticket)
    {
        var author = await AuthorAdminAsync(ticket);
        return author is null || author == CurrentUserId();
    }

    /// <summary>The ticket's author while they're the school's admin.</summary>
    private async Task<int?> AuthorAdminAsync(SupportTicket ticket)
    {
        if (ticket.CreatedBySupport || ticket.CreatedByUserId is not { } author)
        {
            return null;
        }
        return await db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Id == author && u.SchoolId == ticket.SchoolId
                           && u.Roles.Any(r => r.BuiltIn == UserRole.Admin))
            ? author
            : null;
    }

    private BadRequestObjectResult AuthorOnly(SupportTicket ticket) =>
        BadRequest($"O vyřešení rozhoduje autor požadavku ({ticket.CreatedByName}).");

    private ActionResult? AssigneeOnly(SupportTicket ticket)
    {
        if (!ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený.");
        }
        return ticket.AssigneeUserId == CurrentUserId()
            ? null
            : BadRequest(ticket.AssigneeUserId is null
                ? "Požadavek nejdřív převezměte."
                : $"Požadavek řeší {ticket.AssigneeName}.");
    }

    private ActionResult? ValidateTicket(string? title, string? description)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return this.FieldError("title", "Nadpis je povinný.");
        }
        if (title.Length > SupportTicket.MaxTitleLength)
        {
            return this.FieldError("title", $"Nejvýš {SupportTicket.MaxTitleLength} znaků.");
        }
        return description?.Trim().Length > SupportTicket.MaxTextLength
            ? this.FieldError("description", $"Nejvýš {SupportTicket.MaxTextLength} znaků.")
            : null;
    }

    private ActionResult? ValidateText(string? text, string field = "text", string required = "Napište zprávu.")
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return this.FieldError(field, required);
        }
        return text.Length > SupportTicket.MaxTextLength
            ? this.FieldError(field, $"Nejvýš {SupportTicket.MaxTextLength} znaků.")
            : null;
    }

    private async Task<SupportTicket> NewTicketAsync(
        School school, User me, string title, string? description, bool bySupport)
    {
        var plan = await plans.GetPlanAsync(school);
        var ticket = new SupportTicket
        {
            SchoolId = school.Id,
            CreatedByUserId = me.Id,
            CreatedByName = PersonNames.Display(me),
            CreatedBySupport = bySupport,
            Title = title.Trim(),
            Description = description?.Trim() ?? "",
            CreatedAt = clock.GetUtcNow(),
            PlanName = plan.Name,
            DueDate = CzechWorkingDays.Add(SchoolClock.Today(clock), plan.ResponseWorkingDays),
            Status = SupportTicketStatus.New,
        };
        db.SupportTickets.Add(ticket);
        AddEntry(ticket, SupportTicketEntryKind.Created, me);
        await db.SaveChangesAsync();
        await db.Entry(ticket).Reference(t => t.School).LoadAsync();
        return ticket;
    }

    /// <summary>Taken over by <paramref name="by"/> itself (from the
    /// assignee <paramref name="from"/>, whom the entry names), or handed
    /// over to another SuperAdmin (the entry names them).</summary>
    private void Assign(SupportTicket ticket, User assignee, User by, string? from = null)
    {
        ticket.AssigneeUserId = assignee.Id;
        ticket.AssigneeName = PersonNames.Display(assignee);
        ticket.Status = SupportTicketStatus.Assigned;
        ticket.StatusChangedAt = clock.GetUtcNow();
        if (assignee == by)
        {
            AddEntry(ticket, SupportTicketEntryKind.Assigned, by, text: from);
        }
        else
        {
            AddEntry(ticket, SupportTicketEntryKind.HandedOver, by, text: ticket.AssigneeName);
        }
    }

    private void AllowAccess(SupportTicket ticket, User me)
    {
        ticket.AccessUntil = clock.GetUtcNow() + options.Value.ApprovalLength;
        ticket.AccessRequestedAt = null;
        AddEntry(ticket, SupportTicketEntryKind.AccessGranted, me, until: ticket.AccessUntil);
    }

    private SupportTicketEntry SetStatus(SupportTicket ticket, SupportTicketStatus status, User me, string? text = null)
    {
        ticket.Status = status;
        ticket.StatusChangedAt = clock.GetUtcNow();
        return AddEntry(ticket, SupportTicketEntryKind.StatusChanged, me, text: text, status: status);
    }

    /// <summary>Closed (at <paramref name="now"/>, the moment of the entry
    /// closing it): no more entering, the running sessions end.</summary>
    private async Task CloseAccessAsync(SupportTicket ticket, DateTimeOffset now)
    {
        if (ticket.AccessUntil > now)
        {
            ticket.AccessUntil = now;
        }
        ticket.AccessRequestedAt = null;
        await EndSessionsAsync(ticket.Id, now);
    }

    /// <summary>Ends the running ones — their tokens stop working at once.</summary>
    private async Task EndSessionsAsync(int ticketId, DateTimeOffset now)
    {
        foreach (var session in await db.SupportSessions
                     .Where(s => s.TicketId == ticketId && s.EndedAt == null && s.ExpiresAt > now)
                     .ToListAsync())
        {
            session.EndedAt = now;
            session.RefreshSecretHash = null;
        }
    }

    private SupportTicketEntry AddEntry(
        SupportTicket ticket, SupportTicketEntryKind kind, User author, string? text = null, bool @internal = false,
        SupportTicketStatus? status = null, DateTimeOffset? until = null) =>
        db.SupportTicketEntries.Add(new SupportTicketEntry
        {
            Ticket = ticket,
            SchoolId = ticket.SchoolId,
            At = clock.GetUtcNow(),
            Kind = kind,
            AuthorUserId = author.Id,
            AuthorName = PersonNames.Display(author),
            BySupport = author.IsSuperAdmin,
            Internal = @internal,
            Text = text,
            Status = status,
            Until = until,
        }).Entity;

    private static string StatusTitle(SupportTicketStatus status) => status switch
    {
        SupportTicketStatus.Assigned => "Požadavek je přiřazený",
        SupportTicketStatus.InProgress => "Požadavek se řeší",
        SupportTicketStatus.WaitingForSchool => "Podpora čeká na vaši odpověď",
        SupportTicketStatus.Resolved => "Požadavek vyřešen",
        SupportTicketStatus.Closed => "Požadavek uzavřen",
        _ => "Změněn stav požadavku",
    };

    private async Task NotifyAssigneeAsync(SupportTicket ticket, string title, string? body)
    {
        if (ticket.AssigneeUserId is { } assignee)
        {
            await notifier.NotifyAsync(
                ticket.SchoolId, [assignee], NotificationType.SupportTicket, title,
                string.IsNullOrWhiteSpace(body) ? null : Notifier.Preview(body), SuperAdminPage, ticket.Id);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>The assignee, or every SuperAdmin while nobody takes care of it.</summary>
    private async Task NotifySupportAsync(SupportTicket ticket, string title, string? body, int? exceptUserId = null)
    {
        List<int> recipients = ticket.AssigneeUserId is { } assignee
            ? [assignee]
            : await db.Users.IgnoreQueryFilters()
                .Where(u => u.IsSuperAdmin && u.SchoolId == null)
                .Select(u => u.Id)
                .ToListAsync();
        await notifier.NotifyAsync(
            ticket.SchoolId, recipients, NotificationType.SupportTicket, title,
            string.IsNullOrWhiteSpace(body) ? null : Notifier.Preview(body), SuperAdminPage, ticket.Id,
            exceptUserId: exceptUserId);
        await db.SaveChangesAsync();
    }

    private static string MessageTitle(SupportTicket ticket) => $"Zpráva od podpory: {ticket.Title}";

    private static string AnswerTitle(SupportTicket ticket) => $"Odpověď školy ({ticket.School!.Name}): {ticket.Title}";

    private static string NoteTitle(SupportTicket ticket) => $"Interní poznámka ({ticket.School!.Name}): {ticket.Title}";

    /// <summary>A message or note as it is now goes to whom it's for: the
    /// school's admins, the support, or — a note — the assignee, unless they
    /// wrote or changed it themselves. Also after a change: unread again.</summary>
    private async Task NotifyOfAsync(SupportTicket ticket, SupportTicketEntry entry)
    {
        var me = CurrentUserId();
        if (entry.Kind == SupportTicketEntryKind.Note)
        {
            if (ticket.AssigneeUserId is { } assignee && assignee != me)
            {
                await notifier.NotifyAsync(
                    ticket.SchoolId, [assignee], NotificationType.SupportTicket, NoteTitle(ticket),
                    Notifier.Preview(entry.Text!), SuperAdminPage, ticket.Id);
                await db.SaveChangesAsync();
            }
        }
        else if (entry.BySupport)
        {
            await NotifySchoolAsync(ticket, MessageTitle(ticket), entry.Text);
        }
        else
        {
            await NotifySupportAsync(ticket, AnswerTitle(ticket), entry.Text);
        }
    }

    /// <summary>The notifications of the entry as it is, read or not, are
    /// gone — unless a newer one about the ticket replaced them. Before the
    /// daily e-mail or a push, those don't go out. Saves.</summary>
    private async Task UnnotifyAsync(SupportTicket ticket, SupportTicketEntry entry)
    {
        var (type, title) = entry.Kind == SupportTicketEntryKind.Note
            ? (NotificationType.SupportTicket, NoteTitle(ticket))
            : entry.BySupport
                ? (NotificationType.SupportTicketUpdate, MessageTitle(ticket))
                : (NotificationType.SupportTicket, AnswerTitle(ticket));
        var body = string.IsNullOrWhiteSpace(entry.Text) ? null : Notifier.Preview(entry.Text);
        var notifications = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.SchoolId == ticket.SchoolId && n.TargetId == ticket.Id && n.Type == type
                        && n.Title == title && n.Body == body)
            .ToListAsync();
        db.Notifications.RemoveRange(notifications);
        await db.SaveChangesAsync();
    }

    private async Task NotifySchoolAsync(SupportTicket ticket, string title, string? body)
    {
        var admins = await db.Users
            .Where(u => u.SchoolId == ticket.SchoolId && u.Roles.Any(r => r.BuiltIn == UserRole.Admin))
            .Select(u => u.Id)
            .ToListAsync();
        await notifier.NotifyAsync(
            ticket.SchoolId, admins, NotificationType.SupportTicketUpdate, title,
            string.IsNullOrWhiteSpace(body) ? null : Notifier.Preview(body), "admin", ticket.Id);
        await db.SaveChangesAsync();
    }

    private async Task<List<SupportTicketDto>> ListAsync(IQueryable<SupportTicket> query)
    {
        var tickets = await query.Include(t => t.School).Take(ListLength).ToListAsync();
        return tickets.Select(t => ToDto(t)).ToList();
    }

    /// <param name="mayConfirm">The caller may close, return or reopen it
    /// (the school's side, <see cref="MayConfirmAsync"/>).</param>
    private SupportTicketDto ToDto(SupportTicket t, bool mayConfirm = false)
    {
        var now = clock.GetUtcNow();
        var open = t.IsOpen;
        var reopenable = t.ReopenableUntil > now ? t.ReopenableUntil : null;
        return new SupportTicketDto(
            t.Id, t.SchoolId, t.School!.Name, t.School.SupportAccess, t.Title, t.Description, t.CreatedAt,
            t.CreatedByName, t.CreatedBySupport, t.PlanName, t.DueDate, t.Status, t.StatusChangedAt,
            t.AssigneeUserId, t.AssigneeName, open && t.DueDate < SchoolClock.Today(clock),
            open ? t.AccessRequestedAt : null, open && t.AccessUntil > now ? t.AccessUntil : null,
            t.AutoClosesAt, reopenable,
            mayConfirm && (t.Status == SupportTicketStatus.Resolved || reopenable is not null));
    }

    /// <summary>The thread with the ticket's support sessions in it: when
    /// each started, what it changed in the school (the audit log), how it
    /// ended — both sides see them.</summary>
    private async Task<SupportTicketDetailDto> DetailAsync(SupportTicket ticket, bool withInternal)
    {
        var me = CurrentUserId();
        var stored = await db.SupportTicketEntries.AsNoTracking()
            .Include(e => e.Revisions)
            .Where(e => e.TicketId == ticket.Id && (withInternal || !e.Internal))
            .ToListAsync();
        // The school's side of a message: its author and whoever shares a role with them.
        var myRoles = withInternal ? [] : await RoleIdsAsync(me);
        var authors = stored.Where(e => !e.BySupport && e.AuthorUserId != null)
            .Select(e => e.AuthorUserId!.Value).Distinct().ToList();
        var authorRoles = withInternal
            ? []
            : await db.Users.IgnoreQueryFilters()
                .Where(u => authors.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Roles.Select(r => r.Id).ToList());
        bool Ours(SupportTicketEntry e) => withInternal
            ? e.BySupport
            : !e.BySupport && e.AuthorUserId is { } author
                && (author == me || authorRoles.GetValueOrDefault(author)?.Any(myRoles.Contains) == true);
        bool SeesChanges(SupportTicketEntry e) => withInternal || Ours(e);
        var entries = stored
            .Where(e => e.DeletedAt == null || SeesChanges(e))
            .Select(e => new SupportTicketEntryDto(
                e.Id, e.At, e.Kind, e.AuthorName, e.BySupport, e.Internal, e.DeletedAt is null ? e.Text : null,
                e.Status, e.Until, e.SessionId,
                CanEdit: e.Kind is SupportTicketEntryKind.Message or SupportTicketEntryKind.Note
                         && e.DeletedAt is null && Ours(e),
                EditedAt: SeesChanges(e) ? e.EditedAt : null,
                Deleted: e.DeletedAt is not null,
                Revisions: SeesChanges(e) && e.Revisions.Count > 0
                    ? e.Revisions.OrderBy(r => r.At).ThenBy(r => r.Id)
                        .Select(r => new SupportTicketEntryRevisionDto(r.At, r.Change, r.AuthorName, r.Text))
                        .ToList()
                    : null))
            .ToList();
        var sessions = await db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.TicketId == ticket.Id)
            .Include(s => s.SuperAdminUser).ThenInclude(u => u!.Person)
            .ToListAsync();
        var names = sessions.ToDictionary(
            s => s.Id, s => s.SuperAdminUser is { } u ? PersonNames.Display(u) : s.SuperAdminUsername);
        var ids = sessions.Select(s => s.Id).ToList();
        var actions = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.SupportSessionId != null && ids.Contains(a.SupportSessionId.Value))
            .Select(a => new
            {
                a.Id, SessionId = a.SupportSessionId!.Value, a.At, a.Method, a.Path, a.StatusCode, a.Area,
                a.Description,
            })
            .ToListAsync();
        actions.RemoveAll(a => SupportController.IsEnteringOrLeaving(a.Path));
        var now = clock.GetUtcNow();
        foreach (var s in sessions)
        {
            var name = names[s.Id];
            entries.Add(new SupportTicketEntryDto(
                0, s.StartedAt, SupportTicketEntryKind.SessionStarted, name, true, false, null, null, null, s.Id,
                LastSeenAt: s.LastSeenAt ?? s.StartedAt));
            var left = entries.Any(e => e.Kind == SupportTicketEntryKind.Visit && e.SessionId == s.Id);
            if (!left && (s.EndedAt is not null || s.ExpiresAt <= now))
            {
                entries.Add(new SupportTicketEntryDto(
                    0, s.EndedAt ?? s.ExpiresAt,
                    s.EndedAt is null ? SupportTicketEntryKind.SessionExpired : SupportTicketEntryKind.SessionEnded,
                    name, true, false, null, null, null, s.Id));
            }
        }
        entries.AddRange(actions.Select(a => new SupportTicketEntryDto(
            0, a.At, SupportTicketEntryKind.SessionAction,
            names[a.SessionId],
            true, false, a.Description ?? $"{a.Area}: změna", null, null, a.SessionId,
            a.StatusCode, withInternal ? $"{a.Method} {a.Path}" : null)));
        var mayConfirm = !withInternal && !InSupportMode()
                         && (ticket.Status == SupportTicketStatus.Cancelled || await MayConfirmAsync(ticket));
        return new SupportTicketDetailDto(ToDto(ticket, mayConfirm), entries
            .OrderBy(e => e.At)
            // At one moment: the visit starts first and ends last.
            .ThenBy(e => e.Kind switch
            {
                SupportTicketEntryKind.SessionStarted => 0,
                SupportTicketEntryKind.Visit or SupportTicketEntryKind.SessionEnded
                    or SupportTicketEntryKind.SessionExpired => 2,
                _ => 1,
            })
            .ThenBy(e => e.Id)
            .ToList());
    }

    private bool InSupportMode() => User.HasClaim(c => c.Type == SupportClaims.SessionId);

    private ObjectResult SupportModeRefused() =>
        StatusCode(StatusCodes.Status403Forbidden, "V režimu podpory požadavky spravuje jen škola.");

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public enum SupportTicketFilter
{
    Open,
    Mine,
    Unassigned,
    All,
}

/// <param name="AllowAccess">Lets the support in for this ticket right away
/// (with <see cref="SupportAccess.Approval"/>).</param>
public record CreateSchoolTicketRequest(string? Title, string? Description, bool AllowAccess = false);

public record CreateSupportTicketRequest(int SchoolId, string? Title, string? Description);

/// <param name="Internal">A SuperAdmins' note the school doesn't see.</param>
public record SupportTicketMessageRequest(string? Text, bool Internal = false);

public record ReleaseSupportTicketRequest(string? Summary);

public record SetSupportTicketStatusRequest(SupportTicketStatus Status);

/// <param name="UserId">Another SuperAdmin to hand it over to; none = me.</param>
/// <param name="TakeOverFrom">The assignee to take it from — refused when
/// someone else takes care of it by now.</param>
public record AssignSupportTicketRequest(int? UserId = null, int? TakeOverFrom = null);

public record CancelSupportTicketRequest(string? Reason);

/// <param name="Text">Why it isn't resolved — the school's goes to the thread
/// as a message (required), a SuperAdmin's as an internal note (optional).</param>
public record ReturnSupportTicketRequest(string? Text);

/// <param name="SchoolAccess">The school's policy (<see cref="School.SupportAccess"/>).</param>
/// <param name="Overdue">Still open after <paramref name="DueDate"/>.</param>
/// <param name="AccessRequestedAt">Open and the assignee waits for the school to allow entering.</param>
/// <param name="AccessUntil">Open and entering allowed until then; null otherwise.</param>
/// <param name="AutoClosesAt">Resolved: closes by itself then.</param>
/// <param name="ReopenableUntil">Closed or cancelled and may still be reopened till then.</param>
/// <param name="CanConfirm">In the ticket's detail for the school: the caller
/// may close or return it (resolved), or reopen it.</param>
public record SupportTicketDto(
    int Id, int SchoolId, string SchoolName, SupportAccess SchoolAccess, string Title, string Description,
    DateTimeOffset CreatedAt, string CreatedByName, bool CreatedBySupport, string PlanName, DateOnly DueDate,
    SupportTicketStatus Status, DateTimeOffset? StatusChangedAt, int? AssigneeUserId, string? AssigneeName,
    bool Overdue, DateTimeOffset? AccessRequestedAt, DateTimeOffset? AccessUntil,
    DateTimeOffset? AutoClosesAt = null, DateTimeOffset? ReopenableUntil = null, bool CanConfirm = false);

/// <param name="Id">0 for the support sessions' entries, which aren't stored.</param>
/// <param name="Text">With <see cref="SupportTicketEntryKind.SessionAction"/>
/// what was changed, in words (the audit log's description, else its agenda).</param>
/// <param name="StatusCode">With <see cref="SupportTicketEntryKind.SessionAction"/>.</param>
/// <param name="Request">With <see cref="SupportTicketEntryKind.SessionAction"/>
/// "METHOD /path" — only for the SuperAdmins.</param>
/// <param name="LastSeenAt">With <see cref="SupportTicketEntryKind.SessionStarted"/>:
/// the session's last request (<see cref="SupportSession.LastSeenAt"/>).</param>
/// <param name="Text">Null when deleted (<paramref name="Revisions"/> have it).</param>
/// <param name="CanEdit">A message or note of the caller's side — they may
/// change or delete it (a SuperAdmin also retype it).</param>
/// <param name="EditedAt">Changed afterwards; only for the entry's side and
/// the SuperAdmins, like <paramref name="Deleted"/> ones and <paramref name="Revisions"/>.</param>
public record SupportTicketEntryDto(
    int Id, DateTimeOffset At, SupportTicketEntryKind Kind, string AuthorName, bool BySupport, bool Internal,
    string? Text, SupportTicketStatus? Status, DateTimeOffset? Until, int? SessionId, int? StatusCode = null,
    string? Request = null, DateTimeOffset? LastSeenAt = null, bool CanEdit = false,
    DateTimeOffset? EditedAt = null, bool Deleted = false,
    IReadOnlyList<SupportTicketEntryRevisionDto>? Revisions = null);

/// <param name="Text">The entry's text before the change.</param>
public record SupportTicketEntryRevisionDto(
    DateTimeOffset At, SupportTicketEntryChange Change, string AuthorName, string? Text);

public record EditSupportTicketEntryRequest(string? Text);

/// <param name="Internal">A message becomes an internal note, or the other way round.</param>
public record SetSupportTicketEntryInternalRequest(bool Internal);

public record SupportTicketDetailDto(SupportTicketDto Ticket, IReadOnlyList<SupportTicketEntryDto> Entries);
