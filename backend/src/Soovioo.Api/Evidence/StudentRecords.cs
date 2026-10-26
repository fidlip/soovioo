using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Evidence;

/// <summary>A pupil's matrika as one person's profile — written the same way
/// from Žáci (<see cref="StudentsController"/>) and from Osoby
/// (<see cref="People.PeopleController"/>).</summary>
public static class StudentRecords
{
    public const string VariableSymbolTaken = "Tento variabilní symbol už má jiný žák.";

    /// <summary>Copies the matrika onto <paramref name="student"/>; an empty
    /// variable symbol keeps the current one or generates a new one. Returns
    /// <see cref="VariableSymbolTaken"/> when another pupil has the one
    /// asked for (nothing else is checked here).</summary>
    public static async Task<string?> ApplyAsync(AppDbContext db, StudentProfile student, StudentMatrika matrika)
    {
        int? exceptId = student.Id == 0 ? null : student.Id;
        string variableSymbol;
        if (!string.IsNullOrWhiteSpace(matrika.DefaultVariableSymbol))
        {
            variableSymbol = matrika.DefaultVariableSymbol.Trim();
            if (await IsVariableSymbolTakenAsync(db, variableSymbol, exceptId))
            {
                return VariableSymbolTaken;
            }
        }
        else if (student.DefaultVariableSymbol is { } current)
        {
            variableSymbol = current;
        }
        else
        {
            // Another pupil added at the same moment may have just taken it.
            do
            {
                variableSymbol = await VariableSymbols.NextAsync(db, student.SchoolId, DateTime.Today.Year);
            }
            while (await IsVariableSymbolTakenAsync(db, variableSymbol, exceptId));
        }

        student.SchoolClassId = matrika.SchoolClassId;
        student.RodneCislo = matrika.RodneCislo;
        student.DateOfBirth = matrika.DateOfBirth;
        student.Gender = matrika.Gender;
        student.CitizenshipQualifier = matrika.CitizenshipQualifier;
        student.CountryCode = matrika.CountryCode;
        student.Street = matrika.Street;
        student.MunicipalityCode = matrika.MunicipalityCode;
        student.PostalCode = matrika.PostalCode;
        student.CatchmentArea = matrika.CatchmentArea;
        student.EnrollmentDate = matrika.EnrollmentDate;
        student.ContactPhone = matrika.ContactPhone;
        student.ContactEmail = matrika.ContactEmail;
        student.DefaultVariableSymbol = variableSymbol;
        return null;
    }

    /// <summary>What makes the pupil more than a mistaken entry, as
    /// "12 známek", "3 absence"… (empty = none).</summary>
    public static async Task<List<string>> HistoryOfAsync(AppDbContext db, StudentProfile student)
    {
        var id = student.Id;
        var ownId = student.Person!.UserId;
        var counts = new (int Count, string One, string Few, string Many)[]
        {
            (await db.Grades.CountAsync(g => g.StudentId == id), "známku", "známky", "známek"),
            (await db.Absences.CountAsync(a => a.StudentId == id), "absenci", "absence", "absencí"),
            (await db.BehaviorRecords.CountAsync(r => r.StudentId == id)
                + await db.BehaviorClassifications.CountAsync(c => c.StudentId == id),
                "záznam chování", "záznamy chování", "záznamů chování"),
            (await db.HomeworkCompletions.CountAsync(c => c.StudentId == id), "splněný úkol", "splněné úkoly", "splněných úkolů"),
            (await db.PaymentRecipients.CountAsync(r => r.StudentId == id && r.Paid), "zaplacenou platbu", "zaplacené platby", "zaplacených plateb"),
            (await db.Conversations.CountAsync(c => c.StudentId == id)
                + (ownId is null ? 0 : await db.Messages.CountAsync(m => m.SenderUserId == ownId)),
                "zprávu", "zprávy", "zpráv"),
        };
        return counts
            .Where(c => c.Count > 0)
            .Select(c => $"{c.Count} {(c.Count == 1 ? c.One : c.Count < 5 ? c.Few : c.Many)}")
            .ToList();
    }

