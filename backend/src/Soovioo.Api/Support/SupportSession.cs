using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

/// <summary>A SuperAdmin entering a school in support mode ("Vstoupit do
/// školy") — who, where and when, plus every data-changing request made
/// meanwhile (<see cref="Actions"/>). Not tenant-scoped: only SuperAdmins
/// ever read it.</summary>
public class SupportSession
{
    public int Id { get; set; }

    public int? SuperAdminUserId { get; set; }
    public User? SuperAdminUser { get; set; }

    /// <summary>Snapshot, survives the account's deletion.</summary>
    public required string SuperAdminUsername { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set by "Opustit"; null = left by letting the token expire.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public List<SupportAction> Actions { get; set; } = [];
}

/// <summary>One non-GET request made in a support session.</summary>
public class SupportAction
{
    public int Id { get; set; }

    public int SupportSessionId { get; set; }
    public SupportSession? SupportSession { get; set; }

    public required string Method { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
