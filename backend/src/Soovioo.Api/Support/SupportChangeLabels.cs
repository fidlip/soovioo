using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Support;

/// <summary>A journaled row (<see cref="SupportChange"/>) in words, and the
/// stored files it holds.</summary>
public static class SupportChangeLabels
{
    private static readonly Dictionary<string, string> Types = new()
    {
        ["School"] = "Škola",
        ["Grade"] = "Známka",
        ["StudentProfile"] = "Žák",
        ["SchoolClass"] = "Třída",
        ["Subject"] = "Předmět",
        ["SubjectClassroom"] = "Učebna předmětu",
        ["Classroom"] = "Učebna",
        ["Absence"] = "Absence",
        ["Excuse"] = "Omluvenka",
        ["ExcuseAttachment"] = "Příloha omluvenky",
        ["TimetableEntry"] = "Hodina rozvrhu",
        ["TimetableVersion"] = "Verze rozvrhu",
        ["CurriculumItem"] = "Učební plán",
        ["ClassCurriculumItem"] = "Učební plán třídy",
        ["SubjectRvpArea"] = "Oblast RVP předmětu",
        ["TeachingAssignment"] = "Přidělení výuky",
        ["StudentGroup"] = "Skupina",
        ["GroupDivision"] = "Dělení na skupiny",
        ["StudentGroupClass"] = "Třída skupiny",
        ["StudentGroupMember"] = "Člen skupiny",
        ["TimetableGroupChange"] = "Změna skupiny v rozvrhu",
        ["TeacherAbsence"] = "Nepřítomnost učitele",
        ["LessonChange"] = "Suplování",
        ["SubstitutionDay"] = "Den suplování",
        ["SchoolPeriod"] = "Vyučovací hodina",
        ["SchoolYear"] = "Školní rok",
        ["GradingPeriod"] = "Klasifikační období",
        ["SchoolDayOff"] = "Volný den",
        ["GradeMainBlock"] = "Hlavní blok ročníku",
        ["ClassMainBlock"] = "Hlavní blok třídy",
        ["Conversation"] = "Konverzace",
        ["ConversationParticipant"] = "Účastník konverzace",
        ["Message"] = "Zpráva",
        ["MessageAttachment"] = "Příloha zprávy",
        ["MessageBroadcast"] = "Hromadná zpráva",
        ["Event"] = "Akce",
        ["EventSchoolClass"] = "Třída na akci",
        ["EventTeacher"] = "Učitel na akci",
        ["EventParent"] = "Rodič na akci",
        ["EventStudent"] = "Žák na akci",
        ["User"] = "Účet",
        ["UserRoles"] = "Role účtu",
        ["ParentStudent"] = "Rodič žáka",
        ["Person"] = "Osoba",
        ["TeacherProfile"] = "Učitel",
        ["TeacherQualification"] = "Aprobace",
        ["ParentProfile"] = "Rodič",
        ["Homework"] = "Domácí úkol",
        ["HomeworkStudent"] = "Žák domácího úkolu",
        ["HomeworkCompletion"] = "Splnění domácího úkolu",
        ["HomeworkAttachment"] = "Příloha domácího úkolu",
        ["BehaviorRecord"] = "Výchovné opatření",
        ["BehaviorClassification"] = "Hodnocení chování",
        ["ReportCardGrade"] = "Známka na vysvědčení",
        ["ReportCardNote"] = "Poznámka na vysvědčení",
        ["AssessmentModeRule"] = "Způsob hodnocení",
        ["Payment"] = "Platba",
        ["PaymentRecipient"] = "Příjemce platby",
        ["PaymentSchoolClass"] = "Třída platby",
        ["PaymentParent"] = "Rodič platby",
        ["PaymentStudent"] = "Žák platby",
        ["VisibilityRule"] = "Pravidlo viditelnosti",
        ["VisibilityChoice"] = "Volba viditelnosti",
        ["RetentionPolicy"] = "Pravidla výmazu",
        ["StaffHandover"] = "Předání agendy",
        ["AccountLink"] = "Propojení účtů",
        ["AccountLinkRequest"] = "Žádost o propojení účtů",
        ["Role"] = "Role",
        ["RolePermission"] = "Oprávnění role",
    };

    private static readonly string[] Names = ["Name", "Title", "Username", "Subject", "Body"];

    public const int MaxRecordLength = 60;

    public static string Type(IEntityType type) =>
        Types.GetValueOrDefault(type.ClrType == typeof(Dictionary<string, object>) ? type.Name : type.ClrType.Name)
        ?? type.ClrType.Name;

    /// <summary>A name, title… of the row, else its key ("#12").</summary>
    public static string Record(JsonObject values, string keyJson)
    {
        var first = Text(values, "FirstName");
        var last = Text(values, "LastName");
        var name = first is not null || last is not null
            ? $"{first} {last}".Trim()
            : Names.Select(n => Text(values, n)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (name is not null)
        {
            return name.Length > MaxRecordLength ? name[..(MaxRecordLength - 1)] + "…" : name;
        }
        var key = JsonNode.Parse(keyJson)!.AsObject();
        return "#" + string.Join("/", key.Select(p => p.Value?.ToString()));
    }

    private static string? Text(JsonObject values, string name) =>
        values.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>The relative paths (<see cref="Messaging.MessageAttachmentStorageService"/>)
    /// of the files the row holds.</summary>
    public static IEnumerable<string> Files(IEntityType type, JsonObject values)
    {
        if (Text(values, "StoragePath") is { } path
            && type.ClrType.Name is "MessageAttachment" or "ExcuseAttachment" or "HomeworkAttachment")
        {
            yield return path;
        }
        if (type.ClrType == typeof(Person) && Text(values, nameof(Person.PhotoKey)) is { } photo)
        {
            foreach (var file in PhotoStorage.Files(photo))
            {
                yield return file;
            }
        }
    }

    /// <summary>A row still holds the file.</summary>
    public static async Task<bool> IsHeldAsync(AppDbContext db, string path)
    {
        if (await db.MessageAttachments.IgnoreQueryFilters().AnyAsync(a => a.StoragePath == path)
            || await db.ExcuseAttachments.IgnoreQueryFilters().AnyAsync(a => a.StoragePath == path)
            || await db.HomeworkAttachments.IgnoreQueryFilters().AnyAsync(a => a.StoragePath == path))
        {
            return true;
        }
        var photo = PhotoStorage.KeyOf(path);
        return photo is not null && await db.Persons.IgnoreQueryFilters().AnyAsync(p => p.PhotoKey == photo);
    }
}
