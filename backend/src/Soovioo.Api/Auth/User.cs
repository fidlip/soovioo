using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    /// <summary>Manages schools themselves — no school, no <see cref="Roles"/>.</summary>
    public bool IsSuperAdmin { get; set; }

    private Role? activeRole;

    /// <summary>The <em>active</em> role — the one the JWT carries
    /// (<see cref="PermissionAccess"/> checks its permissions). Always one of
    /// <see cref="Roles"/> (setting it adds it there). Persisted, so the last
    /// choice made in the role switcher survives a new login. Null only for
    /// SuperAdmin.</summary>
    public Role? ActiveRole
    {
        get => activeRole;
        set
        {
            activeRole = value;
            if (value is not null && !Roles.Contains(value))
            {
                Roles.Add(value);
            }
        }
    }

    public int? ActiveRoleId { get; set; }

    /// <summary>Every role the account may act as (e.g. a teacher who is also
    /// a parent at the same school), always loaded with the user. Query
    /// "users with role X" against this, never against <see cref="ActiveRole"/>.</summary>
    public List<Role> Roles { get; set; } = [];

    public bool HasKind(RoleKind kind) => Roles.Any(r => r.Kind == kind);

    public bool HasBuiltIn(UserRole builtIn) => Roles.Any(r => r.BuiltIn == builtIn);

    /// <summary>Null only for SuperAdmin — every other role belongs to one School.</summary>
    public int? SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>Name and contacts of the human behind the account (null only
    /// for SuperAdmin) — see <see cref="People.Person"/>.</summary>
    public People.Person? Person { get; set; }

    /// <summary>Secret used to authenticate the unauthenticated ICS feed
    /// endpoint (<see cref="Events.CalendarFeedController"/>) — calendar
    /// apps subscribing by URL can't send a Bearer token. Lazily generated
    /// on first request, not at account creation, so existing rows stay
    /// null until a user actually asks for their feed link.</summary>
    public string? CalendarToken { get; set; }

    /// <summary>Set whenever an Admin assigns a password on this account
    /// (creation or reset) — cleared only when the user picks their own
    /// password via <see cref="MyProfileController.ChangePassword"/> (or via
    /// <see cref="InvitationToken"/> below). Forces a redirect to Settings on
    /// every login until then (see <c>auth.guard.ts</c>). Fallback path for
    /// an account that was never invited by e-mail, or whose invite expired
    /// before being used.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>One-time secret an Admin's "Poslat pozvánku" action e-mails
    /// to the user — see <see cref="UsersController"/>'s invite endpoint and
    /// <see cref="AuthController.AcceptInvite"/>. Null until an invite is
    /// sent; cleared again once the recipient picks their own password (or
    /// an Admin re-sends a fresh one, replacing the old link). Separate from
    /// <see cref="CalendarToken"/>'s hex format only by convention — both
    /// are opaque random secrets, never parsed as anything else.</summary>
    public string? InvitationToken { get; set; }

    /// <summary>Null exactly when <see cref="InvitationToken"/> is — kept as
    /// two nullable columns rather than a non-nullable default so a stale
    /// expiry can never outlive its token by accident.</summary>
    public DateTimeOffset? InvitationTokenExpiresAt { get; set; }

    /// <summary>Set = the account can't log in (left the school — a former
    /// pupil, a parent without pupils at the school, a member of staff who
    /// left). Its data stays until <see cref="ErasedAt"/>.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    /// <summary>Set = the person's data was erased and the account
    /// anonymized (<see cref="Retention.AccountEraser"/>). The row stays —
    /// events, payments and messages it created still point at it — but it's
    /// no longer anyone and is left out of every list.</summary>
    public DateTimeOffset? ErasedAt { get; set; }

    /// <summary>Staff: the last day at the school (Ukončení pracovního
    /// poměru) — the account works through it and is deactivated the day
    /// after (<see cref="Retention.StaffLeaving"/>).</summary>
    public DateOnly? LeftAt { get; set; }

    /// <summary>For the school's management only.</summary>
    public Retention.StaffLeaveReason? LeaveReason { get; set; }
    public string? LeaveNote { get; set; }

    /// <summary>Pupils' nicknames shown to this teacher (their children's to
    /// a parent) instead of plain names; null = staff by the school's
    /// <see cref="School.StaffSeeStudentNicknames"/>, parents yes.</summary>
    public bool? ShowStudentNicknames { get; set; }

    /// <summary>Kinds of notifications the user switched off in Nastavení —
    /// empty = all of them on.</summary>
    public List<Notifications.NotificationType> MutedNotifications { get; set; } = [];

    /// <summary>Kinds left out of the daily e-mail — the ones the school
    /// forces (<see cref="School.ForcedEmailNotifications"/>) come anyway.</summary>
    public List<Notifications.NotificationType> MutedEmailNotifications { get; set; } = [];

    /// <summary>When the daily e-mail goes out (Prague time); null = the
    /// school's <see cref="School.DigestTime"/>.</summary>
    public TimeOnly? DigestTime { get; set; }

    /// <summary>The last daily e-mail's turn — the next one has what came
    /// after it.</summary>
    public DateTimeOffset? DigestSentAt { get; set; }

    /// <summary>Kinds not sent to the phone as a push.</summary>
    public List<Notifications.NotificationType> MutedPushNotifications { get; set; } = [];

    /// <summary>When the user agreed that a push carries the whole text
    /// (it passes Google's and Apple's servers); null = only the kind of
    /// event. Takes effect only while the school allows it
    /// (<see cref="School.PushFullText"/>).</summary>
    public DateTimeOffset? PushFullTextAt { get; set; }

    /// <summary>Own night quiet for push (Prague time); both null = the
    /// school's <see cref="School.PushQuietFrom"/>–<see cref="School.PushQuietTo"/>.</summary>
    public TimeOnly? PushQuietFrom { get; set; }
    public TimeOnly? PushQuietTo { get; set; }

    /// <summary>Push comes at night too.</summary>
    public bool PushQuietOff { get; set; }

    /// <summary>Secret of the "odhlásit" link in the daily e-mail (works
    /// without logging in) — generated with the first e-mail.</summary>
    public string? EmailUnsubscribeToken { get; set; }
}
