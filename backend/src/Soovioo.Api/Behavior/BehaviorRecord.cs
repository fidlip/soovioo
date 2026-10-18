using Soovioo.Api.Evidence;

namespace Soovioo.Api.Behavior;

/// <summary>Who may record which type is enforced in BehaviorController, not
/// here — see CreateBehaviorRecord's doc comment for the exact rule per
/// type (confirmed with the user, see ROADMAP "Chování").</summary>
public enum BehaviorRecordType
{
    Pochvala,
    Poznamka,
    NapomenutiTridnihoUcitele,
    DutkaTridnihoUcitele,
    NapomenutiReditele,
    DutkaReditele,
}

public class BehaviorRecord
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    public BehaviorRecordType Type { get; set; }
    public required string Text { get; set; }
    public DateOnly Date { get; set; }

    public required string RecordedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null until whoever views this record (typically the
    /// targeted student's Parent/Student account) marks it read — same
    /// shared-per-record simplification as <see cref="Messaging.Message.ReadAt"/>,
    /// not tracked per individual viewer. Drives the "unread" homepage
    /// widget (see DashboardController).</summary>
    public DateTimeOffset? ReadAt { get; set; }
}
