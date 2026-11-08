namespace Soovioo.Api.Auth;

/// <summary>Stored by name — never reorder-sensitive, and the web uses the
/// same names.</summary>
public enum Permission
{
    GradesRead,
    GradesWrite,
    AbsencesRead,
    AbsencesWrite,
    AbsencesExcuse,
    BehaviorRead,
    BehaviorPraise,
    BehaviorClassTeacher,
    BehaviorDirector,
    HomeworkRead,
    HomeworkWrite,
    HomeworkComplete,
    EventsWrite,
    PaymentsRead,
    PaymentsWrite,
    MessagesBroadcastClass,
    MessagesBroadcastSchool,
    Timetable,
    TimetableVersionsRead,
    TeachingRead,
    Teaching,
    Classes,
    Subjects,
    Classrooms,
    Students,
    StudentPhotos,
    Retention,
    MatrikaExport,
    AuditLog,

    /// <summary>Osoby, accounts, roles and the school's settings — only and
    /// always the built-in Admin role, never stored.</summary>
    Administration,
}

/// <summary>How far a permission reaches. Which ones a role can be given
/// depends on its <see cref="RoleKind"/>, see <see cref="PermissionCatalog"/>.</summary>
public enum PermissionScope
{
    None,

    /// <summary>A parent's own children, a pupil themselves; for staff what
    /// is theirs (přidělení výuky: their own). For what goes
    /// to whole classes (events, payments, class messages) a parent's
    /// children's classes — e.g. the parents' class representative.</summary>
    Own,

    /// <summary>The classes the member of staff is třídní of.</summary>
    ClassTeacher,

    /// <summary>The classes they are třídní of or teach (have a lesson there
    /// in the timetable). For grades and homework: the subjects they teach
    /// there. For events and payments it limits whom they can address;
    /// changing one is then up to whoever created it.</summary>
    MyClasses,

    School,
}

public record PermissionInfo(
    Permission Permission,
    string Agenda,
    string Label,
    IReadOnlyList<PermissionScope> StaffScopes,
    IReadOnlyList<PermissionScope> ParentScopes,
    IReadOnlyList<PermissionScope> StudentScopes)
{
    public IReadOnlyList<PermissionScope> ScopesFor(RoleKind kind) => kind switch
    {
        RoleKind.Staff => StaffScopes,
        RoleKind.Parent => ParentScopes,
        _ => StudentScopes,
    };
}

