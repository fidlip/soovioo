namespace Soovioo.Api.Auth;

/// <summary>Long-lived credential that buys fresh 15-minute access JWTs via
/// <see cref="AuthController.Refresh"/>. Only a SHA-256 hash of the secret
/// is stored — a DB leak doesn't hand out live sessions. Rotated on every
/// use: each refresh revokes the presented token and issues a successor in
/// the same <see cref="FamilyId"/> (one family = one login). Presenting an
/// already-rotated token outside the short grace window means it was copied,
/// so <see cref="RefreshTokenService"/> revokes the whole family.
/// Deliberately not tenant-filtered: it's always looked up by hash before
/// any school context exists.</summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }

    /// <summary>"Zůstat přihlášen" — persistent 30-day cookie (sliding with
    /// every rotation). False = browser-session cookie whose family can't
    /// outlive 8 hours from login, rotations included.</summary>
    public bool Persistent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Set (together with <see cref="RevokedAt"/>) only when this
    /// token was revoked by being rotated — distinguishes "superseded a
    /// moment ago by a concurrent tab" from logout/password-change
    /// revocation.</summary>
    public int? ReplacedByTokenId { get; set; }
}
