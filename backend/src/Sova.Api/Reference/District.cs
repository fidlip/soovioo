namespace Sova.Api.Reference;

/// <summary>Matrika's RAOR číselník (okresy, NUTS4) — the OKRESB item's code
/// list. Global reference data (not tenant-scoped). Seeded once at startup
/// from Data/Seed/districts.csv — see Country.cs for provenance.</summary>
public class District
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}
