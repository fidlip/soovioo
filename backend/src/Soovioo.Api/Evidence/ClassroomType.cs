namespace Soovioo.Api.Evidence;

/// <summary>Preset classroom kinds (admin picks one instead of free text).
/// Stored as the enum name (string column), so new kinds don't renumber
/// anything.</summary>
public enum ClassroomType
{
    /// <summary>kmenová</summary>
    Homeroom,
    /// <summary>odborná</summary>
    Specialized,
    /// <summary>laboratoř</summary>
    Laboratory,
    /// <summary>počítačová</summary>
    Computer,
    /// <summary>tělocvična</summary>
    Gym,
    /// <summary>dílna</summary>
    Workshop,
    /// <summary>jiná</summary>
    Other,
}
