using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Soovioo.Api.Auth;

namespace Soovioo.Api.Tests.Auth;

/// <summary>Every endpoint's gate, as declared on the controller and the
/// action. A new endpoint, or a changed gate, fails here until the map below
/// says so — the in-method scope checks have their own tests per agenda,
/// and <see cref="RequirePermissionAttributeTests"/> the gate itself.</summary>
public class AuthorizationMapTests
{
    /// <summary>"anonymous", "authenticated", "roles:X,Y", "perm:P", joined
    /// by " + "; "superadmin" when a SuperAdmin may get through.</summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Absences.AddAbsence"] = "authenticated + perm:AbsencesWrite",
        ["Absences.DeleteAbsence"] = "authenticated + perm:AbsencesWrite",
        ["Absences.GetAbsences"] = "authenticated",
        ["Absences.ReviewAbsence"] = "authenticated + perm:AbsencesWrite",
        ["Absences.SubmitExcuse"] = "authenticated",
        ["Absences.UpdateAbsence"] = "authenticated + perm:AbsencesWrite",
        ["AuditLog.GetActions"] = "authenticated + perm:AuditLog + superadmin",
        ["AuditLog.GetEntries"] = "authenticated + perm:AuditLog + superadmin",
        ["AuditLog.GetFilterOptions"] = "authenticated + perm:AuditLog + superadmin",
        ["Auth.AcceptInvite"] = "anonymous",
        ["Auth.Login"] = "anonymous",
        ["Auth.Logout"] = "anonymous",
        ["Auth.Refresh"] = "anonymous",
        ["Behavior.CreateBehaviorRecord"] = "roles:Staff",
        ["Behavior.DeleteBehaviorRecord"] = "roles:Staff",
        ["Behavior.GetBehaviorClassifications"] = "authenticated",
        ["Behavior.GetBehaviorRecords"] = "authenticated",
        ["Behavior.MarkBehaviorRecordRead"] = "authenticated",
        ["Behavior.SetClassification"] = "roles:Staff",
        ["CalendarFeed.GetFeed"] = "anonymous",
        ["CalendarToken.GetFeedUrl"] = "authenticated",
        ["CalendarToken.RegenerateFeedUrl"] = "authenticated",
        ["ClassDirectory.Get"] = "roles:Parent,Student",
        ["Classrooms.AddClassroom"] = "authenticated + perm:Classrooms",
        ["Classrooms.DeleteClassroom"] = "authenticated + perm:Classrooms",
        ["Classrooms.GetClassrooms"] = "authenticated",
        ["Classrooms.UpdateClassroom"] = "authenticated + perm:Classrooms",
        ["Countries.GetCountries"] = "authenticated",
        ["Conversations.DeleteMessage"] = "authenticated",
        ["Conversations.DownloadAttachment"] = "authenticated",
        ["Conversations.GetBroadcast"] = "authenticated",
        ["Conversations.GetContacts"] = "authenticated",
        ["Conversations.GetConversation"] = "authenticated",
        ["Conversations.GetConversations"] = "authenticated",
        ["Conversations.MarkRead"] = "authenticated",
        ["Conversations.Reply"] = "authenticated",
        ["Conversations.StartConversation"] = "authenticated",
        ["Conversations.UploadAttachment"] = "authenticated",
        ["Dashboard.GetDashboard"] = "authenticated",
        ["Demo.Clear"] = "anonymous",
        ["Demo.Create"] = "anonymous",
        ["Demo.Delete"] = "anonymous",
        ["Demo.Feedback"] = "anonymous",
        ["Demo.GetUsers"] = "anonymous",
        ["Demo.Login"] = "anonymous",
        ["Demo.Reset"] = "anonymous",
        ["DemoAdmin.GetContacts"] = "roles:SuperAdmin",
        ["DemoAdmin.GetContactsCsv"] = "roles:SuperAdmin",
        ["DemoAdmin.GetFeedback"] = "roles:SuperAdmin",
        ["DemoEmails.GetEmails"] = "authenticated",
        ["Events.CreateEvent"] = "authenticated + perm:EventsWrite",
        ["Events.DeleteEvent"] = "authenticated + perm:EventsWrite",
        ["Events.GetEvents"] = "authenticated",
        ["Events.GetRecipientOptions"] = "authenticated + perm:EventsWrite",
        ["Events.UpdateEvent"] = "authenticated + perm:EventsWrite",
        ["Grades.AddGrade"] = "authenticated + perm:GradesWrite",
        ["Grades.DeleteGrade"] = "authenticated + perm:GradesWrite",
        ["Grades.GetGrades"] = "authenticated",
        ["Grades.UpdateGrade"] = "authenticated + perm:GradesWrite",
        ["Homework.CreateHomework"] = "authenticated + perm:HomeworkWrite",
        ["Homework.DeleteHomework"] = "authenticated + perm:HomeworkWrite",
        ["Homework.DownloadAttachment"] = "authenticated",
        ["Homework.GetHomework"] = "authenticated",
        ["Homework.MarkComplete"] = "authenticated",
        ["Homework.UpdateHomework"] = "authenticated + perm:HomeworkWrite",
        ["Homework.UploadAttachment"] = "authenticated + perm:HomeworkWrite",
        ["Matrika.Export"] = "authenticated + perm:MatrikaExport",
        ["MessageBroadcast.BroadcastToClass"] = "authenticated + perm:MessagesBroadcastClass",
        ["MessageBroadcast.BroadcastToSchool"] = "authenticated + perm:MessagesBroadcastSchool",
        ["MessageBroadcast.GetBroadcastableClasses"] = "authenticated + perm:MessagesBroadcastClass",
        ["Municipalities.SearchMunicipalities"] = "authenticated",
        ["MyChildren.Get"] = "roles:Parent",
        ["MyChildren.SetVisibility"] = "roles:Parent",
        ["MyProfile.ChangePassword"] = "authenticated + superadmin",
        ["MyProfile.GetProfile"] = "authenticated + superadmin",
        ["MyProfile.GetVisibility"] = "authenticated + superadmin",
        ["MyProfile.SetVisibility"] = "authenticated + superadmin",
        ["MyProfile.SwitchRole"] = "authenticated + superadmin",
        ["MyProfile.UpdateProfile"] = "authenticated + superadmin",
        ["MySchool.Get"] = "authenticated + perm:Administration",
        ["MySchool.GetNotifications"] = "authenticated + perm:Administration",
        ["MySchool.GetVisibility"] = "authenticated + perm:Administration",
        ["MySchool.SetBankAccount"] = "authenticated + perm:Administration",
        ["MySchool.SetDetails"] = "authenticated + perm:Administration",
        ["MySchool.SetMatrikaIds"] = "authenticated + perm:Administration",
        ["MySchool.SetNotifications"] = "authenticated + perm:Administration",
        ["MySchool.SetType"] = "authenticated + perm:Administration",
        ["MySchool.SetVisibility"] = "authenticated + perm:Administration",
        ["Notifications.GetNotifications"] = "authenticated",
        ["Notifications.GetSettings"] = "authenticated",
        ["Notifications.GetUnreadCounts"] = "authenticated",
        ["Notifications.MarkAllRead"] = "authenticated",
        ["Notifications.MarkRead"] = "authenticated",
        ["Notifications.RegisterDevice"] = "authenticated",
        ["Notifications.RemoveDevice"] = "authenticated",
        ["Notifications.SetEmailSettings"] = "authenticated",
        ["Notifications.SetPushSettings"] = "authenticated",
        ["Notifications.SetSettings"] = "authenticated",
        ["Notifications.Unsubscribe"] = "anonymous",
        ["ParentStudents.AddLink"] = "authenticated + perm:Students",
        ["ParentStudents.GetLinks"] = "authenticated + perm:Students",
        ["ParentStudents.RemoveLink"] = "authenticated + perm:Students",
        ["Parents.GetParents"] = "roles:Staff",
        ["Payments.CreatePayment"] = "authenticated + perm:PaymentsWrite",
        ["Payments.DeletePayment"] = "authenticated + perm:PaymentsWrite",
        ["Payments.GetBankAccount"] = "authenticated",
        ["Payments.GetPayments"] = "authenticated",
        ["Payments.GetRecipientOptions"] = "authenticated + perm:PaymentsWrite",
        ["Payments.SetRecipientPaid"] = "authenticated + perm:PaymentsWrite",
        ["Payments.SetRecipientVariableSymbol"] = "authenticated + perm:PaymentsWrite",
        ["People.AddPerson"] = "authenticated + perm:Administration",
        ["People.DeletePerson"] = "authenticated + perm:Administration",
        ["People.GetPeople"] = "authenticated + perm:Administration",
        ["People.GetPerson"] = "authenticated + perm:Administration",
        ["People.UpdatePerson"] = "authenticated + perm:Administration",
        ["Photos.ApproveStudentPhoto"] = "authenticated",
        ["Photos.DeleteStudentPhoto"] = "authenticated",
        ["Photos.DeleteUserPhoto"] = "authenticated",
        ["Photos.GetOwnStudents"] = "authenticated",
        ["Photos.GetStudentPhoto"] = "authenticated",
        ["Photos.GetStudentState"] = "authenticated",
        ["Photos.GetUserPhoto"] = "authenticated",
        ["Photos.GiveConsent"] = "authenticated",
        ["Photos.RejectStudentPhoto"] = "authenticated",
        ["Photos.RevokeConsent"] = "authenticated",
        ["Photos.UploadStudentPhoto"] = "authenticated",
        ["Photos.UploadUserPhoto"] = "authenticated",
        ["PublicSchools.GetSchool"] = "anonymous",
        ["ReportCard.GetReportCard"] = "authenticated",
        ["Retention.CancelLeave"] = "authenticated + perm:Retention",
        ["Retention.CancelStaffLeave"] = "authenticated + perm:Retention",
        ["Retention.EraseDue"] = "authenticated + perm:Retention",
        ["Retention.EraseStaff"] = "authenticated + perm:Retention",
        ["Retention.EraseStudent"] = "authenticated + perm:Retention",
        ["Retention.GetHandoverConflicts"] = "authenticated + perm:Retention",
        ["Retention.GetPeople"] = "authenticated + perm:Retention",
        ["Retention.GetPolicy"] = "authenticated + perm:Retention",
        ["Retention.GetStaffLeave"] = "authenticated + perm:Retention",
        ["Retention.SetLeave"] = "authenticated + perm:Retention",
        ["Retention.SetPolicy"] = "authenticated + perm:Retention",
        ["Retention.SetStaffLeave"] = "authenticated + perm:Retention",
        ["Roles.AddRole"] = "authenticated + perm:Administration",
        ["Roles.DeleteRole"] = "authenticated + perm:Administration",
        ["Roles.GetCatalog"] = "authenticated + perm:Administration",
        ["Roles.GetRoles"] = "authenticated + perm:Administration",
        ["Roles.UpdateRole"] = "authenticated + perm:Administration",
        ["SchoolClasses.AddSchoolClass"] = "authenticated + perm:Classes",
        ["SchoolClasses.DeleteSchoolClass"] = "authenticated + perm:Classes",
        ["SchoolClasses.GetSchoolClasses"] = "authenticated",
        ["SchoolClasses.UpdateSchoolClass"] = "authenticated + perm:Classes",
        ["SchoolDirectory.Get"] = "roles:Parent,Student",
        ["SchoolPeriods.DeletePeriod"] = "authenticated + perm:Timetable",
        ["SchoolPeriods.GetPeriods"] = "authenticated",
        ["SchoolPeriods.UpsertPeriod"] = "authenticated + perm:Timetable",
        ["Schools.AddSchool"] = "roles:SuperAdmin",
        ["Schools.DeleteSchool"] = "roles:SuperAdmin",
        ["Schools.GetSchools"] = "roles:SuperAdmin",
        ["Schools.SetMatrikaIds"] = "roles:SuperAdmin",
        ["Schools.UpdateSchool"] = "roles:SuperAdmin",
        ["Students.AddStudent"] = "authenticated + perm:Students",
        ["Students.DeleteStudent"] = "authenticated + perm:Students",
        ["Students.GetStudents"] = "authenticated",
        ["Students.UpdateStudent"] = "authenticated + perm:Students",
        ["Subjects.AddSubject"] = "authenticated + perm:Subjects",
        ["Subjects.DeleteSubject"] = "authenticated + perm:Subjects",
        ["Subjects.GetSubjects"] = "authenticated",
        ["Subjects.UpdateSubject"] = "authenticated + perm:Subjects",
        ["Support.AddSuperAdmin"] = "roles:SuperAdmin",
        ["Support.DeleteSuperAdmin"] = "roles:SuperAdmin",
        ["Support.EndCurrentSession"] = "authenticated",
        ["Support.GetSuperAdmins"] = "roles:SuperAdmin",
        ["Support.GetSupportSessions"] = "roles:SuperAdmin",
        ["Support.Redeem"] = "anonymous",
        ["Support.StartSupportSession"] = "roles:SuperAdmin",
        ["Support.UpdateSuperAdmin"] = "roles:SuperAdmin",
        ["Teachers.GetTeachers"] = "authenticated",
        ["TeacherWishes.Get"] = "authenticated",
        ["TeacherWishes.Set"] = "authenticated + perm:Timetable",
        ["Teaching.ConfirmAssignments"] = "authenticated + perm:Teaching",
        ["Teaching.GetTeacherTeaching"] = "authenticated",
        ["Teaching.GetTeaching"] = "authenticated",
        ["Teaching.ImportFromTimetable"] = "authenticated + perm:Teaching",
        ["Teaching.SaveCurriculum"] = "authenticated + perm:Teaching",
        ["Teaching.SetClassCurriculum"] = "authenticated + perm:Teaching",
        ["Teaching.SetClassDoubleLessons"] = "authenticated + perm:Teaching",
        ["Teaching.SetAssignment"] = "authenticated + perm:Teaching",
        ["Timetable.AddEntry"] = "authenticated + perm:Timetable",
        ["Timetable.DeleteEntry"] = "authenticated + perm:Timetable",
        ["Timetable.GetTeacherTimetable"] = "authenticated",
        ["Timetable.GetTimetable"] = "authenticated",
        ["Timetable.UpdateEntry"] = "authenticated + perm:Timetable",
        ["TimetableGeneration.Generate"] = "authenticated + perm:Timetable",
        ["TimetableGeneration.GetGeneration"] = "authenticated + perm:Timetable",
        ["TimetableGeneration.Seen"] = "authenticated + perm:Timetable",
        ["TimetableVersions.ArchiveVersion"] = "authenticated + perm:Timetable",
        ["TimetableVersions.CreateVersion"] = "authenticated + perm:Timetable",
        ["TimetableVersions.GetBusyClassrooms"] = "authenticated + perm:Timetable",
        ["TimetableVersions.GetProblems"] = "authenticated + perm:Timetable",
        ["TimetableVersions.GetVersions"] = "authenticated",
        ["TimetableVersions.PublishVersion"] = "authenticated + perm:Timetable",
        ["TimetableVersions.RestoreVersion"] = "authenticated + perm:Timetable",
        ["TimetableVersions.UnpublishVersion"] = "authenticated + perm:Timetable",
        ["TimetableVersions.UpdateVersion"] = "authenticated + perm:Timetable",
        ["Users.Activate"] = "authenticated + perm:Administration",
        ["Users.AddUser"] = "authenticated + perm:Administration",
        ["Users.Deactivate"] = "authenticated + perm:Administration",
        ["Users.DeleteUser"] = "authenticated + perm:Administration",
        ["Users.GetUsers"] = "authenticated + perm:Administration",
        ["Users.SendInvite"] = "authenticated + perm:Administration",
        ["Users.UpdateUser"] = "authenticated + perm:Administration",
    };

    public static IEnumerable<(string Name, string Gate)> Actual()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var controller in controllers)
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                var name = $"{controller.Name.Replace("Controller", "")}.{action.Name}";
                yield return (name, GateOf(controller, action));
            }
        }
    }

    private static string GateOf(Type controller, MethodInfo action)
    {
        var all = controller.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true)).ToList();
        if (action.GetCustomAttributes<AllowAnonymousAttribute>().Any()
            || (!all.OfType<IAuthorizeData>().Any() && !all.OfType<RequirePermissionAttribute>().Any())
            || (controller.GetCustomAttributes<AllowAnonymousAttribute>().Any() && !action.GetCustomAttributes<AuthorizeAttribute>().Any()))
        {
            return "anonymous";
        }
        var parts = new List<string>();
        var authorize = all.OfType<AuthorizeAttribute>().ToList();
        // The narrowest role list wins: a method's own roles are ANDed on top.
        var roles = authorize.Where(a => a.Roles is not null).Select(a => a.Roles!).LastOrDefault();
        parts.Add(roles is null ? "authenticated" : $"roles:{roles}");
        parts.AddRange(all.OfType<RequirePermissionAttribute>().Select(p => $"perm:{p.Permission}").Distinct());
        if (all.OfType<AllowSuperAdminAttribute>().Any())
        {
            parts.Add("superadmin");
        }
        return string.Join(" + ", parts);
    }

    [Fact]
    public void EveryEndpointHasTheGateTheMapSays()
    {
        var actual = Actual().ToDictionary(a => a.Name, a => a.Gate);

        var problems = actual.Keys.Except(Expected.Keys).Select(k => $"unmapped: {k} = {actual[k]}")
            .Concat(Expected.Keys.Except(actual.Keys).Select(k => $"gone: {k}"))
            .Concat(actual.Where(a => Expected.TryGetValue(a.Key, out var e) && e != a.Value)
                .Select(a => $"{a.Key}: {a.Value}, expected {Expected[a.Key]}"))
            .ToList();
        if (problems.Count > 0)
        {
            Assert.Fail(string.Join("\n", problems));
        }
    }
}