    /// <summary>The 409 text for a pupil with <paramref name="history"/>.</summary>
    public static async Task<string> RefusalAsync(AppDbContext db, StudentProfile student, List<string> history)
    {
        var type = await db.Schools.Where(s => s.Id == student.SchoolId).Select(s => (SchoolType?)s.Type).FirstOrDefaultAsync()
            ?? SchoolType.Elementary;
        return StudentTerm.Localize(
            $"Žáka nejde smazat, už má {string.Join(", ", history)}. Místo smazání ukončete jeho vzdělávání.", type);
    }

    /// <summary>Deletes a pupil without history (see <see cref="HistoryOfAsync"/>)
    /// with what they got on their own — parent links, unpaid installments
    /// of school-wide payments, their Person, photo and own account (which
    /// never wrote anything). Saves.</summary>
    public static async Task DeleteAsync(AppDbContext db, StudentProfile student, People.PhotoStorage? photos)
    {
        var id = student.Id;
        await db.DeleteAsync(db.PaymentRecipients.Where(r => r.StudentId == id));
        await db.DeleteAsync(db.PaymentStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.HomeworkStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.EventStudents.Where(x => x.StudentId == id));
        await db.DeleteAsync(db.ParentStudents.Where(ps => ps.StudentId == id));
        var person = student.Person!;
        await db.DeleteAsync(db.VisibilityChoices.Where(c => c.PersonId == person.Id));
        if (person.UserId is { } linkedUserId && await db.Users.FindAsync(linkedUserId) is { } linkedUser)
        {
            db.Users.Remove(linkedUser);
        }
        db.Students.Remove(student);
        db.Persons.Remove(person);
        await db.SaveChangesAsync();
        photos?.Delete(person.PhotoKey);
    }

    // Country/Municipality names are looked up by code rather than via
    // navigation properties, since student.Country/student.Municipality
    // aren't populated after Add/Update without an extra Include — these
    // two single-row lookups are cheaper than that.
    public static async Task<StudentDto> ToDtoAsync(AppDbContext db, StudentProfile student)
    {
        var className = student.SchoolClass?.Name
            ?? await db.SchoolClasses.Where(c => c.Id == student.SchoolClassId).Select(c => c.Name).FirstAsync();
        var countryName = student.CountryCode is null
            ? null
            : await db.Countries.Where(c => c.Code == student.CountryCode).Select(c => c.Name).FirstOrDefaultAsync();
        var municipalityName = student.MunicipalityCode is null
            ? null
            : await db.Municipalities.Where(m => m.Code == student.MunicipalityCode).Select(m => m.Name).FirstOrDefaultAsync();

        return new StudentDto(
            student.Id, student.Person!.FirstName, student.Person.LastName, student.SchoolClassId, className,
            student.RodneCislo, student.DateOfBirth, student.Gender, student.CitizenshipQualifier,
            student.CountryCode, countryName, student.Street, student.MunicipalityCode, municipalityName,
            student.PostalCode, student.CatchmentArea, student.EnrollmentDate, student.ContactPhone,
            student.ContactEmail, student.DefaultVariableSymbol, student.LeftAt, student.LeaveReason, student.LeaveReasonDetail, student.LeaveNote);
    }

    private static Task<bool> IsVariableSymbolTakenAsync(AppDbContext db, string variableSymbol, int? exceptStudentId) =>
        db.Students.AnyAsync(s => s.DefaultVariableSymbol == variableSymbol && s.Id != exceptStudentId);
}

/// <summary>The matrika part of a pupil — everything but the name, which is
/// the Person's.</summary>
public record StudentMatrika(
    int SchoolClassId,
    string? RodneCislo = null,
    DateOnly? DateOfBirth = null,
    Gender? Gender = null,
    string? CitizenshipQualifier = null,
    string? CountryCode = null,
    string? Street = null,
    string? MunicipalityCode = null,
    string? PostalCode = null,
    bool? CatchmentArea = null,
    DateOnly? EnrollmentDate = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    string? DefaultVariableSymbol = null);
