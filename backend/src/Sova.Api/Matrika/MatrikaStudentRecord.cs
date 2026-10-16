namespace Sova.Api.Matrika;

/// <summary>One matrika "věta" (record) for a single student, holding only the
/// items <see cref="MatrikaExportService"/> currently derives with confidence.
/// See the comment block at the top of that class for what's still missing.</summary>
public record MatrikaStudentRecord(
    string Rdat,
    string Izo,
    string Cast,
    string Rodc,
    string Pohlavi,
    string DatNaroz,
    string Rocnik,
    string Trida,
    // KSTPR/STPR/OBECB/OKRESB are optional pass-through fields: only emitted
    // into the XML when the student record actually has them, rather than
    // being required for export to succeed (unlike the fields above).
    string? Kstpr = null,
    string? Stpr = null,
    string? Obecb = null,
    string? Okresb = null);