/// <summary>Every permission with the scopes each kind of role may have and
/// what the built-in roles get: Admin everything, a teacher their own
/// classes, parents and pupils their own.</summary>
public static class PermissionCatalog
{
    private static readonly PermissionScope[] NoneOnly = [PermissionScope.None];
    private static readonly PermissionScope[] Own = [PermissionScope.None, PermissionScope.Own];
    private static readonly PermissionScope[] SchoolOnly = [PermissionScope.None, PermissionScope.School];
    private static readonly PermissionScope[] OwnOrSchool = [PermissionScope.None, PermissionScope.Own, PermissionScope.School];
    private static readonly PermissionScope[] Classes =
        [PermissionScope.None, PermissionScope.ClassTeacher, PermissionScope.MyClasses, PermissionScope.School];

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(Permission.GradesRead, "Známky", "Vidět známky", Classes, Own, Own),
        new(Permission.GradesWrite, "Známky", "Zapsat, upravit a smazat známku", Classes, NoneOnly, NoneOnly),
        new(Permission.AbsencesRead, "Docházka", "Vidět docházku", Classes, Own, Own),
        new(Permission.AbsencesWrite, "Docházka", "Zapsat, upravit a smazat absenci, vyřídit omluvenku", Classes, NoneOnly, NoneOnly),
        new(Permission.AbsencesExcuse, "Docházka", "Omluvit absenci", Classes, Own, Own),
        new(Permission.BehaviorRead, "Chování", "Vidět záznamy chování", Classes, Own, Own),
        new(Permission.BehaviorPraise, "Chování", "Pochvala a poznámka", Classes, NoneOnly, NoneOnly),
        new(Permission.BehaviorClassTeacher, "Chování", "Napomenutí a důtka třídního učitele, chování velmi dobré", Classes, NoneOnly, NoneOnly),
        new(Permission.BehaviorDirector, "Chování", "Napomenutí a důtka ředitele, uspokojivé a neuspokojivé chování", Classes, NoneOnly, NoneOnly),
        new(Permission.HomeworkRead, "Úkoly", "Vidět úkoly", Classes, Own, Own),
        new(Permission.HomeworkWrite, "Úkoly", "Zadat, upravit a smazat úkol", Classes, NoneOnly, NoneOnly),
        new(Permission.HomeworkComplete, "Úkoly", "Označit úkol jako hotový", Classes, Own, Own),
        new(Permission.EventsWrite, "Akce", "Vytvořit, upravit a smazat akci", Classes, Own, NoneOnly),
        new(Permission.PaymentsRead, "Platby", "Vidět platby", Classes, Own, Own),
        new(Permission.PaymentsWrite, "Platby", "Založit a smazat platbu, označit zaplaceno", Classes, Own, NoneOnly),
        new(Permission.MessagesBroadcastClass, "Zprávy", "Hromadná zpráva třídě", Classes, Own, NoneOnly),
        new(Permission.MessagesBroadcastSchool, "Zprávy", "Hromadná zpráva celé škole", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.Timetable, "Rozvrh", "Upravit rozvrh a zvonění", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.TimetableVersionsRead, "Rozvrh", "Vidět koncepty verzí rozvrhu", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.TeachingRead, "Rozvrh", "Vidět učební plán a přidělení výuky", OwnOrSchool, NoneOnly, NoneOnly),
        new(Permission.Teaching, "Rozvrh", "Upravit učební plán a přidělení výuky", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.Classes, "Škola", "Upravit třídy", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.Subjects, "Škola", "Upravit předměty", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.Classrooms, "Škola", "Upravit učebny", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.Students, "Žáci", "Založit, upravit a smazat žáka, vazby na rodiče", Classes, NoneOnly, NoneOnly),
        new(Permission.StudentPhotos, "Žáci", "Schválit a smazat fotku žáka", Classes, NoneOnly, NoneOnly),
        new(Permission.Retention, "Archiv", "Ukončení vzdělávání, archiv a výmaz údajů", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.MatrikaExport, "Archiv", "Export matriky", SchoolOnly, NoneOnly, NoneOnly),
        new(Permission.AuditLog, "Logy", "Vidět logy", SchoolOnly, NoneOnly, NoneOnly),
    ];

    public static PermissionInfo Info(Permission permission) => All.First(i => i.Permission == permission);

    public static bool Allows(Permission permission, RoleKind kind, PermissionScope scope) =>
        permission != Permission.Administration && Info(permission).ScopesFor(kind).Contains(scope);

    /// <summary>What the built-in roles may do out of the box. Permissions
    /// not listed are <see cref="PermissionScope.None"/>.</summary>
    public static IReadOnlyDictionary<Permission, PermissionScope> Defaults(UserRole builtIn) => builtIn switch
    {
        UserRole.Admin => All
            .Where(i => i.StaffScopes.Contains(PermissionScope.School))
            .ToDictionary(i => i.Permission, _ => PermissionScope.School),
        // Their own classes (and subjects), not the whole school; the
        // school's setup (timetable, classes, subjects, rooms) is Admin's.
        UserRole.Teacher => new Dictionary<Permission, PermissionScope>
        {
            [Permission.GradesRead] = PermissionScope.MyClasses,
            [Permission.GradesWrite] = PermissionScope.MyClasses,
            [Permission.AbsencesRead] = PermissionScope.MyClasses,
            [Permission.AbsencesWrite] = PermissionScope.MyClasses,
            [Permission.AbsencesExcuse] = PermissionScope.ClassTeacher,
            [Permission.BehaviorRead] = PermissionScope.MyClasses,
            [Permission.BehaviorPraise] = PermissionScope.MyClasses,
            [Permission.BehaviorClassTeacher] = PermissionScope.ClassTeacher,
            [Permission.HomeworkRead] = PermissionScope.MyClasses,
            [Permission.HomeworkWrite] = PermissionScope.MyClasses,
            [Permission.HomeworkComplete] = PermissionScope.MyClasses,
            [Permission.EventsWrite] = PermissionScope.MyClasses,
            [Permission.PaymentsRead] = PermissionScope.MyClasses,
            [Permission.PaymentsWrite] = PermissionScope.MyClasses,
            [Permission.MessagesBroadcastClass] = PermissionScope.MyClasses,
            [Permission.Students] = PermissionScope.ClassTeacher,
            [Permission.StudentPhotos] = PermissionScope.ClassTeacher,
        },
        UserRole.Parent => new Dictionary<Permission, PermissionScope>
        {
            [Permission.GradesRead] = PermissionScope.Own,
            [Permission.AbsencesRead] = PermissionScope.Own,
            [Permission.AbsencesExcuse] = PermissionScope.Own,
            [Permission.BehaviorRead] = PermissionScope.Own,
            [Permission.HomeworkRead] = PermissionScope.Own,
            [Permission.HomeworkComplete] = PermissionScope.Own,
            [Permission.PaymentsRead] = PermissionScope.Own,
        },
        UserRole.Student => new Dictionary<Permission, PermissionScope>
        {
            [Permission.GradesRead] = PermissionScope.Own,
            [Permission.AbsencesRead] = PermissionScope.Own,
            [Permission.BehaviorRead] = PermissionScope.Own,
            [Permission.HomeworkRead] = PermissionScope.Own,
            [Permission.HomeworkComplete] = PermissionScope.Own,
            [Permission.PaymentsRead] = PermissionScope.Own,
        },
        _ => new Dictionary<Permission, PermissionScope>(),
    };

    public static RoleKind KindOf(UserRole builtIn) => builtIn switch
    {
        UserRole.Parent => RoleKind.Parent,
        UserRole.Student => RoleKind.Student,
        _ => RoleKind.Staff,
    };

    public static string DefaultName(UserRole builtIn, Evidence.SchoolType schoolType) => builtIn switch
    {
        UserRole.Admin => "Admin",
        UserRole.Teacher => "Učitel",
        UserRole.Parent => "Rodič",
        UserRole.Student => schoolType == Evidence.SchoolType.Secondary ? "Student" : "Žák",
        _ => builtIn.ToString(),
    };

    public static readonly UserRole[] BuiltInRoles = [UserRole.Admin, UserRole.Teacher, UserRole.Parent, UserRole.Student];
}
