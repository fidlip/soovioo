namespace Soovioo.Api.Evidence;

/// <summary>The tenant root. Every other entity belongs to exactly one School
/// (except SuperAdmin users, whose SchoolId is null).</summary>
public class School
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>URL-safe identifier, used to resolve the tenant at login
    /// (and later from a subdomain). Unique.</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SchoolType Type { get; set; } = SchoolType.Elementary;

    /// <summary>Resortní identifikátor školy (RÚIAN/školský rejstřík) — identifies
    /// this school in the IZO item of every matrika record. Null until an admin
    /// fills it in; required before a matrika export can be generated.</summary>
    public string? Izo { get; set; }

    /// <summary>Resortní identifikátor právnické osoby — the login name for the
    /// matrika.msmt.cz/profa.msmt.cz portal and the RED_IZO item used in the
    /// anonymized "b" (podpůrná opatření) file. Null until an admin fills it in.</summary>
    public string? RedIzo { get; set; }

    // Institutional address/contact — not transmitted to matrika itself, but
    // needed on real documents (report card letterhead, official
    // correspondence) and for the school's own record-keeping.
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Ředitel/ředitelka — appears on official documents.</summary>
    public string? DirectorName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Used to build the SPAYD QR string for payments (see
    /// <c>Payments</c>) — the account recipients pay into. Null until an
    /// admin fills it in; without it, the frontend simply skips rendering a
    /// QR code.</summary>
    public string? BankAccountNumber { get; set; }

    /// <summary>A "Vyzkoušet" demo school (see <c>Demo/</c>): generated
    /// data, passwordless login by picking a user, deleted with everything
    /// in it once <see cref="ExpiresAt"/> passes. The demo-only endpoints
    /// refuse any school without this flag.</summary>
    public bool IsDemo { get; set; }

    /// <summary>When the demo school gets deleted (demo schools only).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Demo only: the generated users with the most to show
    /// (admin, teacher, parent, pupil) — offered first in the user picker,
    /// and the ones the wizard's "za koho se chcete podívat" logs in as.</summary>
    public List<int>? DemoFeaturedUserIds { get; set; }

    /// <summary>Pupils' photos are on: with a guardian's consent recorded
    /// per pupil they can be uploaded. Off (the default) = no uploads and
    /// none shown. Who sees them is up to "Viditelnost údajů".</summary>
    public bool StudentPhotos { get; set; }

    /// <summary>Parents may upload a photo of themselves (voluntary).</summary>
    public bool ParentPhotos { get; set; }

    /// <summary>Whether teachers see pupils' nicknames until they choose
    /// themselves (<see cref="User.ShowStudentNicknames"/>).</summary>
    public bool StaffSeeStudentNicknames { get; set; } = true;

    /// <summary>When the daily e-mail of new notifications goes out (Prague
    /// time) for everyone who didn't pick their own time.</summary>
    public TimeOnly DigestTime { get; set; } = new(17, 0);

    /// <summary>Kinds whose e-mail people can't switch off.</summary>
    public List<Notifications.NotificationType> ForcedEmailNotifications { get; set; } = [];

    /// <summary>A push may carry the whole text of a notification, for
    /// people who agree to it (<see cref="User.PushFullTextAt"/>). Off = only
    /// the kind of event goes through Google's and Apple's servers.</summary>
    public bool PushFullText { get; set; }

    /// <summary>Night quiet for push (Prague time) of everyone who didn't
    /// set their own — what comes in it is sent when it ends.</summary>
    public TimeOnly PushQuietFrom { get; set; } = new(21, 0);
    public TimeOnly PushQuietTo { get; set; } = new(7, 0);

    /// <summary>Teachers' wishes for those who didn't set their own, see
    /// <see cref="People.TeacherProfile.MinLessonsPerDay"/>. Null = none.</summary>
    public int? TeacherMinLessonsPerDay { get; set; }
    public int? TeacherMaxLessonsPerDay { get; set; }
    public int? TeacherMaxGapsPerDay { get; set; }

    /// <summary>The school's <see cref="Timetable.MainBlock"/>, periods from–to.</summary>
    public int MainBlockFrom { get; set; } = 1;
    public int MainBlockTo { get; set; } = 4;

    /// <summary>When a SuperAdmin may enter the school in support mode.</summary>
    public Support.SupportAccess SupportAccess { get; set; } = Support.SupportAccess.Approval;

    /// <summary>The built-in four (<see cref="Auth.RoleSeeder.AddMissing"/>)
    /// and the school's own.</summary>
    public List<Auth.Role> Roles { get; set; } = [];
}
