using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Live;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>The school's support tickets, kept in the backoffice
/// (<see cref="IBackofficeTickets"/>): the school's admins ask, answer, close,
/// decide the support's estimates. Who may do what the instance decides; the
/// thread gets what happened in the school — the support's sessions, what they
/// changed and their reset — from here.</summary>
[Authorize]
[ApiController]
[AuditArea("Podpora")]
public class SupportTicketsController(
    AppDbContext db, ICurrentSchoolContext currentSchool, IBackofficeTickets tickets, SupportPlanClient plans,
    SupportReset reset, LivePublisher live, TimeProvider clock)
    : ControllerBase
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 8000;

    private int SchoolId => currentSchool.SchoolId!.Value;

    /// <summary>The school's tickets: open ones by due date, then the rest newest first.</summary>
    [HttpGet("api/my-school/support-tickets")]
    [RequirePermission(Permission.Administration)]
    public Task<ActionResult<IReadOnlyList<SupportTicketSummaryDto>>> GetSchoolTickets() =>
        Backoffice<IReadOnlyList<SupportTicketSummaryDto>>(async () => (await tickets.ListAsync(SchoolId))
            .Select(t => new SupportTicketSummaryDto(
                t.Id, t.Title, t.Status, t.CreatedAt, t.DueDate, t.Overdue, t.CreatedByName, t.CreatedBySupport,
                t.AssigneeName, t.LastActivityAt))
            .ToList());

    /// <summary>The school's support plan, for the sidebar.</summary>
    [HttpGet("api/my-school/support-plan")]
    [RequirePermission(Permission.Administration)]
    public async Task<SupportPlanDto> GetSchoolPlan() =>
        await plans.GetPlanAsync(await db.Schools.FirstAsync(s => s.Id == SchoolId));

    /// <summary>The school's support time in a month (<c>yyyy-MM</c>, none = this one).</summary>
    [HttpGet("api/my-school/support-usage")]
    [RequirePermission(Permission.Administration)]
    public Task<ActionResult<JsonElement>> GetSchoolUsage(string? month = null) =>
        Backoffice(() => tickets.UsageAsync(SchoolId, month));

    [HttpGet("api/my-school/support-tickets/{id:int}")]
    [RequirePermission(Permission.Administration)]
    public Task<ActionResult<SupportTicketDetailDto>> GetSchoolTicket(int id) =>
        Backoffice(async () => await DetailAsync(await tickets.GetAsync(SchoolId, id)));

    /// <summary>Due by the school's plan. With <see cref="CreateSchoolTicketRequest.EstimateRequested"/>
    /// the support sends an estimate before any work.</summary>
    [HttpPost("api/my-school/support-tickets")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Vytvořen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CreateSchoolTicket(CreateSchoolTicketRequest request)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return this.FieldError("title", "Nadpis je povinný.");
        }
        if (title.Length > MaxTitleLength)
        {
            return this.FieldError("title", $"Nejvýš {MaxTitleLength} znaků.");
        }
        if (request.Description?.Trim().Length > MaxTextLength)
        {
            return this.FieldError("description", $"Nejvýš {MaxTextLength} znaků.");
        }
        return await Backoffice(async () => await DetailAsync(await tickets.CreateAsync(
            SchoolId, await MeAsync(), title, request.Description?.Trim(), request.EstimateRequested)));
    }

    /// <summary>An answer to the support.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/messages")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Odpověď k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> AddSchoolMessage(int id, SupportTicketMessageRequest request)
    {
        if ((Refused() ?? ValidateText(request.Text)) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, "messages", new { text = request.Text!.Trim() });
    }

    /// <summary>A message of the school changed — by its author or whoever
    /// shares a role with them.</summary>
    [HttpPut("api/my-school/support-tickets/{id:int}/entries/{entryId:int}")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Upravena zpráva k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> EditSchoolEntry(
        int id, int entryId, EditSupportTicketEntryRequest request)
    {
        if ((Refused() ?? ValidateText(request.Text)) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Put, $"entries/{entryId}", new { text = request.Text!.Trim() },
            ticket => MayChangeAsync(ticket, entryId));
    }

    [HttpDelete("api/my-school/support-tickets/{id:int}/entries/{entryId:int}")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Smazána zpráva k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> DeleteSchoolEntry(int id, int entryId)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Delete, $"entries/{entryId}", null, ticket => MayChangeAsync(ticket, entryId));
    }

    /// <summary>The school withdraws a ticket not closed yet, saying why; the
    /// support's sessions for it end. With <see cref="CancelSupportTicketRequest.ResetChanges"/>
    /// the support's changes are reset first (<see cref="MayConfirmAsync"/>).</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/cancel")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Zrušen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CancelTicket(int id, CancelSupportTicketRequest request)
    {
        if ((Refused() ?? ValidateText(request.Reason, "reason", "Napište, proč požadavek rušíte.")) is { } invalid)
        {
            return invalid;
        }
        var reason = request.Reason!.Trim();
        return await ActAsync(id, HttpMethod.Post, "cancel", new { reason }, async ticket =>
        {
            if (!IsOpen(ticket.Status))
            {
                return BadRequest("Požadavek už je uzavřený.");
            }
            if (request.ResetChanges && !await MayConfirmAsync(ticket))
            {
                return ResetAuthorOnly(ticket);
            }
            var me = await MeAsync();
            if (request.ResetChanges)
            {
                await ResetAsync(ticket.Id, null, me, null, endSessions: false);
            }
            await EndSessionsAsync(ticket.Id, me, WithReason("Škola požadavek zrušila.", reason));
            return await SaveResetAsync();
        });
    }

    /// <summary>"Uzavřít" — the school confirms the ticket resolved (<see cref="MayConfirmAsync"/>).</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/close")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Uzavřen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> CloseTicket(int id)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, "close", null, ConfirmingAsync);
    }

    /// <summary>"Vrátit k řešení" — not resolved after all, with a message saying why.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/return")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Požadavek na podporu vrácen k řešení")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReturnTicket(int id, ReturnSupportTicketRequest request)
    {
        if ((Refused() ?? ValidateText(request.Text, "text", "Napište, proč požadavek vracíte k řešení.")) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, "return", new { text = request.Text!.Trim() }, ConfirmingAsync);
    }

    /// <summary>"Znovu otevřít" a closed or cancelled ticket — a mistake. A
    /// closed one only whoever may close it.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/reopen")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Znovu otevřen požadavek na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> ReopenTicket(int id)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, "reopen", null, async ticket =>
            ticket.Status == SupportTicketStatus.Cancelled ? null : await ConfirmingAsync(ticket));
    }

    /// <summary>The school wants an estimate before any work.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/request-estimate")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Vyžádán odhad práce k požadavku na podporu")]
    public async Task<ActionResult<SupportTicketDetailDto>> RequestEstimate(int id)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, "request-estimate");
    }

    /// <summary>Approves the support's estimate of work over the plan or of a paid service — the school's admin only.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/estimates/{estimateId:int}/approve")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Schválen odhad práce podpory")]
    public async Task<ActionResult<SupportTicketDetailDto>> ApproveEstimate(
        int id, int estimateId, EstimateDecisionRequest? request)
    {
        if ((Refused() ?? await AdminOnlyAsync()) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, $"estimates/{estimateId}/approve", new { note = request?.Note?.Trim() });
    }

    [HttpPost("api/my-school/support-tickets/{id:int}/estimates/{estimateId:int}/reject")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Odmítnut odhad práce podpory")]
    public async Task<ActionResult<SupportTicketDetailDto>> RejectEstimate(
        int id, int estimateId, EstimateDecisionRequest? request)
    {
        if ((Refused() ?? await AdminOnlyAsync()) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, $"estimates/{estimateId}/reject", new { note = request?.Note?.Trim() });
    }

    /// <summary>The school disagrees with the category of logged support time, saying why.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/time/{timeId:int}/dispute")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Rozporována kategorie času podpory")]
    public async Task<ActionResult<SupportTicketDetailDto>> DisputeTime(int id, int timeId, DisputeTimeRequest request)
    {
        if ((Refused() ?? ValidateText(request.Reason, "reason", "Napište, proč s kategorií nesouhlasíte.")) is { } refused)
        {
            return refused;
        }
        return await ActAsync(id, HttpMethod.Post, $"time/{timeId}/dispute",
            new { category = request.Category, reason = request.Reason!.Trim() });
    }

    /// <summary>What resetting the support's changes would do — of the
    /// whole ticket, or of one of its sessions.</summary>
    [HttpGet("api/my-school/support-tickets/{id:int}/reset")]
    [RequirePermission(Permission.Administration)]
    public async Task<ActionResult<SupportResetPreviewDto>> PreviewSchoolReset(int id, int? sessionId = null)
    {
        if (Refused() is { } refused)
        {
            return refused;
        }
        try
        {
            var ticket = await tickets.GetAsync(SchoolId, id);
            return await ResettableAsync(ticket, sessionId) ?? (ActionResult)Ok(await reset.PreviewAsync(ticket.Id, sessionId));
        }
        catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
        {
            return Failed(e);
        }
    }

    /// <summary>"Resetovat" — returns the support's changes, saying why; the
    /// thread says how it went and the support's running sessions end.</summary>
    [HttpPost("api/my-school/support-tickets/{id:int}/reset")]
    [RequirePermission(Permission.Administration)]
    [AuditAction("Resetovány změny podpory")]
    public async Task<ActionResult<SupportTicketDetailDto>> ResetSchoolTicket(int id, ResetSupportChangesRequest request)
    {
        if ((Refused() ?? ValidateText(request.Reason, "reason", "Napište, proč změny podpory resetujete.")) is { } invalid)
        {
            return invalid;
        }
        try
        {
            var ticket = await tickets.GetAsync(SchoolId, id);
            if (await ResettableAsync(ticket, request.SessionId) is { } refused)
            {
                return refused;
            }
            await ResetAsync(ticket.Id, request.SessionId, await MeAsync(), request.Reason!.Trim(), endSessions: true);
            if (await SaveResetAsync() is { } failed)
            {
                return failed;
            }
            await live.TicketsAsync(SchoolId, [ticket.Id]);
            return Ok(await DetailAsync(ticket));
        }
        catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
        {
            return Failed(e);
        }
    }

    /// <summary>Checks with <paramref name="check"/> (a refusal, or null to go
    /// on) on the ticket as the backoffice has it, then has the backoffice do it.</summary>
    private async Task<ActionResult<SupportTicketDetailDto>> ActAsync(
        int id, HttpMethod method, string action, object? body = null,
        Func<BackofficeTicket, Task<ActionResult?>>? check = null)
    {
        try
        {
            if (check is not null && await check(await tickets.GetAsync(SchoolId, id)) is { } refused)
            {
                return refused;
            }
            var ticket = await tickets.ActAsync(SchoolId, id, await MeAsync(), method, action, body);
            await live.TicketsAsync(SchoolId, [ticket.Id]);
            return Ok(await DetailAsync(ticket));
        }
        catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
        {
            return Failed(e);
        }
    }

    private async Task<ActionResult<T>> Backoffice<T>(Func<Task<T>> call)
    {
        try
        {
            return Ok(await call());
        }
        catch (Exception e) when (e is TicketRefusedException or SupportUnavailableException)
        {
            return Failed(e);
        }
    }

    public const string Unavailable = "Podpora je teď nedostupná, zkuste to později.";
    public const string NotConnected = "Tato instance podporu nemá.";

    private static ObjectResult Failed(Exception e) => e switch
    {
        TicketRefusedException refused => Problem(refused.Status, refused.Message),
        SupportUnavailableException { Connected: false } => Problem(StatusCodes.Status404NotFound, NotConnected),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, Unavailable),
    };

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Detail = detail }) { StatusCode = status };

    /// <summary>The support in the school doesn't handle the school's tickets.</summary>
    private ObjectResult? Refused() => User.InSupportMode()
        ? StatusCode(StatusCodes.Status403Forbidden, "V režimu podpory požadavky spravuje jen škola.")
        : null;

    private async Task<ActionResult?> AdminOnlyAsync() =>
        await IsAdminAsync(CurrentUserId())
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, "O odhadu práce rozhoduje admin školy.");

    private async Task<ActionResult?> ConfirmingAsync(BackofficeTicket ticket) =>
        await MayConfirmAsync(ticket) ? null : BadRequest($"O vyřešení rozhoduje autor požadavku ({ticket.CreatedByName}).");

    private async Task<ActionResult?> MayChangeAsync(BackofficeTicket ticket, int entryId)
    {
        var entry = ticket.Entries.FirstOrDefault(e => e.Id == entryId && !e.BySupport && !e.Deleted
                                                      && e.Kind == Kinds.Message);
        if (entry is null)
        {
            return NotFound();
        }
        return await SharesRoleAsync(entry.AuthorUserId)
            ? null
            : BadRequest("Měnit jde jen vlastní zprávy a zprávy kolegů se stejnou rolí.");
    }

    private ActionResult? ValidateText(string? text, string field = "text", string required = "Napište zprávu.")
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return this.FieldError(field, required);
        }
        return text.Length > MaxTextLength ? this.FieldError(field, $"Nejvýš {MaxTextLength} znaků.") : null;
    }

    private static bool IsOpen(SupportTicketStatus status) =>
        status is not (SupportTicketStatus.Resolved or SupportTicketStatus.Cancelled or SupportTicketStatus.Closed);

    private static bool IsResettable(SupportTicketStatus status) =>
        status is not (SupportTicketStatus.Cancelled or SupportTicketStatus.Closed);

    /// <summary>Closing, returning or reopening a resolved ticket is up to
    /// its author — or any of the school's admins when the support opened
    /// it or the author is no longer the school's admin.</summary>
    private async Task<bool> MayConfirmAsync(BackofficeTicket ticket)
    {
        if (ticket.CreatedBySupport || ticket.CreatedByUserId is not { } author || !await IsAdminAsync(author))
        {
            return true;
        }
        return author == CurrentUserId();
    }

    private Task<bool> IsAdminAsync(int userId) =>
        db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId && u.SchoolId == SchoolId
                                                    && u.Roles.Any(r => r.BuiltIn == UserRole.Admin));

    private async Task<bool> SharesRoleAsync(int? authorId)
    {
        var me = CurrentUserId();
        if (authorId == me)
        {
            return true;
        }
        return authorId is { } author && (await RoleIdsAsync([author])).GetValueOrDefault(author, []).Overlaps(
            (await RoleIdsAsync([me])).GetValueOrDefault(me, []));
    }

    private async Task<Dictionary<int, HashSet<int>>> RoleIdsAsync(IReadOnlyCollection<int> userIds) =>
        await db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id) && u.SchoolId == SchoolId)
            .ToDictionaryAsync(u => u.Id, u => u.Roles.Select(r => r.Id).ToHashSet());

    private async Task<ActionResult?> ResettableAsync(BackofficeTicket ticket, int? sessionId)
    {
        if (sessionId is not null && !await db.SupportSessions.IgnoreQueryFilters()
                .AnyAsync(s => s.Id == sessionId && s.TicketId == ticket.Id && s.SchoolId == SchoolId))
        {
            return NotFound();
        }
        if (!IsResettable(ticket.Status))
        {
            return BadRequest("Požadavek je uzavřený — změny podpory už resetovat nejde.");
        }
        return await MayConfirmAsync(ticket) ? null : ResetAuthorOnly(ticket);
    }

    private BadRequestObjectResult ResetAuthorOnly(BackofficeTicket ticket) =>
        BadRequest($"Změny podpory může resetovat autor požadavku ({ticket.CreatedByName}).");

    /// <summary>Returns the changes and says in the thread how it went (and
    /// why); with <paramref name="endSessions"/> the running sessions end —
    /// they'd go on with what's gone. The caller saves (<see cref="SaveResetAsync"/>).</summary>
    private async Task ResetAsync(int ticketId, int? sessionId, TicketActor me, string? reason, bool endSessions)
    {
        var result = await reset.ApplyAsync(ticketId, sessionId);
        var now = clock.GetUtcNow();
        db.SupportTicketEntries.Add(new SupportTicketEntry
        {
            TicketId = ticketId,
            SchoolId = SchoolId,
            At = now,
            Kind = SupportTicketEntryKind.Reset,
            AuthorName = me.Name,
            Text = string.IsNullOrEmpty(reason) ? ResetSummary(result) : $"**Důvod:** {reason}\n\n{ResetSummary(result)}",
            SessionId = sessionId,
        });
        if (endSessions)
        {
            await EndSessionsAsync(ticketId, me, WithReason("Změny podpory byly resetovány.", reason));
        }
    }

    /// <summary>Null when saved; a row changed under the reset fails it all.</summary>
    private async Task<ActionResult?> SaveResetAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Conflict("Reset se nepodařil, data se mezitím změnila. Zkuste to znovu.");
        }
    }

    private const int ResetSummaryLines = 30;

    private static string ResetSummary(SupportResetPreviewDto result)
    {
        var skipped = result.Items.Where(i => i.Skipped is not null).ToList();
        var text = $"Vráceno záznamů: {result.Items.Count - skipped.Count}.";
        if (skipped.Count > 0)
        {
            text += $"\n\nNevráceno ({skipped.Count}):\n"
                    + string.Join("\n", skipped.Take(ResetSummaryLines).Select(i => $"- {i.Type} {i.Record}: {i.Skipped}"))
                    + (skipped.Count > ResetSummaryLines ? "\n- …" : "");
        }
        return text;
    }

    /// <summary>Ends the running ones — their tokens stop working at once;
    /// the support inside learns from whom and why.</summary>
    private async Task EndSessionsAsync(int ticketId, TicketActor by, string reason)
    {
        var now = clock.GetUtcNow();
        foreach (var session in await db.SupportSessions
                     .Where(s => s.TicketId == ticketId && s.SchoolId == SchoolId && s.EndedAt == null && s.ExpiresAt > now)
                     .ToListAsync())
        {
            session.EndedAt = now;
            session.RefreshSecretHash = null;
            session.EndedBy = by.Name;
            session.EndReason = reason;
        }
    }

    private static string WithReason(string what, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? what : $"{what}\n\n**Důvod:** {reason.Trim()}";

    private async Task<TicketActor> MeAsync()
    {
        var id = CurrentUserId();
        var me = await db.Users.IgnoreQueryFilters().Include(u => u.Person).FirstAsync(u => u.Id == id);
        return new TicketActor(me.Id, PersonNames.Display(me));
    }

    private int CurrentUserId() => User.ActorId()!.Value;

    /// <summary>The backoffice's kinds the school's thread shows.</summary>
    public static class Kinds
    {
        public const string Message = "Message";
    }

    /// <summary>The backoffice's thread with what happened in the school for
    /// the ticket: the support's sessions, what they changed (the audit log),
    /// how they ended, and the resets.</summary>
    private async Task<SupportTicketDetailDto> DetailAsync(BackofficeTicket ticket)
    {
        var me = CurrentUserId();
        var authors = ticket.Entries.Where(e => !e.BySupport && e.AuthorUserId is not null)
            .Select(e => e.AuthorUserId!.Value).Append(me).Distinct().ToList();
        var roles = await RoleIdsAsync(authors);
        var myRoles = roles.GetValueOrDefault(me, []);
        bool Ours(BackofficeTicketEntry e) =>
            !e.BySupport && e.AuthorUserId is { } author
                         && (author == me || roles.GetValueOrDefault(author)?.Overlaps(myRoles) == true);
        var entries = ticket.Entries
            .Select(e => new SupportTicketEntryDto(
                e.Id, e.At, e.Kind, e.AuthorName, e.BySupport, e.Text, e.Status, e.Until, null,
                CanEdit: e.Kind == Kinds.Message && !e.Deleted && Ours(e),
                EditedAt: Ours(e) ? e.EditedAt : null,
                Deleted: e.Deleted))
            .ToList();

        var local = await db.SupportTicketEntries.AsNoTracking()
            .Where(e => e.TicketId == ticket.Id && e.SchoolId == SchoolId)
            .ToListAsync();
        entries.AddRange(local.Select(e => new SupportTicketEntryDto(
            0, e.At, e.Kind.ToString(), e.AuthorName, e.Kind == SupportTicketEntryKind.Visit, e.Text, null, null,
            e.SessionId)));

        var sessions = await db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.TicketId == ticket.Id && s.SchoolId == SchoolId)
            .ToListAsync();
        var ids = sessions.Select(s => s.Id).ToList();
        var actions = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.SupportSessionId != null && ids.Contains(a.SupportSessionId.Value))
            .Select(a => new
            {
                SessionId = a.SupportSessionId!.Value, a.At, a.Path, a.StatusCode, a.Area, a.Description, a.AsUserName,
            })
            .ToListAsync();
        actions.RemoveAll(a => SupportController.IsEnteringOrLeaving(a.Path));
        var now = clock.GetUtcNow();
        var mayReset = IsResettable(ticket.Status) && await MayConfirmAsync(ticket);
        var changes = mayReset ? await reset.CountsAsync(ticket.Id) : [];
        foreach (var s in sessions)
        {
            entries.Add(new SupportTicketEntryDto(
                0, s.StartedAt, nameof(SupportTicketEntryKind.SessionStarted), s.SuperAdminUsername, true, null, null, null,
                s.Id, InactiveSince: s.EndedAt is null && s.ExpiresAt > now ? s.DisconnectedAt : null,
                Changes: changes.GetValueOrDefault(s.Id)));
            var left = local.Any(e => e.Kind == SupportTicketEntryKind.Visit && e.SessionId == s.Id);
            if (!left && (s.EndedAt is not null || s.ExpiresAt <= now))
            {
                entries.Add(new SupportTicketEntryDto(
                    0, s.EndedAt ?? s.ExpiresAt,
                    (s.EndedAt is null ? SupportTicketEntryKind.SessionExpired : SupportTicketEntryKind.SessionEnded).ToString(),
                    s.SuperAdminUsername, true, null, null, null, s.Id));
            }
        }
        var names = sessions.ToDictionary(s => s.Id, s => s.SuperAdminUsername);
        entries.AddRange(actions.Select(a => new SupportTicketEntryDto(
            0, a.At, nameof(SupportTicketEntryKind.SessionAction), names[a.SessionId], true,
            a.Description ?? $"{a.Area}: změna", null, null, a.SessionId, a.StatusCode, As: a.AsUserName)));

        var reopenable = ticket.ReopenableUntil > now ? ticket.ReopenableUntil : null;
        var mayConfirm = ticket.Status == SupportTicketStatus.Cancelled || await MayConfirmAsync(ticket);
        return new SupportTicketDetailDto(
            new SupportTicketDto(
                ticket.Id, ticket.Title, ticket.Description, ticket.Status, ticket.CreatedAt, ticket.StatusChangedAt,
                ticket.CreatedByName, ticket.CreatedBySupport, ticket.PlanName, ticket.DueDate, ticket.Overdue,
                ticket.AssigneeName, ticket.AutoClosesAt, reopenable,
                CanConfirm: mayConfirm && (ticket.Status == SupportTicketStatus.Resolved || reopenable is not null),
                ResettableChanges: changes.Values.Sum(),
                CanDecideEstimates: await IsAdminAsync(me)),
            entries
                .OrderBy(e => e.At)
                // At one moment: the visit starts first and ends last.
                .ThenBy(e => e.Kind switch
                {
                    nameof(SupportTicketEntryKind.SessionStarted) => 0,
                    nameof(SupportTicketEntryKind.Visit) or nameof(SupportTicketEntryKind.SessionEnded)
                        or nameof(SupportTicketEntryKind.SessionExpired) => 2,
                    _ => 1,
                })
                .ThenBy(e => e.Id)
                .ToList(),
            ticket.Time);
    }
}

