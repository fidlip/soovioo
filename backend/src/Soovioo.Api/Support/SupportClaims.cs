namespace Soovioo.Api.Support;

public static class SupportClaims
{
    /// <summary>Id of the <see cref="SupportSession"/> the token belongs to.</summary>
    public const string SessionId = "supportSessionId";

    /// <summary>Username of the SuperAdmin behind a support-mode token.</summary>
    public const string By = "supportBy";

    /// <summary>The school's display name, for the "Režim podpory" banner.</summary>
    public const string SchoolName = "supportSchool";
}
