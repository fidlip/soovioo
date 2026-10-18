namespace Soovioo.Api.Evidence;

/// <summary>Decides how the school's pupils are called across the whole UI
/// (see <see cref="StudentTerm"/>): "žák" at a primary school, "student" at
/// a secondary one.</summary>
public enum SchoolType
{
    /// <summary>Základní škola — "žák".</summary>
    Elementary,

    /// <summary>Střední škola — "student".</summary>
    Secondary,
}
