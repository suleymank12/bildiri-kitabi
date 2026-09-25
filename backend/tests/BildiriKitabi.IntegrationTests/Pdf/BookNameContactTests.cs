using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.IntegrationTests.Books;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.IntegrationTests.Pdf;

/// <summary>Contact details typed into the book name are printed as typed and are not reported as leaks.</summary>
public sealed class BookNameContactTests
{
    private const string NameWithContacts = "Kongre 0312 555 12 34 iletisim@example.org";

    [Fact]
    public void Contact_details_in_the_book_name_appear_on_the_cover_and_in_the_metadata()
    {
        var book = SampleBookFixture.CreateGenerator().Generate(NameWithContacts, [SamplePaper()], cancellationToken: TestContext.Current.CancellationToken);
        var pdf = new BookPdf(book.Pdf);

        pdf.Page(1).Text.ShouldContain(NameWithContacts);
        pdf.Information["Title"].ShouldBe(NameWithContacts);
        pdf.Page(4).HeaderText.ShouldBe(NameWithContacts);
    }

    [Fact]
    public void A_different_phone_number_from_the_paper_is_still_a_leak()
    {
        using var docx = TestDocx.Create(
            TestDocx.Paragraph("SIZINTI DENEMESİ", "<w:b/>", "<w:jc w:val=\"center\"/>") +
            TestDocx.Paragraph("Yazar Adı | Tel: 0500 000 99 99"));
        var bytes = docx.ToArray();
        var reader = new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance);
        var generator = new BookGenerator(
            reader,
            new UnsanitizedRenderer(new QuestPdfBookRenderer(), [reader.Read(new MemoryStream(bytes))]),
            new PdfPigLeakScanner(),
            FontGlyphCoverage.Instance,
            TimeProvider.System,
            NullLogger<BookGenerator>.Instance);

        var error = Should.Throw<BookGenerationException>(() => generator.Generate(
            NameWithContacts,
            [new PaperSource("01_Sizinti.docx", () => new MemoryStream(bytes))],
            cancellationToken: TestContext.Current.CancellationToken));

        error.Code.ShouldBe(BookErrorCodes.ContactLeakDetected);
    }

    [Fact]
    public void A_phone_number_in_the_running_head_is_printed_whole_and_is_not_a_leak()
    {
        const string name = "Ulusal Bilim Kongresi Kitabı +90 312 555 12 34";

        var book = SampleBookFixture.CreateGenerator().Generate(name, [SamplePaper()], cancellationToken: TestContext.Current.CancellationToken);

        // Page 4 is the first left-hand page of the paper: its running head is the book name, never shortened.
        new BookPdf(book.Pdf).Page(4).HeaderText.ShouldBe(name);
    }

    private static PaperSource SamplePaper()
    {
        var path = TestPaths.PaperFiles[0];
        return new PaperSource(Path.GetFileName(path), () => File.OpenRead(path));
    }

    /// <summary>Renders the papers exactly as read from the .docx, as if the sanitizer had been switched off.</summary>
    private sealed class UnsanitizedRenderer(IBookRenderer inner, IReadOnlyList<SourceDocument> rawDocuments) : IBookRenderer
    {
        public RenderedBook Render(BookContent book) =>
            inner.Render(book with { Papers = book.Papers.Select((paper, i) => paper with { Document = rawDocuments[i] }).ToList() });
    }
}
