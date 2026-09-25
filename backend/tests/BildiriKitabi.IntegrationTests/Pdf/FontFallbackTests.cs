using System.Text;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Infrastructure.Pdf;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.IntegrationTests.Books;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.IntegrationTests.Pdf;

/// <summary>DejaVu fallback fonts and the unsupported-character check that must agree with QuestPDF.</summary>
public sealed class FontFallbackTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(3));

    // Liberation has the first group; only DejaVu has the second; DejaVu Sans alone has ✓ and 😀; nothing has 𝒜 or 中.
    // ℝ is in DejaVu Serif Regular but not in DejaVu Serif Bold, so style matters.
    public static TheoryData<string, FontFamilyKind, bool> Samples()
    {
        var data = new TheoryData<string, FontFamilyKind, bool>();
        foreach (var character in new[] { "α", "≤", "→", "∀", "⇒", "ℝ", "✓", "😀", "𝒜", "中" })
        {
            data.Add(character, FontFamilyKind.Serif, false);
            data.Add(character, FontFamilyKind.Serif, true);
            data.Add(character, FontFamilyKind.Sans, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Glyph_coverage_agrees_with_what_questpdf_can_render(string character, FontFamilyKind family, bool bold)
    {
        var expected = FontGlyphCoverage.Instance.IsSupported(Rune.GetRuneAt(character, 0), family, bold, italic: false);
        var paper = new SourceDocument(
        [
            new Paragraph { Runs = [new Run { Text = "DENEME" }] },
            new Paragraph { Runs = [new Run { Text = $"x {character} y", FontFamilyKind = family, Bold = bold }] },
        ]);

        var render = () => new QuestPdfBookRenderer().Render(new BookContent("Deneme", CreatedAt, [new BookPaper("DENEME", paper)]));

        if (expected)
        {
            render.ShouldNotThrow();
        }
        else
        {
            Should.Throw<BookGenerationException>(render).Code.ShouldBe(BookErrorCodes.RenderFailed);
        }
    }

    [Fact]
    public void Coverage_reads_the_font_tables()
    {
        var coverage = FontGlyphCoverage.Instance;

        coverage.IsSupported(new Rune('ş'), FontFamilyKind.Serif, bold: false, italic: false).ShouldBeTrue();
        coverage.IsSupported(new Rune('∀'), FontFamilyKind.Serif, bold: true, italic: true).ShouldBeTrue();
        coverage.IsSupported(new Rune(0x211D), FontFamilyKind.Serif, bold: false, italic: false).ShouldBeTrue();
        coverage.IsSupported(new Rune(0x211D), FontFamilyKind.Serif, bold: true, italic: false).ShouldBeFalse();
        coverage.IsSupported(new Rune(0x1F600), FontFamilyKind.Sans, bold: false, italic: false).ShouldBeTrue();
        coverage.IsSupported(new Rune(0x1F600), FontFamilyKind.Serif, bold: true, italic: false).ShouldBeFalse();
        coverage.IsSupported(new Rune(0x1D49C), FontFamilyKind.Sans, bold: false, italic: false).ShouldBeFalse();
    }

    [Fact]
    public void Academic_symbols_are_printed_through_the_fallback_fonts()
    {
        const string symbols = "α β χ² ± ≤ ≥ ∑ √ → × ∀ ∈ ⇒";
        using var docx = TestDocx.Create(
            TestDocx.Paragraph("SEMBOLLÜ BİLDİRİ", "<w:b/>", "<w:jc w:val=\"center\"/>") +
            TestDocx.Paragraph($"Bulgular: {symbols} (p ≤ 0,05).") +
            TestDocx.Paragraph($"Kalın: {symbols}", "<w:b/>") +
            TestDocx.Paragraph($"Sans: {symbols}", "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/>"));
        var bytes = docx.ToArray();

        var book = SampleBookFixture.CreateGenerator().Generate(
            "Sembol Denemesi ∀x ∈ A",
            [new PaperSource("01_Sembol.docx", () => new MemoryStream(bytes))],
            cancellationToken: TestContext.Current.CancellationToken);

        var text = new BookPdf(book.Pdf).AllText;
        foreach (var symbol in symbols.Split(' '))
        {
            text.ShouldContain(symbol);
        }

        text.ShouldContain("Bulgular:");
        text.ShouldContain("(p ≤ 0,05).");
    }

    [Fact]
    public void A_paper_with_a_character_missing_from_every_font_fails_with_its_position_and_the_character()
    {
        var papers = TestPaths.PaperFiles.Take(2).Select(path => new PaperSource(Path.GetFileName(path), () => File.OpenRead(path))).ToList();
        using var docx = TestDocx.Create(TestDocx.Paragraph("SEMBOLLÜ BİLDİRİ", "<w:b/>") + TestDocx.Paragraph("Küme 𝒜 ve 中 tanımı."));
        var bytes = docx.ToArray();
        papers.Add(new PaperSource("03_Sembol.docx", () => new MemoryStream(bytes)));

        var error = Should.Throw<BookGenerationException>(() => SampleBookFixture.CreateGenerator().Generate(
            "Sembol Denemesi",
            papers,
            cancellationToken: TestContext.Current.CancellationToken));

        error.Code.ShouldBe(BookErrorCodes.UnsupportedCharacter);
        error.Message.ShouldBe("3. sıradaki bildiride PDF yazı tipinde bulunmayan '𝒜', '中' karakterleri var.");
    }

    [Fact]
    public void The_sample_book_does_not_embed_the_fallback_fonts()
    {
        var reader = new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance);
        using var source = File.OpenRead(TestPaths.PaperFiles[0]);
        var paper = reader.Read(source);

        var pdf = new QuestPdfBookRenderer().Render(new BookContent("Örnek Bilim Kongresi 2026", CreatedAt, [new BookPaper("KENTSEL", paper)])).Pdf;

        Encoding.Latin1.GetString(pdf).ShouldNotContain("DejaVu");
    }
}
