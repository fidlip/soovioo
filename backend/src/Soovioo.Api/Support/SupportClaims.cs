using System.Security.Claims;

namespace Soovioo.Api.Support;

public static class SupportClaims
{
    /// <summary>Id of the <see cref="SupportSession"/> the token belongs to.</summary>
    public const string SessionId = "supportSessionId";

    /// <summary>Username of the SuperAdmin behind a support-mode token.</summary>
    public const string By = "supportBy";

    /// <summary>The school's display name, for the "Režim podpory" banner.</summary>
    public const string SchoolName = "supportSchool";

    /// <summary>Id of the SuperAdmin behind a support-mode token — the
    /// token's own user id is the school's user they entered as, if any.</summary>
    public const string SuperAdminId = "supportUserId";

    /// <summary>Entered as one of the school's users ("Vstoupit jako…"): their id.</summary>
    public const string AsUserId = "supportAsUserId";

    /// <summary>…and who that is, for the banner ("učitel Jan Novák").</summary>
    public const string As = "supportAs";

    public static bool InSupportMode(this ClaimsPrincipal user) => user.HasClaim(c => c.Type == SessionId);

    /// <summary>In support mode as one of the school's users, not as its admin.</summary>
    public static bool InSupportAsUser(this ClaimsPrincipal user) => user.HasClaim(c => c.Type == AsUserId);

    /// <summary>Who really makes the request: the SuperAdmin in support
    /// mode, else the token's user.</summary>
    public static int? ActorId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(SuperAdminId) ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
