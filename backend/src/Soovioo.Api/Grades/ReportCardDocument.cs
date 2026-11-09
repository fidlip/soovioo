using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

/// <summary>Vysvědčení as a PDF, a page per pupil. Only confirmed report
/// card grades are printed; the overall result once all are.</summary>
public static class ReportCardDocument
{
    public static byte[] Generate(
        School? school,
        IReadOnlyList<ReportCard> cards,
        IReadOnlyDictionary<int, string> classTeachers,
        int schoolYearStartYear,
        SchoolHalfYear half)
    {
        var schoolType = school?.Type ?? SchoolType.Elementary;
        var issuedOn = HalfYears.IssuedOn(schoolYearStartYear, half);
        return Document.Create(container =>
        {
            foreach (var card in cards)
            {
                var student = card.Student;
                var female = student.Gender == Gender.Female;
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Column(column =>
                    {
                        // Falls back to the app name when the school hasn't
                        // filled in its own details yet (MySchoolController).
                        column.Item().Text(school?.Name ?? "Soovioo — školní informační systém").FontSize(10);
                        if (school is { Street: not null, City: not null })
                        {
                            column.Item().Text($"{school.Street}, {school.PostalCode} {school.City}").FontSize(9);
                        }
                        column.Item().PaddingTop(10).Text("Vysvědčení").FontSize(22).Bold();
                        column.Item().Text(
                            $"Školní rok {schoolYearStartYear}/{schoolYearStartYear + 1} · {(half == SchoolHalfYear.First ? "1" : "2")}. pololetí");
                    });

                    page.Content().PaddingTop(20).Column(column =>
                    {
                        column.Spacing(4);
                        Field(column, StudentTerm.Localize("Žák", schoolType), student.Person!.FullName);
                        if (student.DateOfBirth is { } born)
                        {
                            Field(column, "Datum narození", SchoolFormat.Date(born));
                        }
                        Field(column, "Třída", student.SchoolClass?.Name ?? "");
                        Field(column, "Chování", Behavior(card.Behavior));

                        column.Item().PaddingTop(12).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });
                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).PaddingBottom(3).Text("Předmět").Bold();
                                header.Cell().BorderBottom(1).PaddingBottom(3).Text("Hodnocení").Bold();
                            });
                            foreach (var subject in card.Subjects)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(subject.SubjectName);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3)
                                    .Text(subject.Confirmed ? Mark(subject.Kind!.Value, subject.Value, female) : "");
                            }
                        });

                        column.Item().PaddingTop(12);
                        Field(column, "Celkové hodnocení", card.Result(half) is { } result ? Result(result, female) : "");
                        Field(column, "Zameškané hodiny",
                            $"{card.ExcusedHours + card.UnexcusedHours}, z toho neomluvených {card.UnexcusedHours}");

                        column.Item().PaddingTop(30);
                        Field(column, "Datum vydání", SchoolFormat.Date(issuedOn));

                        column.Item().PaddingTop(50).Row(row =>
                        {
                            Signature(row.RelativeItem(), "třídní učitel/ka", classTeachers.GetValueOrDefault(student.SchoolClassId));
                            row.ConstantItem(40);
                            Signature(row.RelativeItem(), "ředitel/ka školy", school?.DirectorName);
                        });
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

    public static string Mark(ReportCardGradeKind kind, int? value, bool female) => kind switch
    {
        ReportCardGradeKind.Exempt => female ? "uvolněna" : "uvolněn",
        ReportCardGradeKind.NotGraded => female ? "nehodnocena" : "nehodnocen",
        _ => value switch
        {
            1 => "výborný",
            2 => "chvalitebný",
            3 => "dobrý",
            4 => "dostatečný",
            _ => "nedostatečný",
        },
    };

    private static string Result(ReportCardResult result, bool female) => result switch
    {
        ReportCardResult.WithHonours => female ? "prospěla s vyznamenáním" : "prospěl s vyznamenáním",
        ReportCardResult.Passed => female ? "prospěla" : "prospěl",
        ReportCardResult.Failed => female ? "neprospěla" : "neprospěl",
        _ => female ? "nehodnocena" : "nehodnocen",
    };

    private static string Behavior(BehaviorClassificationValue value) => value switch
    {
        BehaviorClassificationValue.VelmiDobre => "velmi dobré",
        BehaviorClassificationValue.Uspokojive => "uspokojivé",
        _ => "neuspokojivé",
    };

    private static void Field(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(130).Text(label + ":").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).Bold();
        });

    private static void Signature(IContainer container, string role, string? name) =>
        container.Column(column =>
        {
            column.Item().BorderTop(1).PaddingTop(3).Text(name ?? "").FontSize(10);
            column.Item().Text(role).FontSize(9).FontColor(Colors.Grey.Darken1);
        });
}
