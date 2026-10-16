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

    /// <summary>KSTPR/STPR item for matrika — státní občanství. Free text/code
    /// (the document confirms "203" for Czech citizens) rather than a strict
    /// enum, since we don't have the full RAKO číselník. Null = not recorded.</summary>
    public string? Citizenship { get; set; }

    // Trvalé bydliště — the real postal address. Matrika's OBECB/OKRESB items
    // want RAUJ/RAOR codelist codes we don't have yet (see MatrikaExportService),
    // so this is the human-readable address real schools actually need for
    // correspondence; code-mapping it is separate future work.
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>SP_OBVOD item — whether the student's permanent address falls
    /// within the school's spádový obvod. Null = not determined.</summary>
    public bool? CatchmentArea { get; set; }

    /// <summary>Roughly ZAHDAT (datum zahájení vzdělávání) — also just useful
    /// on its own for "kdy žák nastoupil" regardless of matrika.</summary>
    public DateOnly? EnrollmentDate { get; set; }

    // Emergency/guardian contact — a real school needs a phone number on file
    // regardless of whether a parent has a Sova account (ParentStudent links
    // an actual User; this is the "call this number if something happens" field).
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
}