/// <param name="EstimateRequested">The support sends an estimate before any work.</param>
public record CreateSchoolTicketRequest(string? Title, string? Description, bool EstimateRequested = false);

public record SupportTicketMessageRequest(string? Text);

/// <param name="ResetChanges">Reset the support's changes first.</param>
public record CancelSupportTicketRequest(string? Reason, bool ResetChanges = false);

/// <param name="SessionId">Only this session's changes; none = the whole ticket's.</param>
public record ResetSupportChangesRequest(int? SessionId = null, string? Reason = null);

/// <param name="Text">Why it isn't resolved — goes to the thread as a message.</param>
public record ReturnSupportTicketRequest(string? Text);

public record EditSupportTicketEntryRequest(string? Text);

public record EstimateDecisionRequest(string? Note);

/// <param name="Category">What the school thinks it is: <c>Bug</c>, <c>Support</c> or <c>Service</c>.</param>
public record DisputeTimeRequest(string? Category, string? Reason);

public record SupportTicketSummaryDto(
    int Id, string Title, SupportTicketStatus Status, DateTimeOffset CreatedAt, DateOnly DueDate, bool Overdue,
    string CreatedByName, bool CreatedBySupport, string? AssigneeName, DateTimeOffset LastActivityAt);

/// <param name="Overdue">Still open after <paramref name="DueDate"/>.</param>
/// <param name="AutoClosesAt">Resolved: closes by itself then.</param>
/// <param name="ReopenableUntil">Closed or cancelled and may still be reopened till then.</param>
/// <param name="CanConfirm">The caller may close or return it (resolved), or reopen it.</param>
/// <param name="ResettableChanges">The support's changes not reset yet, when the caller may reset them; 0 otherwise.</param>
/// <param name="CanDecideEstimates">The caller is the school's admin.</param>
public record SupportTicketDto(
    int Id, string Title, string Description, SupportTicketStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StatusChangedAt, string CreatedByName, bool CreatedBySupport, string PlanName, DateOnly DueDate,
    bool Overdue, string? AssigneeName, DateTimeOffset? AutoClosesAt, DateTimeOffset? ReopenableUntil,
    bool CanConfirm, int ResettableChanges, bool CanDecideEstimates);

