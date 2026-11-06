using Soovioo.Api.Auth;

namespace Soovioo.Api.Notifications;

public enum PushPlatform
{
    Android,
    Ios,
}

/// <summary>A phone with the mobile app logged in as this user — where
/// <see cref="PushJob"/> sends the push, this user's and that of the accounts
/// linked to it (<see cref="AccountLink"/>). One user can have several; the
/// app registers it after logging in and removes it when logging out.</summary>
public class PushDevice
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The FCM registration token of the app on that phone.</summary>
    public required string Token { get; set; }

    public PushPlatform Platform { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last time the app registered it again.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
