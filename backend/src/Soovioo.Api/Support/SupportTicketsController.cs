using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>Support tickets (<see cref="SupportTicket"/>) and their threads:
/// the school's admins ask, answer and allow entering; one SuperAdmin takes
/// care of a ticket — changes its state, asks to enter and enters — the
/// others can write to it.</summary>
[Authorize]
[ApiController]
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
        var ticket = await NewTicketAsync(school.Id, me, request.Title!, request.Description, bySupport: false);
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
            return BadRequest("Požadavek už je uzavřený.");
        }
        var me = await MeAsync();
        AddEntry(ticket, SupportTicketEntryKind.Message, me, text: request.Text!.Trim());
        if (ticket.Status == SupportTicketStatus.WaitingForSchool)
        {
            SetStatus(ticket, SupportTicketStatus.Assigned, me);
        }
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, $"Odpověď školy ({ticket.School!.Name}): {ticket.Title}", request.Text);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>The school withdraws a ticket not closed yet; entering for
    /// it ends.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/cancel")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SupportTicketDetailDto>> CancelTicket(int id)
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
        var me = await MeAsync();
        SetStatus(ticket, SupportTicketStatus.Cancelled, me);
        await CloseAccessAsync(ticket);
        await db.SaveChangesAsync();
        await NotifySupportAsync(ticket, $"Škola zrušila požadavek ({ticket.School!.Name}): {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: false));
    }

    /// <summary>Lets the support in for this ticket for
    /// <see cref="SupportOptions.ApprovalMinutes"/> (again).</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/grant-access")]
    [RequirePermission(Permission.Administration)]
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
        AddEntry(ticket, SupportTicketEntryKind.AccessRevoked, me);
        await EndSessionsAsync(ticket.Id, now);
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
        query = query.Where(t => t.Status != SupportTicketStatus.Resolved && t.Status != SupportTicketStatus.Cancelled);
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
        var ticket = await NewTicketAsync(school.Id, me, request.Title!, request.Description, bySupport: true);
        Assign(ticket, me);
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"Podpora založila požadavek: {ticket.Title}", ticket.Description);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Převzít" — only a ticket nobody takes care of.</summary>
    [HttpPost("api/support-tickets/{id:int}/assign")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<SupportTicketDetailDto>> AssignTicket(int id)
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
        if (ticket.AssigneeUserId is not null)
        {
            return BadRequest($"Požadavek už řeší {ticket.AssigneeName}.");
        }
        Assign(ticket, await MeAsync());
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"Požadavek převzal(a) {ticket.AssigneeName}: {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>"Přerušit práci" — back to the pool with a summary of the
    /// work so far (internal); the other SuperAdmins hear of it.</summary>
    [HttpPost("api/support-tickets/{id:int}/release")]
    [Authorize(Roles = "SuperAdmin")]
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
        SetStatus(ticket, request.Status, await MeAsync());
        if (request.Status == SupportTicketStatus.Resolved)
        {
            await CloseAccessAsync(ticket);
        }
        await db.SaveChangesAsync();
        await NotifySchoolAsync(ticket, $"{StatusTitle(request.Status)}: {ticket.Title}", null);
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>Any SuperAdmin: a message to the school (its admins hear of
    /// it) or an internal note (the assignee hears of it). Notes also on a
    /// closed ticket.</summary>
    [HttpPost("api/support-tickets/{id:int}/messages")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<SupportTicketDetailDto>> AddMessage(int id, SupportTicketMessageRequest request)
    {
        if (ValidateText(request.Text) is { } invalid)
        {
            return invalid;
        }
        var ticket = await TicketAsync(id);
        if (ticket is null)
        {
            return NotFound();
        }
        AuditContext.Of(HttpContext).SchoolId = ticket.SchoolId;
        if (!request.Internal && !ticket.IsOpen)
        {
            return BadRequest("Požadavek už je uzavřený — zbývá interní poznámka.");
        }
        var me = await MeAsync();
        var text = request.Text!.Trim();
        AddEntry(ticket, request.Internal ? SupportTicketEntryKind.Note : SupportTicketEntryKind.Message, me,
            text: text, @internal: request.Internal);
        await db.SaveChangesAsync();
        if (!request.Internal)
        {
            await NotifySchoolAsync(ticket, $"Zpráva od podpory: {ticket.Title}", text);
        }
        else if (ticket.AssigneeUserId is { } assignee && assignee != me.Id)
        {
            await notifier.NotifyAsync(
                ticket.SchoolId, [assignee], NotificationType.SupportTicket,
                $"Interní poznámka ({ticket.School!.Name}): {ticket.Title}", Notifier.Preview(text), SuperAdminPage,
                ticket.Id);
            await db.SaveChangesAsync();
        }
        return Ok(await DetailAsync(ticket, withInternal: true));
    }

    /// <summary>The assignee asks the school's admins to allow entering
    /// (<see cref="SupportAccess.Approval"/>).</summary>
    [HttpPost("api/support-tickets/{id:int}/request-access")]
    [Authorize(Roles = "SuperAdmin")]
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
        if (ticket.Status != SupportTicketStatus.InProgress)
        {
            SetStatus(ticket, SupportTicketStatus.InProgress, me);
        }
        var session = new SupportSession
        {
            SuperAdminUserId = me.Id,
            SuperAdminUsername = me.Username,
            SchoolId = school.Id,
            TicketId = ticket.Id,
        };
        var code = await supportSessions.StartAsync(session);
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

    private Task<User> MeAsync()
    {
        var id = CurrentUserId();
        return db.Users.IgnoreQueryFilters().Include(u => u.Person).FirstAsync(u => u.Id == id);
    }

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
        int schoolId, User me, string title, string? description, bool bySupport)
    {
        var plan = await plans.GetPlanAsync(schoolId);
        var ticket = new SupportTicket
        {
            SchoolId = schoolId,
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

    private void Assign(SupportTicket ticket, User me)
    {
        ticket.AssigneeUserId = me.Id;
        ticket.AssigneeName = PersonNames.Display(me);
        ticket.Status = SupportTicketStatus.Assigned;
        ticket.StatusChangedAt = clock.GetUtcNow();
        AddEntry(ticket, SupportTicketEntryKind.Assigned, me);
    }

    private void AllowAccess(SupportTicket ticket, User me)
    {
        ticket.AccessUntil = clock.GetUtcNow() + options.Value.ApprovalLength;
        ticket.AccessRequestedAt = null;
        AddEntry(ticket, SupportTicketEntryKind.AccessGranted, me, until: ticket.AccessUntil);
    }

    private void SetStatus(SupportTicket ticket, SupportTicketStatus status, User me)
    {
        ticket.Status = status;
        ticket.StatusChangedAt = clock.GetUtcNow();
        AddEntry(ticket, SupportTicketEntryKind.StatusChanged, me, status: status);
    }

    /// <summary>Closed: no more entering, the running sessions end.</summary>
    private async Task CloseAccessAsync(SupportTicket ticket)
    {
        var now = clock.GetUtcNow();
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

    private void AddEntry(
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
        });

    private static string StatusTitle(SupportTicketStatus status) => status switch
    {
        SupportTicketStatus.Assigned => "Požadavek je přiřazený",
        SupportTicketStatus.InProgress => "Požadavek se řeší",
        SupportTicketStatus.WaitingForSchool => "Podpora čeká na vaši odpověď",
        SupportTicketStatus.Resolved => "Požadavek vyřešen",
        _ => "Změněn stav požadavku",
    };

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
        return tickets.Select(ToDto).ToList();
    }

    private SupportTicketDto ToDto(SupportTicket t)
    {
        var now = clock.GetUtcNow();
        var open = t.IsOpen;
        return new SupportTicketDto(
            t.Id, t.SchoolId, t.School!.Name, t.School.SupportAccess, t.Title, t.Description, t.CreatedAt,
            t.CreatedByName, t.CreatedBySupport, t.PlanName, t.DueDate, t.Status, t.StatusChangedAt,
            t.AssigneeUserId, t.AssigneeName, open && t.DueDate < SchoolClock.Today(clock),
            open ? t.AccessRequestedAt : null, open && t.AccessUntil > now ? t.AccessUntil : null);
    }

    private async Task<SupportTicketDetailDto> DetailAsync(SupportTicket ticket, bool withInternal)
    {
        var entries = await db.SupportTicketEntries
            .Where(e => e.TicketId == ticket.Id && (withInternal || !e.Internal))
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new SupportTicketEntryDto(
                e.Id, e.At, e.Kind, e.AuthorName, e.BySupport, e.Internal, e.Text, e.Status, e.Until))
            .ToListAsync();
        return new SupportTicketDetailDto(ToDto(ticket), entries);
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

/// <param name="SchoolAccess">The school's policy (<see cref="School.SupportAccess"/>).</param>
/// <param name="Overdue">Still open after <paramref name="DueDate"/>.</param>
/// <param name="AccessRequestedAt">Open and the assignee waits for the school to allow entering.</param>
/// <param name="AccessUntil">Open and entering allowed until then; null otherwise.</param>
public record SupportTicketDto(
    int Id, int SchoolId, string SchoolName, SupportAccess SchoolAccess, string Title, string Description,
    DateTimeOffset CreatedAt, string CreatedByName, bool CreatedBySupport, string PlanName, DateOnly DueDate,
    SupportTicketStatus Status, DateTimeOffset? StatusChangedAt, int? AssigneeUserId, string? AssigneeName,
    bool Overdue, DateTimeOffset? AccessRequestedAt, DateTimeOffset? AccessUntil);

public record SupportTicketEntryDto(
    int Id, DateTimeOffset At, SupportTicketEntryKind Kind, string AuthorName, bool BySupport, bool Internal,
    string? Text, SupportTicketStatus? Status, DateTimeOffset? Until);

public record SupportTicketDetailDto(SupportTicketDto Ticket, IReadOnlyList<SupportTicketEntryDto> Entries);
