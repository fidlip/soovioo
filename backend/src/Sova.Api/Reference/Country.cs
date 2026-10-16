namespace Sova.Api.Reference;

/// <summary>Matrika's RAST číselník (states/countries) — the STPR item's code
/// list. Global reference data, shared across every school (not tenant-scoped,
/// no SchoolId). Seeded once at startup from Data/Seed/countries.csv, itself
/// extracted from MŠMT's own published codelist workbook
/// (sberdat.uiv.cz/login/ciselniky/ciselniky.xls) — see ROADMAP.md.</summary>
public class Country
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}
