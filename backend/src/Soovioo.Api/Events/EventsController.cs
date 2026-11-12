using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Audit;
using Soovioo.Api.Auth;
using Soovioo.Api.Common;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Events;

[Authorize]
[ApiController]
[Route("api/events")]
[AuditArea("Kalendář")]
public partial class EventsController(AppDbContext db, ICurrentSchoolContext currentSchool, Notifier notifier) : ControllerBase
{
    /// <summary>Visibility depends on the caller's role and the event's
    /// <see cref="EventAudience"/> — see <see cref="EventVisibilityExtensions"/>
    /// for the exact rules (Admin: everything in the school; Teacher: own
    /// creations + Everyone + anything touching their classes/students;
    /// Parent: Everyone + anything touching their own children; Student:
    /// Everyone + anything touching themselves/their own class).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetEvents(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        if (currentSchool.SchoolId is null)
        {
            // SuperAdmin isn't tied to a school — cross-school events aren't
            // in scope yet (see ROADMAP), so there's nothing to show them.
            return Ok(Enumerable.Empty<EventDto>());
        }
        var schoolId = currentSchool.SchoolId.Value;

        var query = await User.ScopeAsync(db, Permission.EventsWrite) == PermissionScope.School
            ? db.VisibleToAdmin(schoolId)
            : User.ActiveKind() switch
            {
                RoleKind.Staff => await db.VisibleToTeacherAsync(schoolId, CurrentUserId()),
                RoleKind.Student => await db.VisibleToStudentAsync(schoolId, CurrentUserId()),
                _ => await db.VisibleToParentAsync(schoolId, CurrentUserId()),
            };

        if (from is not null)
        {
            query = query.Where(e => e.EndAt >= from);
        }
        if (to is not null)
        {
            query = query.Where(e => e.StartAt <= to);
        }

        var events = await query
            .Include(e => e.SchoolClasses)
            .Include(e => e.Teachers)
            .Include(e => e.Parents)
            .Include(e => e.Students)
            .OrderBy(e => e.StartAt)
            .ToListAsync();

