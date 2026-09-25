using BildiriKitabi.Core.Documents;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.UnitTests.Docx;

public sealed class OpenXmlDocxReaderTests
{
    private readonly OpenXmlDocxReader _reader = new(NullLogger<OpenXmlDocxReader>.Instance);

    public static TheoryData<string, int> SamplePapers => new()
    {
        { "01_Akilli_Sulama.docx", 12 },
        { "02_Erisilebilir_Ulasim.docx", 15 },
        { "03_Dijital_Arsiv.docx", 12 },
        { "04_Kampus_Enerji.docx", 15 },
        { "05_Uzaktan_Egitim.docx", 12 },
        { "06_Afet_Lojistigi.docx", 12 },
        { "07_Kobi_Siber_Risk.docx", 15 },
        { "08_Uzaktan_Saglik.docx", 12 },
        { "09_Atik_Toplama.docx", 12 },
        { "10_Muze_Deneyimi.docx", 12 },
    };

    [Theory]
    [MemberData(nameof(SamplePapers))]
    public void Sample_paper_has_expected_block_structure(string fileName, int expectedBlocks)
    {
        var document = ReadSample(fileName);

        document.Blocks.Count.ShouldBe(expectedBlocks);
        document.Blocks.OfType<PageBreak>().Count().ShouldBe(1);
        document.Footnotes.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SamplePapers))]
    public void Page_break_separates_turkish_keywords_from_english_abstract(string fileName, int expectedBlocks)
    {
        var blocks = ReadSample(fileName).Blocks;

        var breakIndex = blocks.ToList().FindIndex(b => b is PageBreak);
        breakIndex.ShouldBe(expectedBlocks - 4);
        blocks[breakIndex - 1].ShouldBeOfType<Paragraph>().Text.ShouldStartWith("Anahtar Kelimeler:");
        blocks[breakIndex + 1].ShouldBeOfType<Paragraph>().Text.ShouldBe("ABSTRACT");
    }

    [Theory]
    [MemberData(nameof(SamplePapers))]
    public void Localized_title_style_id_resolves_to_title_style_name(string fileName, int expectedBlocks)
    {
        _ = expectedBlocks;
        var title = ReadSample(fileName).Blocks[0].ShouldBeOfType<Paragraph>();

        title.Style.ShouldBe("Title");
        title.Alignment.ShouldBe(ParagraphAlignment.Center);
        title.KeepWithNext.ShouldBeTrue();
        title.Runs.ShouldAllBe(r => r.Bold);
        title.Runs.ShouldAllBe(r => r.FontSizePt == 11f && r.FontFamilyKind == FontFamilyKind.Serif);
    }

    [Theory]
    [MemberData(nameof(SamplePapers))]
    public void Runs_with_explicit_bold_off_are_not_bold(string fileName, int expectedBlocks)
    {
        _ = expectedBlocks;
        var paragraphs = ReadSample(fileName).Blocks.OfType<Paragraph>().ToList();

        var englishTitle = paragraphs[1];
        englishTitle.Style.ShouldBe("Normal");
        englishTitle.Runs.ShouldNotBeEmpty();
        englishTitle.Runs.ShouldAllBe(r => !r.Bold);

        var author = paragraphs[2];
        author.Runs.ShouldAllBe(r => r.Bold);

        var turkishAbstract = paragraphs.Single(p => p.Text.StartsWith("Bu çalışma", StringComparison.Ordinal));
        turkishAbstract.Runs.ShouldAllBe(r => !r.Bold && !r.Italic && !r.Underline);

        var keywords = paragraphs.Single(p => p.Text.StartsWith("Anahtar Kelimeler:", StringComparison.Ordinal));
        keywords.Runs.Count.ShouldBe(2);
        keywords.Runs[0].Bold.ShouldBeTrue();
        keywords.Runs[1].Bold.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(SamplePapers))]
    public void Paragraph_alignment_and_spacing_are_read(string fileName, int expectedBlocks)
    {
        _ = expectedBlocks;
        var paragraphs = ReadSample(fileName).Blocks.OfType<Paragraph>().ToList();

        paragraphs[1].Alignment.ShouldBe(ParagraphAlignment.Center);
        paragraphs[1].SpacingAfterPt.ShouldBe(8f);

        var summaryHeading = paragraphs.Single(p => p.Text == "ÖZET");
        summaryHeading.Alignment.ShouldBe(ParagraphAlignment.Left);
        summaryHeading.SpacingBeforePt.ShouldBe(6f);
        summaryHeading.SpacingAfterPt.ShouldBe(4f);

        var summary = paragraphs[paragraphs.IndexOf(summaryHeading) + 1];
        summary.Alignment.ShouldBe(ParagraphAlignment.Justify);
        summary.LineSpacing.ShouldBe(new LineSpacing(LineSpacingRule.Multiple, 1f));

        paragraphs.Single(p => p.Text == "ABSTRACT").Alignment.ShouldBe(ParagraphAlignment.Left);
    }

    [Fact]
    public void Contact_lines_are_read_verbatim()
    {
        var paragraphs = ReadSample("02_Erisilebilir_Ulasim.docx").Blocks.OfType<Paragraph>().ToList();

        paragraphs[4].Text.ShouldBe("Email: mert.demir@example.org | Telefon: +90 (500) 000 00 02 | ORCID: 0000-0001-1000-0002");
        paragraphs[7].Text.ShouldBe("E-posta derya.akin@example.org / GSM 0 (500) 000 00 12");
    }

    [Theory]
    [InlineData("<w:b w:val=\"0\"/>", false)]
    [InlineData("<w:b w:val=\"false\"/>", false)]
    [InlineData("<w:b w:val=\"off\"/>", false)]
    [InlineData("<w:b/>", true)]
    [InlineData("<w:b w:val=\"1\"/>", true)]
    [InlineData("<w:b w:val=\"true\"/>", true)]
    public void Toggle_property_honours_its_value(string boldElement, bool expectedBold)
    {
        var document = Read(TestDocx.Paragraph("x", boldElement));

        Runs(document).Single().Bold.ShouldBe(expectedBold);
    }

    [Fact]
    public void Formatting_is_layered_from_defaults_through_based_on_chain_character_style_and_direct_formatting()
    {
        const string styles = """
            <w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val="20"/></w:rPr></w:rPrDefault>
              <w:pPrDefault><w:pPr><w:spacing w:after="200" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:rFonts w:ascii="Arial"/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="Ozel"><w:name w:val="Custom Base"/><w:basedOn w:val="Normal"/>
              <w:pPr><w:jc w:val="center"/></w:pPr><w:rPr><w:b/><w:sz w:val="28"/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="OzelAlt"><w:name w:val="Custom Child"/><w:basedOn w:val="Ozel"/>
              <w:rPr><w:i/></w:rPr></w:style>
            <w:style w:type="character" w:styleId="Vurgu"><w:name w:val="Emphasis"/><w:rPr><w:u w:val="single"/><w:b w:val="0"/></w:rPr></w:style>
            """;
        const string body = """
            <w:p><w:pPr><w:pStyle w:val="OzelAlt"/></w:pPr>
              <w:r><w:t>inherited</w:t></w:r>
              <w:r><w:rPr><w:rStyle w:val="Vurgu"/></w:rPr><w:t>character</w:t></w:r>
              <w:r><w:rPr><w:rStyle w:val="Vurgu"/><w:b/><w:sz w:val="16"/></w:rPr><w:t>direct</w:t></w:r>
            </w:p>
            """;

        var document = Read(body, styles);
        var paragraph = document.Blocks.OfType<Paragraph>().Single();

        paragraph.Style.ShouldBe("Custom Child");
        paragraph.Alignment.ShouldBe(ParagraphAlignment.Center);
        paragraph.SpacingAfterPt.ShouldBe(10f);
        paragraph.LineSpacing.ShouldBe(new LineSpacing(LineSpacingRule.Multiple, 1.15f));

        paragraph.Runs[0].ShouldSatisfyAllConditions(
            r => r.Text.ShouldBe("inherited"),
            r => r.Bold.ShouldBeTrue(),
            r => r.Italic.ShouldBeTrue(),
            r => r.Underline.ShouldBeFalse(),
            r => r.FontSizePt.ShouldBe(14f),
            r => r.FontFamilyKind.ShouldBe(FontFamilyKind.Sans));
        paragraph.Runs[1].ShouldSatisfyAllConditions(
            r => r.Text.ShouldBe("character"),
            r => r.Bold.ShouldBeFalse(),
            r => r.Underline.ShouldBeTrue());
        paragraph.Runs[2].ShouldSatisfyAllConditions(
            r => r.Text.ShouldBe("direct"),
            r => r.Bold.ShouldBeTrue(),
            r => r.FontSizePt.ShouldBe(8f));
    }

    [Fact]
    public void Paragraph_without_style_uses_default_paragraph_style()
    {
        const string styles = """
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:sz w:val="24"/></w:rPr></w:style>
            """;

        var document = Read(TestDocx.Paragraph("x"), styles);

        var paragraph = document.Blocks.OfType<Paragraph>().Single();
        paragraph.Style.ShouldBe("Normal");
        paragraph.Runs.Single().FontSizePt.ShouldBe(12f);
    }

    [Fact]
    public void Page_break_inside_a_paragraph_splits_it()
    {
        const string body = """
            <w:p><w:r><w:t>before</w:t><w:br w:type="page"/><w:t>after</w:t></w:r></w:p>
            """;

        var blocks = Read(body).Blocks;

        blocks.Count.ShouldBe(3);
        blocks[0].ShouldBeOfType<Paragraph>().Text.ShouldBe("before");
        blocks[1].ShouldBeOfType<PageBreak>();
        blocks[2].ShouldBeOfType<Paragraph>().Text.ShouldBe("after");
    }

    [Fact]
    public void Page_break_before_property_inserts_a_page_break()
    {
        var body = TestDocx.Paragraph("one") + TestDocx.Paragraph("two", paragraphProperties: "<w:pageBreakBefore/>");

        var blocks = Read(body).Blocks;

        blocks.Select(b => b.GetType().Name).ShouldBe(["Paragraph", "PageBreak", "Paragraph"]);
    }

    [Fact]
    public void Empty_source_paragraph_is_kept()
    {
        var blocks = Read("<w:p/>" + TestDocx.Paragraph("x")).Blocks;

        blocks.Count.ShouldBe(2);
        blocks[0].ShouldBeOfType<Paragraph>().Runs.ShouldBeEmpty();
    }

    [Fact]
    public void Tabs_and_line_breaks_are_kept_inside_runs()
    {
        const string body = "<w:p><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t></w:r></w:p>";

        Runs(Read(body)).Single().Text.ShouldBe("a\tb\nc");
    }

    [Fact]
    public void Hidden_and_deleted_text_is_skipped_while_inserted_text_is_read()
    {
        const string body = """
            <w:p>
              <w:r><w:t xml:space="preserve">visible </w:t></w:r>
              <w:r><w:rPr><w:vanish/></w:rPr><w:t>hidden</w:t></w:r>
              <w:del w:id="1" w:author="a"><w:r><w:delText>deleted</w:delText></w:r></w:del>
              <w:ins w:id="2" w:author="a"><w:r><w:t>inserted</w:t></w:r></w:ins>
            </w:p>
            """;

        Read(body).Blocks.OfType<Paragraph>().Single().Text.ShouldBe("visible inserted");
    }

    [Fact]
    public void Structured_document_tags_and_hyperlinks_are_read()
    {
        const string body = """
            <w:sdt><w:sdtContent>
              <w:p><w:hyperlink r:id="rIdLink"><w:r><w:t>link text</w:t></w:r></w:hyperlink></w:p>
            </w:sdtContent></w:sdt>
            """;

        var document = Read(body, configure: part =>
            part.AddHyperlinkRelationship(new Uri("https://example.org/paper"), true, "rIdLink"));

        var run = Runs(document).Single();
        run.Text.ShouldBe("link text");
        run.Hyperlink.ShouldBe("https://example.org/paper");
    }

    [Fact]
    public void Text_box_content_is_read_once()
    {
        const string body = """
            <w:p><w:r><w:t>anchor</w:t></w:r><w:r>
              <mc:AlternateContent>
                <mc:Choice Requires="wps"><w:drawing><wp:anchor><a:graphic><a:graphicData>
                  <wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>boxed</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp>
                </a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>
                <mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>boxed</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback>
              </mc:AlternateContent></w:r></w:p>
            """;

        var paragraphs = Read(body).Blocks.OfType<Paragraph>().Select(p => p.Text).ToList();

        paragraphs.ShouldBe(["anchor", "boxed"]);
    }

    [Fact]
    public void Tables_are_read_as_grids()
    {
        const string body = """
            <w:tbl><w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="4000"/></w:tblGrid>
              <w:tr><w:tc><w:p><w:r><w:t>A1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc></w:tr>
              <w:tr><w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>merged</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            """;

        var table = Read(body).Blocks.Single().ShouldBeOfType<Table>();

        table.ColumnWidthsPt.ShouldBe([100f, 200f]);
        table.Rows.Count.ShouldBe(2);
        table.Rows[0].Cells.Select(c => ((Paragraph)c.Blocks.Single()).Text).ShouldBe(["A1", "B1"]);
        table.Rows[1].Cells.Single().ColumnSpan.ShouldBe(2);
    }

    [Fact]
    public void Footnotes_are_numbered_in_reference_order()
    {
        const string body = """
            <w:p><w:r><w:t>text</w:t></w:r><w:r><w:rPr><w:vertAlign w:val="superscript"/></w:rPr><w:footnoteReference w:id="7"/></w:r></w:p>
            """;
        const string footnotes = """
            <w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
            <w:footnote w:id="7"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space="preserve"> Source note.</w:t></w:r></w:p></w:footnote>
            """;

        var document = Read(body, footnotesXml: footnotes);

        var paragraph = document.Blocks.OfType<Paragraph>().Single();
        paragraph.Text.ShouldBe("text1");
        paragraph.Runs[^1].VerticalPosition.ShouldBe(VerticalPosition.Superscript);
        var footnote = document.Footnotes.Single();
        footnote.Number.ShouldBe(1);
        footnote.Paragraphs.Single().Text.ShouldBe(" Source note.");
    }

    [Fact]
    public void Unknown_elements_do_not_stop_reading()
    {
        const string body = """
            <w:p><w:r><w:t>kept</w:t></w:r><w:r><w:sym w:font="Wingdings" w:char="F0E0"/></w:r></w:p>
            """;

        Read(body).Blocks.OfType<Paragraph>().Single().Text.ShouldBe("kept");
    }

    [Fact]
    public void Non_word_content_is_rejected()
    {
        using var stream = new MemoryStream("not a zip file"u8.ToArray());

        Should.Throw<InvalidDocumentException>(() => _reader.Read(stream));
    }

    private SourceDocument ReadSample(string fileName)
    {
        using var stream = File.OpenRead(TestPaths.PaperPath(fileName));
        return _reader.Read(stream);
    }

    private SourceDocument Read(
        string bodyXml,
        string? stylesXml = null,
        string? footnotesXml = null,
        Action<DocumentFormat.OpenXml.Packaging.MainDocumentPart>? configure = null)
    {
        using var stream = TestDocx.Create(bodyXml, stylesXml, footnotesXml, configure);
        return _reader.Read(stream);
    }

    private static List<Run> Runs(SourceDocument document) =>
        document.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).ToList();
}
