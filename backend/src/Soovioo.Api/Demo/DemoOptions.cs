namespace Soovioo.Api.Demo;

/// <summary>Bound from the <c>Demo</c> config section.</summary>
public class DemoOptions
{
    /// <summary>How long a demo lives before the daily job deletes it.</summary>
    public int LifetimeDays { get; set; } = 30;

    /// <summary>Demos existing at once; creating more is refused (with a
    /// contact) until old ones expire. Generation and storage aren't free.</summary>
    public int MaxActive { get; set; } = 200;

    /// <summary>New demos (and resets — both regenerate everything) per IP
    /// address and day.</summary>
    public int CreatePerIpPerDay { get; set; } = 5;

    /// <summary>Where demo feedback (and the support link) goes.</summary>
    public string FeedbackEmail { get; set; } = "info@soovioo.cz";
}
