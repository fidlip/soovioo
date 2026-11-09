using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.People;

namespace Soovioo.Api.Retention;

/// <summary>Erases a former pupil's data, a category at a time
/// (<see cref="RetentionCategory"/>). Works without a request (the daily
/// job), so every query names its school explicitly.</summary>
public class StudentEraser(
    AppDbContext db, AccountEraser accounts, PhotoStorage photos, MessageAttachmentStorageService files)
{
    /// <summary>Categories of <paramref name="student"/> due by
    /// <paramref name="today"/> and not erased yet, in erasing order.</summary>
    public static IReadOnlyList<RetentionCategory> Due(StudentProfile student, RetentionPolicy policy, DateOnly today) =>
        student.LeftAt is not { } leftAt
            ? []
            : Enum.GetValues<RetentionCategory>()
                .Where(c => !student.ErasedCategories.Contains(c) && policy.DueOn(c, leftAt) <= today)
                .ToList();

    /// <summary>Erases <paramref name="categories"/> of the pupil;
    /// <see cref="RetentionCategory.Profile"/> erases everything and deletes
    /// the record. Saves.</summary>
    public async Task EraseAsync(StudentProfile student, IEnumerable<RetentionCategory> categories)
    {
        var wanted = categories.ToHashSet();
        if (wanted.Contains(RetentionCategory.Profile))
        {
            wanted = Enum.GetValues<RetentionCategory>().ToHashSet();
        }

        var id = student.Id;
        var schoolId = student.SchoolId;
        var filesToDelete = new List<string>();
        string? photoKey = null;

        foreach (var category in Enum.GetValues<RetentionCategory>().Where(wanted.Contains))
        {
            switch (category)
            {
                case RetentionCategory.Photos:
                    photoKey = student.Person!.PhotoKey;
                    student.Person.PhotoKey = null;
                    student.Person.PhotoStatus = null;
                    student.PhotoConsentGivenBy = null;
                    student.PhotoConsentAt = null;
                    break;
                case RetentionCategory.Contacts:
                    student.ContactPhone = null;
                    student.ContactEmail = null;
                    student.Person!.Phone = null;
                    student.Person.Email = null;
                    student.Person.Nickname = null;
                    student.Person.NicknameProposal = null;
                    break;
                case RetentionCategory.Messages:
                    var conversations = db.Conversations.IgnoreQueryFilters()
                        .Where(c => c.SchoolId == schoolId && c.StudentId == id);
                    var messages = db.Messages.IgnoreQueryFilters()
                        .Where(m => conversations.Any(c => c.Id == m.ConversationId));
                    var attachments = db.MessageAttachments.IgnoreQueryFilters()
                        .Where(a => messages.Any(m => m.Id == a.MessageId));
                    filesToDelete.AddRange(await attachments.Select(a => a.StoragePath).ToListAsync());
                    await db.DeleteAsync(attachments);
                    await db.DeleteAsync(messages);
                    await db.DeleteAsync(db.ConversationParticipants.IgnoreQueryFilters()
                        .Where(p => conversations.Any(c => c.Id == p.ConversationId)));
                    await db.DeleteAsync(conversations);
                    break;
                case RetentionCategory.Homework:
                    await db.DeleteAsync(db.HomeworkCompletions.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    await db.DeleteAsync(db.HomeworkStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    break;
                case RetentionCategory.Behavior:
                    await db.DeleteAsync(db.BehaviorRecords.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    await db.DeleteAsync(db.BehaviorClassifications.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    break;
                case RetentionCategory.Attendance:
                    await db.DeleteAsync(db.Absences.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    var excuses = db.Excuses.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id);
                    var excuseFiles = db.ExcuseAttachments.IgnoreQueryFilters().Where(a => excuses.Any(e => e.Id == a.ExcuseId));
                    filesToDelete.AddRange(await excuseFiles.Select(a => a.StoragePath).ToListAsync());
                    await db.DeleteAsync(excuseFiles);
                    await db.DeleteAsync(excuses);
                    break;
                case RetentionCategory.Grades:
                    await db.DeleteAsync(db.ReportCardGrades.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    await db.DeleteAsync(db.Grades.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    break;
                case RetentionCategory.Payments:
                    await db.DeleteAsync(db.PaymentRecipients.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    await db.DeleteAsync(db.PaymentStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
                    break;
                case RetentionCategory.Profile:
                    break;
            }
            var types = NotificationTypesOf(category);
            if (types.Length > 0)
            {
                await db.DeleteAsync(db.Notifications.IgnoreQueryFilters()
                    .Where(n => n.SchoolId == schoolId && n.StudentId == id && types.Contains(n.Type)));
            }
            if (!student.ErasedCategories.Contains(category))
            {
                student.ErasedCategories = [.. student.ErasedCategories, category];
            }
        }

        if (wanted.Contains(RetentionCategory.Profile))
        {
            await EraseProfileAsync(student);
        }
        else
        {
            await db.SaveChangesAsync();
        }

        photos.Delete(photoKey);
        foreach (var path in filesToDelete)
        {
            files.Delete(path);
        }
    }

    private static NotificationType[] NotificationTypesOf(RetentionCategory category) => category switch
    {
        RetentionCategory.Photos => [NotificationType.Photo],
        RetentionCategory.Messages => [NotificationType.Message],
        RetentionCategory.Homework => [NotificationType.Homework],
        RetentionCategory.Attendance => [NotificationType.Absence, NotificationType.Excuse, NotificationType.NewExcuse],
        RetentionCategory.Grades => [NotificationType.Grade],
        _ => [],
    };

    /// <summary>The pupil's record goes; their account and the accounts of
    /// parents left with no pupil at the school are erased.</summary>
    private async Task EraseProfileAsync(StudentProfile student)
    {
        var id = student.Id;
        var schoolId = student.SchoolId;
        var parentProfileIds = await db.ParentStudents.IgnoreQueryFilters()
            .Where(ps => ps.SchoolId == schoolId && ps.StudentId == id)
            .Select(ps => ps.ParentProfileId)
            .ToListAsync();

        await db.DeleteAsync(db.EventStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
        await db.DeleteAsync(db.ParentStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));
        await db.DeleteAsync(db.VisibilityChoices.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.PersonId == student.PersonId));
        await db.DeleteAsync(db.Notifications.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.StudentId == id));

        // With an account the Person stays with it, anonymized like any
        // erased account's; without one it goes with the record.
        var person = student.Person!;
        db.Students.Remove(student);
        if (person.UserId is null)
        {
            db.Persons.Remove(person);
        }
        await db.SaveChangesAsync();

        if (person.UserId is { } accountId
            && await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == accountId) is { } own)
        {
            await accounts.EraseAsync(own);
        }

        // A parent with another pupil here (current or former) stays; one
        // who is also staff keeps the account for that.
        var orphaned = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == schoolId
                && db.Persons.IgnoreQueryFilters().Any(p => p.UserId == u.Id && parentProfileIds.Contains(p.Id))
                && !db.ParentStudents.IgnoreQueryFilters().Any(ps => ps.ParentProfile!.Person!.UserId == u.Id))
            .ToListAsync();
        foreach (var parent in orphaned.Where(u => u.Roles.All(r => r.Kind == RoleKind.Parent)))
        {
            await accounts.EraseAsync(parent);
        }
    }
}
