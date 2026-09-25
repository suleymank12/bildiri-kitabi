using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.IntegrationTests.Books;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.IntegrationTests.Pdf;

public sealed class RendererAndLeakScannerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(3));

    private readonly QuestPdfBookRenderer _renderer = new();
    private readonly PdfPigLeakScanner _scanner = new();

    [Fact]
    public void Leak_scanner_counts_contact_values_left_in_a_pdf()
    {
        var content = Book(Paper("Sızıntı", Paragraph("Yazar: ad@example.org, Tel: 0312 555 12 34")));

        var result = _scanner.Scan(_renderer.Render(content).Pdf, content.Title);

        result.ShouldBe(new PdfLeakScanResult(EmailCount: 1, PhoneCount: 1));
        result.HasLeak.ShouldBeTrue();
    }

    [Fact]
    public void Leak_scanner_accepts_a_clean_pdf_with_numbers_that_are_not_contacts()
    {
        var content = Book(Paper("Temiz", Paragraph("ORCID: 0000-0002-1825-009X, DOI: 10.1016/j.jclepro.2020.123456, 2019-2023, 1.250.000 TL")));

        _scanner.Scan(_renderer.Render(content).Pdf, content.Title).HasLeak.ShouldBeFalse();
    }

    [Fact]
    public void Generation_fails_with_contact_leak_code_when_the_rendered_pdf_still_contains_contact_values()
    {
        var leakingRenderer = new InjectingRenderer(_renderer, Paragraph("İletişim: ad@example.org"));
        var generator = new BookGenerator(
            new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance),
            leakingRenderer,
            _scanner,
            FontGlyphCoverage.Instance,
            TimeProvider.System,
            NullLogger<BookGenerator>.Instance);
        var path = TestPaths.PaperFiles[0];

        var error = Should.Throw<BookGenerationException>(() =>
            generator.Generate("Sızıntı Denemesi", [new PaperSource(Path.GetFileName(path), () => File.OpenRead(path))]));

        error.Code.ShouldBe(BookErrorCodes.ContactLeakDetected);
    }

    [Fact]
    public void Tables_footnotes_links_and_empty_paragraphs_are_rendered()
    {
        var document = new SourceDocument(
            [
                Paragraph("TABLOLU BİLDİRİ"),
                new Paragraph(),
                new Table(
                    [100f, 200f],
                    [
                        new TableRow([new TableCell(1, [Paragraph("A1")]), new TableCell(1, [Paragraph("B1")])]),
                        new TableRow([new TableCell(2, [Paragraph("Birleşik hücre")])]),
                    ]),
                new Paragraph
                {
                    Alignment = ParagraphAlignment.Right,
                    FirstLineIndentPt = 18,
                    LineSpacing = new LineSpacing(LineSpacingRule.Exact, 14),
                    Runs =
                    [
                        new Run { Text = "Kaynak sitesi", Hyperlink = "https://example.org/kongre", Underline = true },
                        new Run { Text = "1", VerticalPosition = VerticalPosition.Superscript },
                        new Run { Text = "\tH", FontFamilyKind = FontFamilyKind.Sans },
                        new Run { Text = "2", VerticalPosition = VerticalPosition.Subscript },
                        new Run { Text = "O\nikinci satır", Italic = true },
                    ],
                },
            ],
            [new Footnote(1, [Paragraph("Dipnot açıklaması.")])]);

        var rendered = _renderer.Render(Book(new BookPaper("TABLOLU BİLDİRİ", document)));
        var pdf = new BookPdf(rendered.Pdf);

        rendered.PageCount.ShouldBe(3);
        rendered.PaperPages.ShouldBe([new PageRange(3, 3)]);
        var text = pdf.Page(3).Text;
        foreach (var expected in new[] { "A1", "B1", "Birleşik hücre", "Kaynak sitesi", "ikinci satır", "Dipnotlar", "Dipnot açıklaması." })
        {
            text.ShouldContain(expected);
        }
    }

    [Fact]
    public void Paper_page_ranges_follow_explicit_page_breaks()
    {
        var content = Book(
            Paper("BİRİNCİ", Paragraph("BİRİNCİ"), new PageBreak(), Paragraph("devam"), new PageBreak(), Paragraph("son")),
            Paper("İKİNCİ", Paragraph("İKİNCİ")));

        var rendered = _renderer.Render(content);

        rendered.PageCount.ShouldBe(6);
        rendered.PaperPages.ShouldBe([new PageRange(3, 5), new PageRange(6, 6)]);
    }

    private static BookContent Book(params BookPaper[] papers) => new("Deneme Kitabı", CreatedAt, papers);

    private static BookPaper Paper(string title, params Block[] blocks) => new(title, new SourceDocument(blocks));

    private static Paragraph Paragraph(string text) => new() { Runs = [new Run { Text = text }] };

    /// <summary>Simulates a sanitizer gap by adding an unsanitized paragraph right before rendering.</summary>
    private sealed class InjectingRenderer(IBookRenderer inner, Paragraph injected) : IBookRenderer
    {
        public RenderedBook Render(BookContent book)
        {
            var first = book.Papers[0];
            var papers = book.Papers.ToList();
            papers[0] = first with { Document = first.Document with { Blocks = [.. first.Document.Blocks, injected] } };
            return inner.Render(book with { Papers = papers });
        }
    }
}
