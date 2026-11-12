using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Soovioo.Api.Behavior;
using Soovioo.Api.Common;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Grades;

/// <summary>Vysvědčení list A (slovní hodnocení) and list B for the
/// základní škola, after the forms 1.4 and 1.5 of příloha č. 1 to vyhláška
/// 3/2015 Sb., to print on the school's blank forms (the grey underprint is
/// on the paper). A pupil assessed slovně in everything — behaviour too, or
/// the souhrnné hodnocení — gets list A, the slovní hodnocení going on in
/// lists B as long as it needs; a pupil assessed slovně only in some gets
/// just lists B with those, their list A (klasifikace) saying "hodnocen/a
/// slovně" (MŠMT's guidance, part 3). Each list is a sheet, front and back.
/// The particulars the app doesn't have yet (#263) stay dotted to fill in by
/// hand. Only confirmed grades and the confirmed summary are printed.</summary>
public static class VerbalReportCardDocument
{
    private const string Dots = "……………………";
    private const string Title = "Chování, povinné předměty, nepovinné předměty";

    public static byte[] Generate(School? school, IReadOnlyList<ReportCard> cards, int schoolYearStartYear, SchoolHalfYear half)
    {
        var issuedOn = HalfYears.IssuedOn(schoolYearStartYear, half);

        // Each pupil's pages first on their own: how many lists B there are
        // shows on list A, and a list ends with its back.
        var sheets = cards
            .Select(card =>
            {
                var counting = new Layout(school, card, schoolYearStartYear, half, issuedOn, Pages: 0, First: 1);
                Compose(counting, padded: false).GeneratePdf();
                var pages = Math.Max(2, counting.Counted + counting.Counted % 2);
                return (Card: card, Pages: pages, Padded: counting.Counted != pages);
            })
            .ToList();
        var first = 1;
        var layouts = new List<(Layout Layout, bool Padded)>();
        foreach (var (card, pages, padded) in sheets)
        {
            layouts.Add((new Layout(school, card, schoolYearStartYear, half, issuedOn, pages, first), padded));
            first += pages;
        }

        return Document.Create(container =>
        {
            foreach (var (layout, padded) in layouts)
            {
                Page(container, layout, padded);
            }
        }).GeneratePdf();
    }

    private static Document Compose(Layout layout, bool padded) =>
        Document.Create(container => Page(container, layout, padded));

