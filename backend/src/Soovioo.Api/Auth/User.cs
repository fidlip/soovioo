using Soovioo.Api.Evidence;

namespace Soovioo.Api.Auth;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    private UserRole role;

    /// <summary>The <em>active</em> role — the one the JWT's role claim
    /// carries, so every <c>[Authorize(Roles = …)]</c>/<c>IsInRole</c> check
    /// works against it unchanged. Always one of <see cref="Roles"/> (setting
    /// it adds it there). Persisted, so the last choice made in the role
    /// switcher survives a new login.</summary>
    public UserRole Role
    {
        get => role;
        set
        {
            role = value;
            if (!Roles.Contains(value))
            {
                Roles.Add(value);
            }
        }
    }

    /// <summary>Every role the account may act as (e.g. a teacher who is also
    /// a parent at the same school). Query "users with role X" against this,
    /// never against <see cref="Role"/>.</summary>
    public List<UserRole> Roles { get; set; } = [];

    /// <summary>Null only for SuperAdmin — every other role belongs to one School.</summary>
    public int? SchoolId { get; set; }
    public School? School { get; set; }

    // Staff/contact info — real schools track how to reach a teacher or
    // admin directly, separate from their login username.
    public string? Phone { get; set; }
    public string? Email { get; set; }

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
}
