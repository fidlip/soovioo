namespace Sova.Api.Events;

/// <summary>Who can see an event beyond its creator.
/// <see cref="Everyone"/> is the enum's default (0) on purpose — an `Event`
/// constructed without explicitly setting `Audience` (older code, a test
/// fixture) falls back to today's "everyone in the school" behavior rather
/// than silently becoming private.</summary>
public enum EventAudience
{
    Everyone = 0,
    OnlyMe = 1,

    /// <summary>Audience is the union of whatever <see cref="EventSchoolClass"/>,
    /// <see cref="EventTeacher"/>, <see cref="EventParent"/> and
    /// <see cref="EventStudent"/> rows exist for this event — see
    /// <see cref="EventVisibilityExtensions"/> for the exact resolution per role.</summary>
    Selected = 2,
}
