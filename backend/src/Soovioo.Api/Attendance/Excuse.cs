using Soovioo.Api.Auth;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Attendance;

/// <summary>An omluvenka: why a pupil is or was away, from one day to
/// another. Each school day of it is an <see cref="Absence"/> linked by
/// <see cref="Absence.ExcuseId"/>, which the teacher approves or rejects —
/// approves at the earliest that day.</summary>
public class Excuse
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public int StudentId { get; set; }
    public StudentProfile? Student { get; set; }

    /// <summary>Null for omluvenky from before they were kept, and once the
    /// account is gone.</summary>
    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    /// <summary>Lesson numbers of a one-day omluvenka; empty = the whole day.</summary>
    public List<int> Periods { get; set; } = [];

    public string Reason { get; set; } = "";

    /// <summary>How the family gave it to the teacher who wrote it down;
    /// null when the family wrote it here themselves.</summary>
    public ExcuseChannel? Channel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Absence> Absences { get; set; } = [];
    public List<ExcuseAttachment> Attachments { get; set; } = [];
}

/// <summary>A doctor's note or the like, stored like homework attachments.</summary>
public class ExcuseAttachment
{
    public int Id { get; set; }

    public int ExcuseId { get; set; }
    public Excuse? Excuse { get; set; }

    public int SchoolId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
}

/// <summary>Ways schools take omluvenky by their školní řád.</summary>
public enum ExcuseChannel
{
    Phone,
    InPerson,
    Email,

    /// <summary>On paper — the omluvný list, žákovská knížka, a letter.</summary>
    Letter,
    DataBox,
    TextMessage,
    Other,
}

public enum ExcuseStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>The teacher approved some days and rejected others.</summary>
    PartlyApproved,
}
