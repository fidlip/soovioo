using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Demo;

/// <summary>Deletes everything a demo school holds — every tenant table,
/// its audit log, sessions and the attachments on disk — for "Smazat demo",
/// "Resetovat data" and the daily expiry job. Deliberately unusable on a
/// real school (<see cref="Evidence.School.IsDemo"/> guard): <c>DELETE
/// /api/schools/{id}</c> refuses a school with users for exactly that
/// reason, a real tenant's data never goes through a silent cascade.</summary>
public class DemoSchoolPurger(AppDbContext db, MessageAttachmentStorageService storage)
{
    /// <summary>Empties the school (keeps the <see cref="Evidence.School"/>
    /// row itself unless <paramref name="deleteSchool"/>). Returns false
    /// when it isn't a demo school — nothing touched.</summary>
    /// <param name="keepAuditLog">"Vymazat data": the log (and the support
    /// sessions it refers to) stays.</param>
    /// <param name="keepUserId">An account (with its person) that survives —
    /// somebody has to be able to log into the emptied school.</param>
    public async Task<bool> PurgeAsync(int schoolId, bool deleteSchool, bool keepAuditLog = false, int? keepUserId = null)
    {
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == schoolId);
        if (school is null || !school.IsDemo)
        {
            return false;
        }

        var relational = db.Database.IsRelational();
        await using var transaction = relational ? await db.Database.BeginTransactionAsync() : null;

        // Children before parents, so no foreign key ever points at a
        // deleted row (the DB cascades would cope with most, not all —
        // e.g. SchoolClass.ClassTeacherId → User has none).
        await DeleteAsync(db.Notifications.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.MessageAttachments.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Messages.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.ConversationParticipants.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Conversations.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.MessageBroadcasts.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.HomeworkAttachments.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.HomeworkCompletions.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.HomeworkStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Homework.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.EventSchoolClasses.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.EventTeachers.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.EventParents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.EventStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Events.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.PaymentRecipients.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.PaymentSchoolClasses.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.PaymentParents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.PaymentStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Payments.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.BehaviorRecords.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.BehaviorClassifications.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Grades.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Absences.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TimetableEntries.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.StudentGroupMembers.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.StudentGroupClasses.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.StudentGroups.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.GroupDivisions.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.CurriculumItems.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.ClassCurriculumItems.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.SubjectRvpAreas.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TeachingAssignments.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TimetableGenerations.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TimetableVersions.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.SchoolPeriods.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.VisibilityChoices.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.VisibilityRules.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.RetentionPolicies.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.ParentStudents.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.ParentProfiles.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TeacherQualifications.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.TeacherProfiles.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Students.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.SchoolClasses.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Subjects.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.Classrooms.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        if (!keepAuditLog)
        {
            await DeleteAsync(db.SupportSessions.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
            await DeleteAsync(db.AuditLogEntries.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        }
        await DeleteAsync(db.DemoEmails.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
        await DeleteAsync(db.RefreshTokens.IgnoreQueryFilters().Where(x => x.User!.SchoolId == schoolId));
        await DeleteAsync(db.Persons.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && (keepUserId == null || x.UserId != keepUserId)));
        await DeleteAsync(db.Users.IgnoreQueryFilters().IgnoreAutoIncludes().Where(x => x.SchoolId == schoolId && x.Id != keepUserId));
        // The kept account's photo goes with the school's files below.
        foreach (var kept in await db.Persons.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId && x.PhotoKey != null).ToListAsync())
        {
            kept.PhotoKey = null;
        }
        await db.SaveChangesAsync();
        if (deleteSchool)
        {
            await DeleteAsync(db.RolePermissions.Where(x => db.Roles.IgnoreQueryFilters().Any(r => r.Id == x.RoleId && r.SchoolId == schoolId)));
            await DeleteAsync(db.Roles.IgnoreQueryFilters().Where(x => x.SchoolId == schoolId));
            await ForgetVisitorAsync(schoolId);
            await DeleteAsync(db.Schools.IgnoreQueryFilters().Where(x => x.Id == schoolId));
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }
        // After the commit: a failed delete must not leave rows pointing
        // at files that are already gone.
        storage.DeleteSchoolFiles(schoolId);
        db.ChangeTracker.Clear();
        return true;
    }

    /// <summary>Contacts and feedback e-mails stay only with consent.</summary>
    private async Task ForgetVisitorAsync(int schoolId)
    {
        await DeleteAsync(db.DemoContacts.Where(c => c.SchoolId == schoolId && !c.Consent));
        foreach (var contact in await db.DemoContacts.Where(c => c.SchoolId == schoolId).ToListAsync())
        {
            contact.SchoolId = null;
        }
        foreach (var feedback in await db.Feedback.Where(f => f.SchoolId == schoolId).ToListAsync())
        {
            feedback.SchoolId = null;
            if (!feedback.Consent)
            {
                feedback.Email = null;
            }
        }
        await db.SaveChangesAsync();
    }

    private Task DeleteAsync<T>(IQueryable<T> rows) where T : class => db.DeleteAsync(rows);
}