        return Ok(events.Select(ToDto));
    }

    /// <summary>Drives the recipient picker in the create/edit dialog — only
    /// what the caller is actually allowed to target (Teacher: just their
    /// own classes/students/students' parents; Admin: everything in the
    /// school). The exact same sets are what <see cref="ValidateRecipientsAsync"/>
    /// checks submitted ids against, so the UI can never offer something the
    /// write endpoint would then reject.</summary>
    [HttpGet("recipient-options")]
    [RequirePermission(Permission.EventsWrite)]
    public async Task<ActionResult<RecipientOptionsDto>> GetRecipientOptions()
    {
        var schoolId = currentSchool.SchoolId!.Value;

        var myClassIds = await User.ClassIdsAsync(db, Permission.EventsWrite);
        if (myClassIds is null)
        {
            var classes = await db.SchoolClasses
                .OrderBy(c => c.Name)
                .Select(c => new SchoolClassOptionDto(c.Id, c.Name, c.Rocnik))
                .ToListAsync();
            var teachers = await db.Users
                .Where(u => u.Roles.Any(r => r.BuiltIn == UserRole.Teacher))
                .OrderBy(u => u.Username)
                .Select(u => new PersonOptionDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
                .ToListAsync();
            var parents = await db.Users
                .Where(u => u.Roles.Any(r => r.Kind == RoleKind.Parent))
                .OrderBy(u => u.Username)
                .Select(u => new PersonOptionDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
                .ToListAsync();
            var students = await db.Students
                .OrderBy(s => s.Person!.LastName)
                .Select(s => new StudentOptionDto(s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, s.SchoolClass!.Name, s.SchoolClass!.Rocnik))
                .ToListAsync();
            return Ok(new RecipientOptionsDto(classes, teachers, parents, await LabelledAsync(students)));
        }

        var myClasses = await db.SchoolClasses
            .Where(c => myClassIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new SchoolClassOptionDto(c.Id, c.Name, c.Rocnik))
            .ToListAsync();
        var myStudentIds = await User.ClassStudentIdsAsync(db, Permission.EventsWrite) ?? [];
        var myStudents = await db.Students
            .Where(s => myStudentIds.Contains(s.Id))
            .OrderBy(s => s.Person!.LastName)
            .Select(s => new StudentOptionDto(s.Id, s.Person!.FirstName, s.Person!.LastName, s.SchoolClassId, s.SchoolClass!.Name, s.SchoolClass!.Rocnik))
            .ToListAsync();
        var myParents = await db.ParentStudents
            .Where(ps => myStudentIds.Contains(ps.StudentId) && ps.ParentProfile!.Person!.UserId != null)
            .Select(ps => ps.ParentProfile!.Person!.User!)
            .Distinct()
            .OrderBy(u => u.Username)
            .Select(u => new PersonOptionDto(u.Id, u.Username, PersonNames.Display(u.Person, u.Username)))
            .ToListAsync();

        return Ok(new RecipientOptionsDto(myClasses, [], myParents, await LabelledAsync(myStudents)));
    }

    private Task<List<StudentOptionDto>> LabelledAsync(List<StudentOptionDto> students) =>
        db.WithStudentLabelsAsync(User, students, s => s.Id, (s, label) => s with { Label = label });

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorPattern();

    [HttpPost]
    [RequirePermission(Permission.EventsWrite)]
    [AuditAction("Vytvořena akce")]
    public async Task<ActionResult<EventDto>> CreateEvent(CreateEventRequest request)
    {
        var validationError = await ValidateEventRequestAsync(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var @event = new Event
        {
            SchoolId = currentSchool.SchoolId!.Value,
            Title = request.Title,
            Description = request.Description,
            Location = request.Location,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            AllDay = request.AllDay,
            Color = request.Color,
            Audience = request.Audience,
            CreatedByUserId = CurrentUserId(),
        };
        SetRecipients(@event, request);

        db.Events.Add(@event);
        await db.SaveChangesAsync();
        await NotifyAudienceAsync(@event, "Nová akce");
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEvents), new { }, ToDto(@event));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.EventsWrite)]
    [AuditAction("Upravena akce")]
    public async Task<ActionResult<EventDto>> UpdateEvent(int id, CreateEventRequest request)
    {
        var validationError = await ValidateEventRequestAsync(request);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var @event = await db.Events
            .Include(e => e.SchoolClasses)
            .Include(e => e.Teachers)
            .Include(e => e.Parents)
            .Include(e => e.Students)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (@event is null)
        {
            return NotFound();
        }
        if (!await MayChangeAsync(@event))
        {
            return Forbid();
        }

        var before = Snapshot(@event);
        @event.Title = request.Title;
        @event.Description = request.Description;
        @event.Location = request.Location;
        @event.StartAt = request.StartAt;
        @event.EndAt = request.EndAt;
        @event.AllDay = request.AllDay;
        @event.Color = request.Color;
        @event.Audience = request.Audience;
        SetRecipients(@event, request);
        if (Snapshot(@event) != before)
        {
            var audience = await db.AudienceUserIdsAsync(@event);
            await notifier.ChangedAsync(@event.SchoolId, @event.Id, [NotificationType.Event], [.. audience, CurrentUserId()],
                Drafts(@event, audience, "Změna akce"), Title(@event, "Nová akce"));
        }
        await db.SaveChangesAsync();

        return Ok(ToDto(@event));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permission.EventsWrite)]
    [AuditAction("Smazána akce")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (@event is null)
        {
            return NotFound();
        }
        if (!await MayChangeAsync(@event))
        {
            return Forbid();
        }

        db.Events.Remove(@event);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Null means valid. Checked regardless of Audience so a
    /// request that sends recipient ids alongside Everyone/OnlyMe at least
    /// can't silently smuggle in nonsense — though <see cref="SetRecipients"/>
    /// only ever persists them for Selected.</summary>
    private async Task<string?> ValidateEventRequestAsync(CreateEventRequest request)
    {
        if (request.EndAt < request.StartAt)
        {
            return "EndAt must not be before StartAt.";
        }
        if (request.Color is not null && !HexColorPattern().IsMatch(request.Color))
        {
            return "Barva musí být ve formátu #rrggbb.";
        }
        if (request.Audience == EventAudience.Everyone
            && await User.ScopeAsync(db, Permission.EventsWrite) != PermissionScope.School)
        {
            return "Akci pro celou školu může vytvořit jen ten, kdo má akce pro celou školu.";
        }
        if (request.Audience != EventAudience.Selected)
        {
            return null;
        }

        var allowed = await GetAllowedRecipientIdsAsync();
        if ((request.SchoolClassIds ?? []).Any(x => !allowed.ClassIds.Contains(x)))
        {
            return "One or more schoolClassIds are outside what you may target.";
        }
        if ((request.TeacherUserIds ?? []).Any(x => !allowed.TeacherUserIds.Contains(x)))
        {
            return "One or more teacherUserIds are outside what you may target.";
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

    /// <summary>The same sets <see cref="GetRecipientOptions"/> exposes to
    /// the frontend, as plain id sets for membership checks on write.</summary>
    private async Task<AllowedRecipientIds> GetAllowedRecipientIdsAsync()
    {
        var schoolId = currentSchool.SchoolId!.Value;

        var myClassIds = await User.ClassIdsAsync(db, Permission.EventsWrite);
        if (myClassIds is null)
        {
            var classIds = await db.SchoolClasses.Where(c => c.SchoolId == schoolId).Select(c => c.Id).ToListAsync();
            var teacherIds = await db.Users
                .Where(u => u.SchoolId == schoolId && u.Roles.Any(r => r.BuiltIn == UserRole.Teacher)).Select(u => u.Id).ToListAsync();
            var parentIds = await db.Users
                .Where(u => u.SchoolId == schoolId && u.Roles.Any(r => r.Kind == RoleKind.Parent)).Select(u => u.Id).ToListAsync();
            var studentIds = await db.Students.Where(s => s.SchoolId == schoolId).Select(s => s.Id).ToListAsync();
            return new AllowedRecipientIds(
                classIds.ToHashSet(), teacherIds.ToHashSet(), parentIds.ToHashSet(), studentIds.ToHashSet());
        }

        var myStudentIds = await User.ClassStudentIdsAsync(db, Permission.EventsWrite) ?? [];
        var myParentIds = await db.ParentStudents
            .Where(ps => myStudentIds.Contains(ps.StudentId)).ParentAccountLinks().Select(l => l.UserId).Distinct().ToListAsync();
        // Teacher may never target other teachers — empty set makes any
        // submitted teacherUserIds fail the subset check automatically.
        return new AllowedRecipientIds(myClassIds, [], myParentIds.ToHashSet(), myStudentIds);
    }

    private void SetRecipients(Event @event, CreateEventRequest request)
    {
        @event.SchoolClasses.Clear();
        @event.Teachers.Clear();
        @event.Parents.Clear();
        @event.Students.Clear();
        if (request.Audience != EventAudience.Selected)
        {
            return;
        }

        var schoolId = currentSchool.SchoolId!.Value;
        foreach (var id in request.SchoolClassIds ?? [])
        {
            @event.SchoolClasses.Add(new EventSchoolClass { SchoolClassId = id, SchoolId = schoolId });
        }
        foreach (var id in request.TeacherUserIds ?? [])
        {
            @event.Teachers.Add(new EventTeacher { UserId = id, SchoolId = schoolId });
        }
        foreach (var id in request.ParentUserIds ?? [])
        {
            @event.Parents.Add(new EventParent { UserId = id, SchoolId = schoolId });
        }
        foreach (var id in request.StudentIds ?? [])
        {
            @event.Students.Add(new EventStudent { StudentId = id, SchoolId = schoolId });
        }
    }

    private async Task NotifyAudienceAsync(Event @event, string what) =>
        await notifier.AddAsync(@event.SchoolId, Drafts(@event, await db.AudienceUserIdsAsync(@event), what));

    /// <summary>To the audience but whoever made the change.</summary>
    private List<NotificationDraft> Drafts(Event @event, IEnumerable<int> audience, string what)
    {
        var when = @event.AllDay ? SchoolFormat.Date(@event.StartAt) : SchoolFormat.DateTime(@event.StartAt);
        return audience
            .Where(userId => userId != CurrentUserId())
            .Select(userId => new NotificationDraft(
                userId, NotificationType.Event, Title(@event, what),
                string.IsNullOrWhiteSpace(@event.Location) ? when : $"{when}, {@event.Location}", "events", @event.Id))
            .ToList();
    }

    private static string Title(Event @event, string what) => $"{what}: {@event.Title}";

    private static string Snapshot(Event e) => string.Join("|",
        e.Title, e.Description, e.Location, e.StartAt.ToString("O"), e.EndAt.ToString("O"), e.AllDay, e.Color, e.Audience,
        string.Join(",", e.SchoolClasses.Select(c => c.SchoolClassId).Order()),
        string.Join(",", e.Teachers.Select(t => t.UserId).Order()),
        string.Join(",", e.Parents.Select(p => p.UserId).Order()),
        string.Join(",", e.Students.Select(x => x.StudentId).Order()));

    /// <summary>Any event with the whole school in reach, otherwise one's own.</summary>
    private async Task<bool> MayChangeAsync(Event @event) =>
        @event.CreatedByUserId == CurrentUserId()
        || await User.ScopeAsync(db, Permission.EventsWrite) == PermissionScope.School;

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static EventDto ToDto(Event e) => new(
        e.Id, e.Title, e.Description, e.Location, e.StartAt, e.EndAt, e.AllDay, e.Color, e.Audience,
        e.SchoolClasses.Select(c => c.SchoolClassId).ToList(),
        e.Teachers.Select(t => t.UserId).ToList(),
        e.Parents.Select(p => p.UserId).ToList(),
        e.Students.Select(s => s.StudentId).ToList());

    private record AllowedRecipientIds(
        HashSet<int> ClassIds, HashSet<int> TeacherUserIds, HashSet<int> ParentUserIds, HashSet<int> StudentIds);
}

public record CreateEventRequest(
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool AllDay,
    EventAudience Audience,
    string? Color = null,
    IReadOnlyList<int>? SchoolClassIds = null,
    IReadOnlyList<int>? TeacherUserIds = null,
    IReadOnlyList<int>? ParentUserIds = null,
    IReadOnlyList<int>? StudentIds = null);

public record EventDto(
    int Id,
    string Title,
    string? Description,
    string? Location,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool AllDay,
    string? Color,
    EventAudience Audience,
    IReadOnlyList<int> SchoolClassIds,
    IReadOnlyList<int> TeacherUserIds,
    IReadOnlyList<int> ParentUserIds,
    IReadOnlyList<int> StudentIds);

public record RecipientOptionsDto(
    IEnumerable<SchoolClassOptionDto> Classes,
    IEnumerable<PersonOptionDto> Teachers,
    IEnumerable<PersonOptionDto> Parents,
    IEnumerable<StudentOptionDto> Students);

public record SchoolClassOptionDto(int Id, string Name, int? Rocnik);
public record PersonOptionDto(int Id, string Username, string Name);
public record StudentOptionDto(
    int Id, string FirstName, string LastName, int SchoolClassId, string SchoolClassName, int? Rocnik, NameLabel? Label = null);
