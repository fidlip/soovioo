namespace Soovioo.Api.SchoolYears;

public enum DayOffKind
{
    /// <summary>Prázdniny.</summary>
    Holidays,

    /// <summary>Státní svátek.</summary>
    PublicHoliday,

    /// <summary>Ředitelské volno (§ 24 odst. 2 školského zákona, nejvýš 5
    /// dnů ve školním roce).</summary>
    DirectorsDay,

    Other,
}

/// <summary>Days of a <see cref="SchoolYear"/> without lessons: prázdniny,
/// a public holiday, ředitelské volno. For the whole school, or only for
/// <see cref="ClassIds"/>.</summary>
public class SchoolDayOff
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int SchoolYearId { get; set; }
    public SchoolYear? SchoolYear { get; set; }

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>"Podzimní prázdniny".</summary>
    public required string Name { get; set; }

    public DayOffKind Kind { get; set; }

    /// <summary>Empty = the whole school.</summary>
    public List<int> ClassIds { get; set; } = [];
}
