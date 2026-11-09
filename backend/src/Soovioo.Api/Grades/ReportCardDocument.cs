using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

/// <summary>A page per pupil. 1. pololetí: výpis z vysvědčení (§ 3 of
/// vyhláška 3/2015 Sb.) — plain paper, no stamp, the name and signature of
/// whoever issues it (the třídní). 2. pololetí: no výpis may be issued then,
/// the report card goes on the official form (#262) — so only its
/// particulars to fill it in from. Only confirmed report card grades are
/// printed; the overall result once all are.</summary>
public static class ReportCardDocument
{
    private static readonly string[] MonthsGenitive =
    [
        "ledna", "února", "března", "dubna", "května", "června",
        "července", "srpna", "září", "října", "listopadu", "prosince",
    ];

    private static readonly string[] GradeWords = ["výborný", "chvalitebný", "dobrý", "dostatečný", "nedostatečný"];

    private static readonly string[] BehaviorWords = ["velmi dobré", "uspokojivé", "neuspokojivé"];

    public static byte[] Generate(
        School? school,
        IReadOnlyList<ReportCard> cards,
        IReadOnlyDictionary<int, string> classTeachers,
        int schoolYearStartYear,
        SchoolHalfYear half)
    {
        var schoolType = school?.Type ?? SchoolType.Elementary;
        var format = school?.ReportCardMarkFormat ?? ReportCardMarkFormat.Words;
        var extract = half == SchoolHalfYear.First;
        var issuedOn = HalfYears.IssuedOn(schoolYearStartYear, half);
        return Document.Create(container =>
        {
            foreach (var card in cards)
            {
                var student = card.Student;
                var female = student.Gender == Gender.Female;
                var classTeacher = classTeachers.GetValueOrDefault(student.SchoolClassId);
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(column =>
                    {
                        column.Item().Text("ČESKÁ REPUBLIKA").FontSize(9).LetterSpacing(0.1f);
                        // Falls back to the app name when the school hasn't
                        // filled in its own details yet (MySchoolController).
                        column.Item().Text(school?.Name ?? "Soovioo — školní informační systém").Bold();
                        if (school is { Street: not null, City: not null })
                        {
                            column.Item().Text($"{school.Street}, {school.PostalCode} {school.City}").FontSize(9);
                        }
                        if (school?.Izo is { } izo)
                        {
                            column.Item().Text($"IZO: {izo}").FontSize(9);
                        }
                        column.Item().PaddingTop(12).Text(extract ? "Výpis z vysvědčení" : "Podklady k vysvědčení")
                            .FontSize(22).Bold();
                        column.Item().Text(
                            $"Školní rok {schoolYearStartYear}/{schoolYearStartYear + 1} · {(extract ? "I" : "II")}. pololetí");
                        if (!extract)
                        {
                            column.Item().PaddingTop(4).Text(
                                    "Nejde o vysvědčení: za 2. pololetí se vysvědčení vydává na tiskopisu, výpis z vysvědčení se nevydává.")
                                .FontSize(9).Italic().FontColor(Colors.Grey.Darken2);
                        }
                    });

                    page.Content().PaddingTop(16).Column(column =>
                    {
                        column.Spacing(4);
                        Field(column, "Jméno a příjmení", student.Person!.FullName);
                        if (student.DateOfBirth is { } born)
                        {
                            Field(column, "Datum narození", SchoolFormat.Date(born));
                        }
                        if (student.RodneCislo is { } rodneCislo)
                        {
                            Field(column, "Rodné číslo", rodneCislo);
                        }
                        Field(column, "Třída", student.SchoolClass?.Name ?? "");
                        if (student.SchoolClass?.Rocnik is { } rocnik)
                        {
                            Field(column, "Ročník", $"{rocnik}.");
                        }

                        column.Item().PaddingTop(12).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });
                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).PaddingBottom(3).Text(StudentTerm.Localize("Chování žáka", schoolType)).Bold();
                                header.Cell().BorderBottom(1).PaddingBottom(3).Text(Behavior(card.Behavior, format));
                            });
                            table.Cell().ColumnSpan(2).PaddingTop(10).BorderBottom(1).PaddingBottom(3)
                                .Text("Hodnocení v jednotlivých předmětech").Bold();
                            foreach (var subject in card.Subjects)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(subject.SubjectName);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3)
                                    .Text(subject.Confirmed ? Mark(subject.Kind!.Value, subject.Value, female, format) : "");
                            }
                        });

                        column.Item().PaddingTop(12);
                        Field(column, "Celkové hodnocení", card.Result(half) is { } result ? Result(result, female) : "");
                        Field(column, "Zameškané hodiny",
                            $"{card.ExcusedHours + card.UnexcusedHours}, z toho neomluvených {card.UnexcusedHours}");

                        column.Item().PaddingTop(14).Text(text =>
                        {
                            text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                            text.Line("Stupně hodnocení prospěchu: 1 – výborný, 2 – chvalitebný, 3 – dobrý, 4 – dostatečný, 5 – nedostatečný.");
                            text.Line("Stupně hodnocení chování: 1 – velmi dobré, 2 – uspokojivé, 3 – neuspokojivé.");
                        });

                        if (extract)
                        {
                            column.Item().PaddingTop(20);
                            Field(column, "Datum", LongDate(issuedOn));
                            column.Item().PaddingTop(45).Width(220).Column(signature =>
                            {
                                signature.Item().BorderTop(1).PaddingTop(3).Text(classTeacher ?? "").FontSize(10);
                                signature.Item().Text("výpis vystavil/a").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                        text.Span("Vygenerováno: ");
                        text.Span(SchoolFormat.Date(SchoolClock.Today(TimeProvider.System)));
                    });
                });
            }
        }).GeneratePdf();
    }

    public static string Mark(ReportCardGradeKind kind, int? value, bool female, ReportCardMarkFormat format) => kind switch
    {
        ReportCardGradeKind.Exempt => female ? "uvolněna" : "uvolněn",
        ReportCardGradeKind.NotGraded => female ? "nehodnocena" : "nehodnocen",
        _ => format == ReportCardMarkFormat.Digits ? $"{value}" : GradeWords[value!.Value - 1],
    };

    /// <summary>"31. ledna 2027" — the month as a word in the genitive, as
    /// MŠMT's guidance has the date of signing.</summary>
    public static string LongDate(DateOnly day) => $"{day.Day}. {MonthsGenitive[day.Month - 1]} {day.Year}";

    private static string Result(ReportCardResult result, bool female) => result switch
    {
        ReportCardResult.WithHonours => female ? "prospěla s vyznamenáním" : "prospěl s vyznamenáním",
        ReportCardResult.Passed => female ? "prospěla" : "prospěl",
        ReportCardResult.Failed => female ? "neprospěla" : "neprospěl",
        _ => female ? "nehodnocena" : "nehodnocen",
    };

    private static string Behavior(BehaviorClassificationValue value, ReportCardMarkFormat format)
    {
        var index = value switch
        {
            BehaviorClassificationValue.VelmiDobre => 0,
            BehaviorClassificationValue.Uspokojive => 1,
            _ => 2,
        };
        return format == ReportCardMarkFormat.Digits ? $"{index + 1}" : BehaviorWords[index];
    }

    private static void Field(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(130).Text(label + ":").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).Bold();
        });
}
