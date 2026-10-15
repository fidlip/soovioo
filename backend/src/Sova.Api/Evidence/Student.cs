namespace Sova.Api.Evidence;

public enum Gender
{
    Male,
    Female,
}

public class Student
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>RODC item for matrika export. When set, used as-is. When null,
    /// <see cref="MatrikaExportService"/> constructs the documented pseudo-code
    /// (RRNNDD + "X" + sequence) from <see cref="DateOfBirth"/>/<see cref="Gender"/>
    /// instead — a real rodné číslo is not required to exist for every student.</summary>
    public string? RodneCislo { get; set; }

    /// <summary>Needed to construct DAT_NAROZ (and RODC, when it must be
    /// pseudo-generated). Null until an admin fills it in.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Needed for the RODC month+50 rule for girls, and for the POHLAVI
    /// item. Null until an admin fills it in.</summary>
    public Gender? Gender { get; set; }
}
