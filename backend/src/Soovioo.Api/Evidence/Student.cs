using Soovioo.Api.Auth;
using Soovioo.Api.Reference;

namespace Soovioo.Api.Evidence;

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

    /// <summary>KSTPR item — RAKO kvalifikátor státního občanství ("3"=Občan
    /// ČR, "5"=cizinec s trvalým pobytem, "6"=cizinec bez trvalého pobytu,
    /// "A"=azylant, ...). Deliberately NOT the same thing as <see cref="CountryCode"/>
    /// — matrika wants these as two separate items (an earlier version of this
    /// field conflated them into one "Citizenship" string, which was wrong).
    /// Small, stable codelist — kept as a plain string + a hardcoded option
    /// list on the frontend (same pattern as Gender/role options) rather than
    /// a whole reference table for 8 rows.</summary>
    public string? CitizenshipQualifier { get; set; }

    /// <summary>STPR item — the actual country of citizenship, from the RAST
    /// číselník (<see cref="Reference.Country"/>). "203" = Česká republika is
    /// the literal example the matrika documentation itself gives.</summary>
    public string? CountryCode { get; set; }
    public Country? Country { get; set; }

    /// <summary>Ulice + číslo — trvalé bydliště, the part of the address
    /// matrika doesn't code (OBECB/OKRESB are municipality/district level,
    /// not street level). Real schools need this for correspondence.</summary>
    public string? Street { get; set; }

    /// <summary>OBECB item — FK into the RAUJ číselník
    /// (<see cref="Reference.Municipality"/>), which already carries its own
    /// district code, so OKRESB is derived from this rather than asked for
    /// separately (see MatrikaExportService).</summary>
    public string? MunicipalityCode { get; set; }
    public Municipality? Municipality { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>SP_OBVOD item — whether the student's permanent address falls
    /// within the school's spádový obvod. Null = not determined.</summary>
    public bool? CatchmentArea { get; set; }

    /// <summary>Roughly ZAHDAT (datum zahájení vzdělávání) — also just useful
    /// on its own for "kdy žák nastoupil" regardless of matrika.</summary>
    public DateOnly? EnrollmentDate { get; set; }

    // Emergency/guardian contact — a real school needs a phone number on file
    // regardless of whether a parent has a Soovioo account (ParentStudent links
    // an actual User; this is the "call this number if something happens" field).
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>The Student-role account that lets this student log in for
    /// themselves (nullable — most students won't have one yet). Deliberately
    /// a direct FK, not a join table like <see cref="ParentStudent"/>: a
    /// student only ever has at most one own account, unlike a parent who may
    /// be linked to several children. Set via <see cref="UsersController"/>
    /// when an Admin creates/edits a Student-role user, cleared automatically
    /// (see AppDbContext) if that account is deleted, and cleared explicitly
    /// in <see cref="StudentsController.DeleteStudent"/> which deletes the
    /// account outright — the student is gone, so is their login.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Default variable symbol used when generating this student's
    /// <see cref="Payments.PaymentRecipient"/> rows — overridable per-payment
    /// (see <see cref="Payments.PaymentRecipient.VariableSymbol"/>, nullable,
    /// null = fall back to this field). Null until an admin fills it in.</summary>
    public string? DefaultVariableSymbol { get; set; }

    /// <summary>Photo — storage key (see <see cref="People.PhotoStorage"/>),
    /// null = initials. Only ever set while there's a consent
    /// (<see cref="PhotoConsentAt"/>); revoking it deletes the photo.</summary>
    public string? PhotoKey { get; set; }

    /// <summary>Set with <see cref="PhotoKey"/>: a photo the pupil uploaded
    /// waits for a parent's (or the school's) approval.</summary>
    public People.PhotoStatus? PhotoStatus { get; set; }

    /// <summary>The legal guardian's consent to the pupil's photo (GDPR
    /// legal basis) — who gave it and when. Null = no consent, no photo.</summary>
    public string? PhotoConsentGivenBy { get; set; }
    public DateTimeOffset? PhotoConsentAt { get; set; }
}
