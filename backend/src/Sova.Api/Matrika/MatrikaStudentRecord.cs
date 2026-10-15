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
    string Trida);