    private static void Page(IDocumentContainer container, Layout layout, bool padded) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(1.6f, Unit.Centimetre);
            page.MarginVertical(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10.5f));

            page.Header().Dynamic(new Side(layout, Header: true));
            page.Content().Extend().BorderLeft(1).BorderRight(1).PaddingHorizontal(8).PaddingVertical(6).Column(column =>
            {
                column.Spacing(6);
                foreach (var (heading, text) in Blocks(layout.Card, layout.Half))
                {
                    column.Item().Text(heading).Bold();
                    if (text is not null)
                    {
                        column.Item().PaddingLeft(6).Text(text);
                    }
                }
                if (padded)
                {
                    column.Item().PageBreak();
                    column.Item().Height(1);
                }
            });
            page.Footer().Dynamic(new Side(layout, Header: false));
        });

    /// <summary>The headings and texts in the box: behaviour, then the
    /// souhrnné hodnocení or the subjects.</summary>
    private static IEnumerable<(string Heading, string? Text)> Blocks(ReportCard card, SchoolHalfYear half)
    {
        var female = card.Student.Gender == Gender.Female;
        if (card.BehaviorMode == AssessmentMode.Verbal)
        {
            yield return ("Chování", card.Note?.BehaviorText);
        }
        else if (card.BehaviorMode == AssessmentMode.ClassificationWithVerbal)
        {
            yield return ($"Chování: {ReportCardDocument.Behavior(card.Behavior, card.BehaviorMarkFormat)}", card.Note?.BehaviorText);
        }

        if (card.Summarised(half))
        {
            yield return ("Souhrnné hodnocení", card.SummaryConfirmed ? card.Note!.SummaryText : null);
            yield break;
        }

        var listA = ListA(card, half);
        foreach (var subject in card.Subjects)
        {
            if (!listA && subject.Mode == AssessmentMode.Classification)
            {
                continue;
            }
            if (!subject.Confirmed)
            {
                yield return (subject.SubjectName, null);
                continue;
            }
            var mark = subject.Mode == AssessmentMode.Verbal && subject.Kind == ReportCardGradeKind.Graded
                ? null
                : ReportCardDocument.Mark(subject.Kind!.Value, subject.Value, female, subject.MarkFormat);
            yield return (mark is null ? subject.SubjectName : $"{subject.SubjectName}: {mark}", subject.VerbalText);
        }
    }

    /// <summary>Slovně in everything: behaviour and every subject (a stupeň
    /// with a slovní hodnocení counts — MŠMT's guidance, 3.1.4), or the
    /// souhrnné hodnocení.</summary>
    private static bool ListA(ReportCard card, SchoolHalfYear half) =>
        card.BehaviorMode != AssessmentMode.Classification
        && (card.Summarised(half) || card.Subjects.All(s => s.Mode != AssessmentMode.Classification));

    /// <param name="Pages">The pupil's pages, a sheet's front and back each;
    /// 0 while counting them.</param>
    /// <param name="First">The page the pupil starts on in the whole
    /// document.</param>
    private sealed record Layout(
        School? School,
        ReportCard Card,
        int SchoolYearStartYear,
        SchoolHalfYear Half,
        DateOnly IssuedOn,
        int Pages,
        int First)
    {
        public bool ListA => VerbalReportCardDocument.ListA(Card, Half);

        /// <summary>The pages the counting run took.</summary>
        public int Counted { get; set; }
    }

    /// <summary>A page's header or footer by where it is on the pupil's
    /// sheets: list A's front and back, then each list B's.</summary>
    private sealed record Side(Layout Layout, bool Header) : IDynamicComponent
    {
        public DynamicComponentComposeResult Compose(DynamicContext context)
        {
            if (Layout.Pages == 0)
            {
                Layout.Counted = context.TotalPages;
            }
            var local = context.PageNumber - Layout.First;
            var listA = Layout.ListA;
            var sheet = local / 2;
            var front = local % 2 == 0;
            var isA = listA && sheet == 0;
            var listsB = Math.Max(0, Layout.Pages / 2 - (listA ? 1 : 0));
            var numberB = sheet + (listA ? 0 : 1);

            var content = context.CreateElement(container =>
            {
                if (Header)
                {
                    if (front)
                    {
                        FrontHeader(container, isA, numberB, listsB);
                    }
                    else
                    {
                        container.Column(column =>
                        {
                            if (isA)
                            {
                                TitleRow(column.Item(), Title, bold: true);
                            }
                            else
                            {
                                column.Item().BorderTop(1);
                            }
                        });
                    }
                }
                else if (front)
                {
                    container.Column(column =>
                    {
                        column.Item().BorderTop(1);
                        column.Item().AlignRight().Text(isA ? "ZŠ slovní hodnocení – list A" : "ZŠ – list B").FontSize(7);
                    });
                }
                else if (isA)
                {
                    BackOfListA(container);
                }
                else
                {
                    BackOfListB(container);
                }
            });
            return new DynamicComponentComposeResult { Content = content, HasMoreContent = false };
        }

        private void FrontHeader(IContainer container, bool isA, int numberB, int listsB)
        {
            var school = Layout.School;
            var student = Layout.Card.Student;
            container.Column(column =>
            {
                column.Item().AlignCenter().Text("ČESKÁ REPUBLIKA").FontSize(13).LetterSpacing(0.25f);
                column.Item().PaddingTop(2).AlignCenter().Text(school?.Name ?? Dots).FontSize(11);
                column.Item().PaddingTop(4).BorderBottom(1).PaddingBottom(2).Text($"IZO {school?.Izo}").FontSize(9);
                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Třída", student.SchoolClass?.Name));
                    row.RelativeItem().Element(c => Field(c, "Ročník",
                        student.SchoolClass?.Rocnik is { } rocnik ? $"{rocnik}." : null));
                });
                column.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Číslo v třídním výkazu", null));
                    row.RelativeItem().Element(c => Field(c, "Školní rok",
                        $"{Layout.SchoolYearStartYear}/{Layout.SchoolYearStartYear + 1}"));
                });
                column.Item().PaddingTop(10).AlignCenter().Text("VYSVĚDČENÍ").FontSize(34).Bold().LetterSpacing(0.02f);
                column.Item().AlignCenter().Text(isA ? "list A" : "list B").FontSize(13).Bold();
                column.Item().PaddingTop(2).AlignCenter().Text(isA
                        ? $"Nedílnou součástí tohoto vysvědčení {(listsB > 0 ? "je" : "není")} Vysvědčení-list B."
                        : $"List B číslo {numberB}, listů B celkem {listsB}.")
                    .FontSize(8);
                column.Item().PaddingTop(8).Element(c => Field(c, "Jméno a příjmení", student.Person!.FullName));
                column.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Datum narození",
                        student.DateOfBirth is { } born ? SchoolFormat.Date(born) : null));
                    row.RelativeItem().Element(c => Field(c, "Rodné číslo", student.RodneCislo));
                });
                if (isA)
                {
                    column.Item().PaddingTop(3).Element(c => Field(c, "Místo narození", null));
                }
                column.Item().PaddingTop(10).Element(c =>
                    TitleRow(c, $"{(Layout.Half == SchoolHalfYear.First ? "I." : "II.")} pololetí", bold: true));
                if (isA)
                {
                    column.Item().Border(1).BorderTop(0).Height(10);
                }
                column.Item().Element(c => TitleRow(c, Title, bold: true, top: false));
            });
        }

        private void BackOfListA(IContainer container)
        {
            var card = Layout.Card;
            var female = card.Student.Gender == Gender.Female;
            container.Column(column =>
            {
                column.Item().Border(1).Row(row =>
                {
                    row.RelativeItem().BorderRight(1).Padding(4).Column(cell =>
                    {
                        cell.Item().AlignCenter().Text("Zameškané hodiny").Bold();
                        cell.Item().AlignCenter().Text($"omluvené {card.ExcusedHours}   neomluvené {card.UnexcusedHours}");
                    });
                    row.RelativeItem().Padding(4).Column(cell =>
                    {
                        cell.Item().AlignCenter().Text("Celkové hodnocení").Bold();
                        cell.Item().AlignCenter().Text(card.Result(Layout.Half) is { } result
                            ? ReportCardDocument.Result(result, female)
                            : Dots);
                    });
                });
                column.Item().Border(1).BorderTop(0).Height(62).Padding(4).Text("Pochvaly a jiná ocenění");
                column.Item().Border(1).BorderTop(0).Padding(4)
                    .Text($"{(female ? "Žákyně splnila" : "Žák splnil")} {Dots} let/rok {Dots} povinné školní docházky.");
                column.Item().Border(1).BorderTop(0).Height(62).Padding(4)
                    .Text("Vzdělávání se uskutečňovalo podle školního vzdělávacího programu:");
                column.Item().PaddingTop(8).Element(Signatures);
                column.Item().PaddingTop(6).Border(1).Column(scale =>
                {
                    scale.Item().BorderBottom(1).PaddingVertical(2).AlignCenter().Text("STUPNĚ CELKOVÉHO HODNOCENÍ").FontSize(7).Bold();
                    scale.Item().Row(row =>
                    {
                        foreach (var (text, bold) in new[]
                                 {
                                     ("Celkové hodnocení", true), ("prospěl(a) s vyznamenáním", false),
                                     ("prospěl(a)", false), ("neprospěl(a)", false),
                                 })
                        {
                            row.RelativeItem().BorderRight(1).Padding(2).AlignCenter().Text(text).FontSize(7).Bold(bold);
                        }
                    });
                });
            });
        }

        private void BackOfListB(IContainer container) =>
            container.Column(column =>
            {
                column.Item().Border(1).Padding(6).Element(Signatures);
                column.Item().PaddingTop(3).Text(text =>
                {
                    text.Span("Upozornění:").Bold().Underline();
                    text.Span(" Vysvědčení-list B je platné pouze jako nedílná součást Vysvědčení-listu A.");
                });
            });

        /// <summary>Place, date, the stamp's circle and the director's and
        /// the třídní's signatures.</summary>
        private void Signatures(IContainer container) =>
            container.Row(row =>
            {
                row.RelativeItem(4).Column(place =>
                {
                    place.Item().Text($"V {Layout.School?.City ?? Dots}");
                    place.Item().PaddingTop(10).Text($"dne {ReportCardDocument.LongDate(Layout.IssuedOn)}");
                });
                row.ConstantItem(70).Height(60).AlignCenter().AlignMiddle().Width(58).Height(58)
                    .Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 58 58\"><circle cx=\"29\" cy=\"29\" r=\"28\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/></svg>");
                row.RelativeItem(4).PaddingLeft(8).Column(signatures =>
                {
                    foreach (var role in new[] { "ředitel(ka) školy", "třídní učitel(ka)" })
                    {
                        signatures.Item().PaddingTop(14).BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1).Height(1);
                        signatures.Item().AlignRight().Text(role).FontSize(7);
                    }
                });
            });

        private static void TitleRow(IContainer container, string text, bool bold, bool top = true) =>
            container.Border(1).BorderTop(top ? 1 : 0).PaddingVertical(2).AlignCenter().Text(text).Bold(bold);

        private static void Field(IContainer container, string label, string? value) =>
            container.Text(text =>
            {
                text.Span($"{label}: ");
                text.Span(value ?? Dots).Bold();
            });
    }
}

internal static class TextSpanExtensions
{
    public static TextSpanDescriptor Bold(this TextSpanDescriptor span, bool bold) => bold ? span.Bold() : span;
}