/// <param name="Id">The backoffice's; 0 for what the school's instance adds.</param>
/// <param name="Kind">The backoffice's (<c>Message</c>, <c>StatusChanged</c>, <c>EstimateProposed</c>…)
/// or the school's (<see cref="SupportTicketEntryKind"/>).</param>
/// <param name="Text">With <see cref="SupportTicketEntryKind.SessionAction"/> what was changed, in
/// words; null when deleted.</param>
/// <param name="StatusCode">With <see cref="SupportTicketEntryKind.SessionAction"/>.</param>
/// <param name="InactiveSince">With <see cref="SupportTicketEntryKind.SessionStarted"/> while it
/// runs: no tab of it open since then.</param>
/// <param name="CanEdit">A message of the caller's side they may change or delete.</param>
/// <param name="Changes">With <see cref="SupportTicketEntryKind.SessionStarted"/>: the session's
/// changes not reset yet, when the caller may reset them.</param>
/// <param name="As">With <see cref="SupportTicketEntryKind.SessionAction"/> made as one of the
/// school's users: who ("učitel Jan Novák").</param>
/// <param name="EditedAt">Changed afterwards; only for the entry's side.</param>
public record SupportTicketEntryDto(
    int Id, DateTimeOffset At, string Kind, string AuthorName, bool BySupport, string? Text,
    SupportTicketStatus? Status, DateTimeOffset? Until, int? SessionId, int? StatusCode = null,
    DateTimeOffset? InactiveSince = null, bool CanEdit = false, DateTimeOffset? EditedAt = null, bool Deleted = false,
    int Changes = 0, string? As = null);

/// <param name="Time">The backoffice's: the ticket's support time, estimates and the school's month.</param>
public record SupportTicketDetailDto(SupportTicketDto Ticket, IReadOnlyList<SupportTicketEntryDto> Entries, JsonElement Time);
