using Sova.Api.Evidence;

namespace Sova.Api.Auth;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }

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
    /// password via <see cref="MyProfileController.ChangePassword"/>. Forces
    /// a redirect to Settings on every login until then (see
    /// <c>auth.guard.ts</c>). Fallback for proper invite-by-email onboarding,
    /// which needs mail infrastructure the project doesn't have yet.</summary>
    public bool MustChangePassword { get; set; }
}
