using Microsoft.EntityFrameworkCore;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;

namespace Sova.Api.Matrika;

/// <summary>
/// Builds the "základní soubor" (V&lt;IZO&gt;_&lt;CAST&gt;.xml, here always
/// prefixed "Z" for základní škola) for the školní matrika individual-data
/// export, per MŠMT's "Informace a metodické poznámky k předávání
/// individuálních údajů ze školních matrik" (2026/27):
/// https://msmt.gov.cz/media/wp-content/uploads/2026/09/2026_metodicke_poznamky.pdf
///
/// This is a SKELETON, not a submission-ready generator:
/// - Items derived with confidence from data Sova actually stores: RDAT,
///   IZO, CAST, RODC, POHLAVI, DAT_NAROZ, ROCNIK, TRIDA — plus, as optional
///   pass-through items (only emitted when present on the student), KSTPR,
///   STPR, OBECB, OKRESB.
/// - POHLAVI's RAPO code ("1"=muž, "2"=žena) is now CONFIRMED against the
///   real RAPO číselník (sberdat.uiv.cz/login/ciselniky/ciselniky.xls) — see
///   ROADMAP.md.
/// - Everything else the real "základní soubor" requires (ZPUSOB, ST_SKOLY,
///   ZAHDAT/KOD_ZAH, UKONDAT/KOD_UKON, JAZYK_O, ...) is still not produced —
///   needs more data capture on Student/SchoolClass, not missing číselníky.
/// - The anonymized "a" (SVP/nadání/IVP) and "b" (podpůrná opatření) files
///   aren't built at all yet.
/// - The export always treats the school as a single-part ZŠ ("Z" prefix,
///   CAST "01" by default) — multi-part schools and other school types
///   (SŠ/MŠ/konzervatoř/VOŠ) aren't modeled.
/// Verify every one of these against the real test server (profa.msmt.cz)
/// once a pilot school's RED_IZO/IZO is available — see ROADMAP.md.
/// </summary>
public class MatrikaExportService(AppDbContext db, ICurrentSchoolContext currentSchool)
{
    public async Task<MatrikaExportOutcome> BuildBaseFileAsync(
        DateOnly rdat, string cast, string contactName, string contactPhone, string contactEmail)
    {
        // School has no per-tenant query filter (it IS the tenant), so this
        // must be scoped explicitly — an unscoped FirstOrDefaultAsync() here
        // would hand back whichever school happens to be first in the table.
        var school = currentSchool.SchoolId is { } schoolId
            ? await db.Schools.FindAsync(schoolId)
            : null;
        if (school is null)
        {
            return MatrikaExportOutcome.Failed(["Škola nenalezena."]);
        }

        if (string.IsNullOrWhiteSpace(school.Izo))
        {
            return MatrikaExportOutcome.Failed(["Škole chybí IZO — doplňte ho v nastavení školy."]);
        }

        var headerErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(cast))
        {
            headerErrors.Add("Chybí číslo části (CAST).");
        }

        if (string.IsNullOrWhiteSpace(contactName))
        {
            headerErrors.Add("Chybí kontaktní osoba.");
        }

        if (string.IsNullOrWhiteSpace(contactPhone))
        {
            headerErrors.Add("Chybí telefon na kontaktní osobu.");
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            headerErrors.Add("Chybí e-mail na kontaktní osobu.");
        }

        if (headerErrors.Count > 0)
        {
            return MatrikaExportOutcome.Failed(headerErrors);
        }

        var students = await db.Students
            .Include(s => s.SchoolClass)
            .Include(s => s.Municipality)
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToListAsync();

        var errors = new List<string>();
        var records = new List<MatrikaStudentRecord>();

        foreach (var student in students)
        {
            var who = $"{student.LastName} {student.FirstName} (#{student.Id})";

            if (student.SchoolClass?.Rocnik is null)
            {
                errors.Add($"{who}: třídě '{student.SchoolClass?.Name}' chybí ročník.");
                continue;
            }

            if (student.Gender is null)
            {
                errors.Add($"{who}: chybí pohlaví.");
                continue;
            }

            if (student.DateOfBirth is null)
            {
                errors.Add($"{who}: chybí datum narození.");
                continue;
            }

            var rodc = string.IsNullOrWhiteSpace(student.RodneCislo)
                ? BuildPseudoRodneCislo(student.DateOfBirth.Value, student.Gender.Value, student.Id)
                : student.RodneCislo;

            records.Add(new MatrikaStudentRecord(
                Rdat: rdat.ToString("d.M.yyyy"),
                Izo: school.Izo,
                Cast: cast,
                Rodc: rodc,
                Pohlavi: student.Gender.Value == Gender.Female ? "2" : "1",
                DatNaroz: BuildDatNaroz(student.DateOfBirth.Value),
                Rocnik: student.SchoolClass!.Rocnik!.Value.ToString(),
                Trida: student.SchoolClass.Name,
                Kstpr: student.CitizenshipQualifier,
                Stpr: student.CountryCode,
                Obecb: student.MunicipalityCode,
                // OKRESB is derived, not stored directly — see Municipality.cs.
                Okresb: student.Municipality?.DistrictCode));
        }

        if (errors.Count > 0)
        {
            return MatrikaExportOutcome.Failed(errors);
        }

        var fileName = $"Z{school.Izo}_{cast}.xml";
        var xml = MatrikaXmlWriter.Write(
            Path.GetFileNameWithoutExtension(fileName), contactName, contactPhone, contactEmail, records);

        return MatrikaExportOutcome.Succeeded(fileName, xml);
    }

    /// <summary>RODC construction per the documented rule: RRNNDD + "X" + a
    /// 3-digit sequence the school assigns uniquely to the student. RR/DD from
    /// the birth date; NN is the birth month, +50 for girls.</summary>
    private static string BuildPseudoRodneCislo(DateOnly dob, Gender gender, int studentId)
    {
        var yy = (dob.Year % 100).ToString("D2");
        var monthNumber = gender == Gender.Female ? dob.Month + 50 : dob.Month;
        var mm = monthNumber.ToString("D2");
        var dd = dob.Day.ToString("D2");
        var seq = (studentId % 1000).ToString("D3");
        return $"{yy}{mm}{dd}X{seq}";
    }

    /// <summary>DAT_NAROZ per the documented privacy-reduction rule: year plus a
    /// literal "aa" (born January-August) or "bb" (born September-December) —
    /// the exact month is not transmitted.</summary>
    private static string BuildDatNaroz(DateOnly dob) =>
        dob.Month <= 8 ? $"{dob.Year}aa" : $"{dob.Year}bb";
}

public record MatrikaExportOutcome(bool Success, string? FileName, byte[]? Content, IReadOnlyList<string> Errors)
{
    public static MatrikaExportOutcome Succeeded(string fileName, byte[] content) =>
        new(true, fileName, content, []);

    public static MatrikaExportOutcome Failed(IReadOnlyList<string> errors) =>
        new(false, null, null, errors);
}
