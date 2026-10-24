using Soovioo.Api.Evidence;

namespace Soovioo.Api.Retention;

/// <summary>What of a former pupil's data gets erased, and when — each
/// category so many years after the pupil left (0 = right away). The school
/// sets it in Administrace → Odchody to match its own spisový a skartační
/// řád; <see cref="Profile"/> ends it all.</summary>
public enum RetentionCategory
{
    Photos,

    /// <summary>The pupil's contact phone and e-mail.</summary>
    Contacts,

    /// <summary>Conversations about the pupil.</summary>
    Messages,
    Homework,
    Behavior,
    Attendance,
    Grades,
    Payments,

    /// <summary>Everything left — the pupil's record with the matrika data,
    /// their account, and the accounts of parents with no other pupil.</summary>
    Profile,
}

public enum ErasureMode
{
    /// <summary>Due erasures wait in Administrace → Odchody for an admin to
    /// carry them out.</summary>
    Confirm,

    /// <summary>The daily job erases whatever is due.</summary>
    Automatic,
}

/// <summary>A school's rules for people who left. One row per school; a
/// school without one uses the defaults below.</summary>
public class RetentionPolicy
{
    /// <summary>Primary key — one policy per school.</summary>
    public int SchoolId { get; set; }
    public School? School { get; set; }

    public ErasureMode Mode { get; set; } = ErasureMode.Confirm;

    /// <summary>Days after a pupil's last day before their account, and the
    /// accounts of parents left without a pupil at the school, stop working
    /// — time to read the last messages.</summary>
    public int AccountGraceDays { get; set; } = 30;

    // Years after the pupil's last day, per category.
    public int PhotosYears { get; set; }
    public int ContactsYears { get; set; } = 1;
    public int MessagesYears { get; set; } = 1;
    public int HomeworkYears { get; set; } = 1;
    public int BehaviorYears { get; set; } = 5;
    public int AttendanceYears { get; set; } = 5;
    public int GradesYears { get; set; } = 10;
    public int PaymentsYears { get; set; } = 10;
    public int ProfileYears { get; set; } = 10;

    /// <summary>Years after a staff account was deactivated before the
    /// person's data is erased and the account anonymized.</summary>
    public int StaffYears { get; set; } = 5;

    public const int MaxYears = 100;
    public const int MaxGraceDays = 365;

    public int YearsFor(RetentionCategory category) => category switch
    {
        RetentionCategory.Photos => PhotosYears,
        RetentionCategory.Contacts => ContactsYears,
        RetentionCategory.Messages => MessagesYears,
        RetentionCategory.Homework => HomeworkYears,
        RetentionCategory.Behavior => BehaviorYears,
        RetentionCategory.Attendance => AttendanceYears,
        RetentionCategory.Grades => GradesYears,
        RetentionCategory.Payments => PaymentsYears,
        RetentionCategory.Profile => ProfileYears,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>The first day <paramref name="category"/> of a pupil whose
    /// last day was <paramref name="leftAt"/> may be erased — never later
    /// than the whole profile.</summary>
    public DateOnly DueOn(RetentionCategory category, DateOnly leftAt) =>
        leftAt.AddDays(1).AddYears(Math.Min(YearsFor(category), ProfileYears));

    /// <summary>The first day a staff account deactivated on
    /// <paramref name="deactivatedOn"/> gets erased.</summary>
    public DateOnly StaffDueOn(DateOnly deactivatedOn) => deactivatedOn.AddYears(StaffYears);
}
