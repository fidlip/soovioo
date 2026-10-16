namespace Sova.Api.Reference;

/// <summary>Matrika's RAUJ číselník (obce, základní územní jednotky) — the
/// OBECB item's code list. Each row already carries its district code, so
/// picking a municipality is enough to derive OKRESB too — no separate
/// district picker needed anywhere in the app. Global reference data (not
/// tenant-scoped). Seeded once at startup from Data/Seed/municipalities.csv
/// — see Country.cs for provenance.</summary>
public class Municipality
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string DistrictCode { get; set; }
    public District? District { get; set; }
}
