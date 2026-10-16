using System.Text;
using System.Xml.Linq;

namespace Sova.Api.Matrika;

/// <summary>
/// Serializes the "základní soubor" envelope + records. The header shape
/// (Vykaz/Vygen/autor/telefon/e-mail/soubor/vytvoreno) is copied verbatim from
/// the documented example in MŠMT's 2026/27 "Informace a metodické poznámky
/// k předávání individuálních údajů ze školních matrik". The &lt;Veta&gt; record
/// wrapper is this module's own naming choice — the document uses "věta" as the
/// term for a record row but doesn't show the actual element name anywhere we
/// could find. Confirm it against a real sample file before a real submission.
/// </summary>
public static class MatrikaXmlWriter
{
    // Best-known current ZŠ structure version from public search results; we
    // were not able to open the authoritative ZS.026 structure PDF directly
    // (dead links on msmt.gov.cz at the time of writing). Reconfirm against
    // "Datová rozhraní pro předávání dat" before a real submission.
    private const string FormatVersion = "ZS.026";

    public static byte[] Write(
        string fileNameWithoutExtension,
        string contactName,
        string contactPhone,
        string contactEmail,
        IReadOnlyList<MatrikaStudentRecord> records)
    {
        var root = new XElement(
            "Vykaz",
            new XAttribute("verze", FormatVersion),
            new XElement("Vygen", "Sova"),
            new XElement("autor", contactName),
            new XElement("telefon", contactPhone),
            new XElement("e-mail", contactEmail),
            new XElement("soubor", fileNameWithoutExtension),
            new XElement("vytvoreno", DateTime.Now.ToString("d.M.yyyy H:mm:ss")));

        foreach (var record in records)
        {
            var veta = new XElement(
                "Veta",
                new XElement("RDAT", record.Rdat),
                new XElement("IZO", record.Izo),
                new XElement("CAST", record.Cast),
                new XElement("RODC", record.Rodc),
                new XElement("POHLAVI", record.Pohlavi),
                new XElement("DAT_NAROZ", record.DatNaroz),
                new XElement("ROCNIK", record.Rocnik),
                new XElement("TRIDA", record.Trida));

            // Optional pass-through items: only written when the student
            // actually has them on file (see MatrikaStudentRecord).
            if (!string.IsNullOrWhiteSpace(record.Kstpr))
            {
                veta.Add(new XElement("KSTPR", record.Kstpr));
            }

            if (!string.IsNullOrWhiteSpace(record.Stpr))
            {
                veta.Add(new XElement("STPR", record.Stpr));
            }

            if (!string.IsNullOrWhiteSpace(record.Obecb))
            {
                veta.Add(new XElement("OBECB", record.Obecb));
            }

            if (!string.IsNullOrWhiteSpace(record.Okresb))
            {
                veta.Add(new XElement("OKRESB", record.Okresb));
            }

            root.Add(veta);
        }

        var doc = new XDocument(new XDeclaration("1.0", "windows-1250", null), root);

        // Windows-1250, per the documented header's `encoding="Windows-1250"`.
        // Needs System.Text.Encoding.CodePages registered — see Program.cs.
        var encoding = Encoding.GetEncoding("Windows-1250");
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, encoding, leaveOpen: true))
        {
            doc.Save(writer);
        }

        return stream.ToArray();
    }
}
