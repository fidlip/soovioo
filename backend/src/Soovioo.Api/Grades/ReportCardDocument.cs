using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Soovioo.Api.Evidence;
using Soovioo.Api.Common;

namespace Soovioo.Api.Grades;

public static class ReportCardDocument
{
    public static byte[] Generate(
        StudentProfile student, School? school, IReadOnlyList<(string Subject, double Average)> grades)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Column(column =>
                {
                    column.Item().Text("Vysvědčení").FontSize(20).Bold();
                    // Falls back to the app name when the school hasn't
                    // filled in its own details yet (MySchoolController).
                    column.Item().Text(school?.Name ?? "Soovioo — školní informační systém").FontSize(10);
                    if (school is { Street: not null, City: not null })
                    {
                        column.Item().Text($"{school.Street}, {school.PostalCode} {school.City}").FontSize(9);
                    }
                    if (school?.DirectorName is { } director)
                    {
                        column.Item().Text($"Ředitel/ka: {director}").FontSize(9);
                    }
                });

                page.Content().PaddingTop(15).Column(column =>
                {
                    column.Item().Text(StudentTerm.Localize("Žák", school?.Type ?? SchoolType.Elementary) + $": {student.Person!.FullName}");
                    column.Item().Text($"Třída: {student.SchoolClass?.Name}");
                    column.Item().PaddingTop(15);

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Předmět").Bold();
                            header.Cell().Text("Průměr").Bold();
                        });

                        foreach (var (subject, average) in grades)
                        {
                            table.Cell().Text(subject);
                            table.Cell().Text(SchoolFormat.Number(average, "0.0"));
                        }
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Vygenerováno: ");
                    text.Span(SchoolFormat.Date(SchoolClock.Today(TimeProvider.System)));
                });
            });
        }).GeneratePdf();
    }
}
