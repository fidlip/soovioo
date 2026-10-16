using Sova.Api.Evidence;

namespace Sova.Api.Events;

public class Event
{
    public int Id { get; set; }

    public int SchoolId { get; set; }
    public School? School { get; set; }

    /// <summary>Null = celoškolní akce. Vyplněné = akce týkající se jedné
    /// třídy, ale stejně jako u zpráv zůstává viditelná celé škole (je to
    /// nástěnka, ne soukromá schránka) — slouží jen jako popisek/filtr.</summary>
    public int? SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }

    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public bool AllDay { get; set; }

    /// <summary>"#rrggbb", nullable so pre-existing rows (and the ICS feed,
    /// which has no color concept) fall back to the calendar's default.</summary>
    public string? Color { get; set; }

    public int CreatedByUserId { get; set; }
}
