using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Titles;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.UnitTests.Titles;

public sealed class TitleDetectorTests
{
    public static TheoryData<string, string> SampleTitles => new()
    {
        { "01_Akilli_Sulama.docx", "KENTSEL TARIMDA AKILLI SULAMA SİSTEMLERİNİN SU TÜKETİMİNE ETKİSİ" },
        { "02_Erisilebilir_Ulasim.docx", "TOPLU TAŞIMA UYGULAMALARINDA ERİŞİLEBİLİR ROTA BİLGİSİNİN KULLANICI DENEYİMİNE KATKISI" },
        { "03_Dijital_Arsiv.docx", "DİJİTAL ARŞİVLERDE ANLAMSAL ARAMANIN BELGEYE ERİŞİM SÜRESİNE ETKİSİ" },
        { "04_Kampus_Enerji.docx", "ÜNİVERSİTE KAMPÜSLERİNDE ENERJİ İZLEME PANELLERİNİN DAVRANIŞSAL TASARRUFA ETKİSİ" },
        { "05_Uzaktan_Egitim.docx", "UZAKTAN EĞİTİMDE ETKİLEŞİMLİ İÇERİKLERİN ÖĞRENCİ KATILIMINA ETKİSİ" },
        { "06_Afet_Lojistigi.docx", "AFET SONRASI DAĞITIMDA DİNAMİK ROTA PLANLAMANIN TESLİM SÜRELERİNE ETKİSİ" },
        { "07_Kobi_Siber_Risk.docx", "KÜÇÜK VE ORTA ÖLÇEKLİ İŞLETMELERDE SİBER RİSK FARKINDALIĞININ ÖLÇÜLMESİ" },
        { "08_Uzaktan_Saglik.docx", "UZAKTAN SAĞLIK RANDEVULARINDA HATIRLATMA TASARIMININ KATILIM ORANINA ETKİSİ" },
        { "09_Atik_Toplama.docx", "KENTSEL ATIK TOPLAMA ROTALARINDA DOLULUK VERİSİ KULLANIMININ OPERASYONEL ETKİSİ" },
        { "10_Muze_Deneyimi.docx", "MÜZE ZİYARETLERİNDE KİŞİSELLEŞTİRİLMİŞ DİJİTAL REHBERLERİN DENEYİME ETKİSİ" },
    };

    [Theory]
    [MemberData(nameof(SampleTitles))]
    public void Sample_titles_come_from_the_title_style_verbatim(string fileName, string expectedTitle)
    {
        using var stream = File.OpenRead(TestPaths.PaperPath(fileName));
        var document = new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance).Read(stream);

        var title = TitleDetector.Detect(document, fileName);

        title.ShouldBe(new DetectedTitle(expectedTitle, TitleSource.TitleStyle));
    }

    [Fact]
    public void Heading_1_style_counts_as_a_title_style()
    {
        var document = Document(
            Paragraph("Giriş notu"),
            Paragraph("Bölüm Başlığı") with { Style = "heading 1" });

        TitleDetector.Detect(document, "x.docx").ShouldBe(new DetectedTitle("Bölüm Başlığı", TitleSource.TitleStyle));
    }

    [Fact]
    public void Blank_title_styled_paragraph_is_skipped()
    {
        var document = Document(
            new Paragraph { Style = "Title", Runs = [new Run { Text = "  " }] },
            Paragraph("Asıl başlık") with { Style = "Title" });

        TitleDetector.Detect(document, "x.docx").Text.ShouldBe("Asıl başlık");
    }

    [Fact]
    public void Falls_back_to_first_centered_all_bold_paragraph_within_the_first_five()
    {
        var document = Document(
            Paragraph("Kongre bildirisi", alignment: ParagraphAlignment.Left, bold: true),
            Paragraph("Merkezde ama kalın değil", alignment: ParagraphAlignment.Center),
            new Paragraph
            {
                Alignment = ParagraphAlignment.Center,
                Runs = [new Run { Text = "Kalın ", Bold = true }, new Run { Text = " " }, new Run { Text = "Başlık", Bold = true }],
            });

        TitleDetector.Detect(document, "x.docx")
            .ShouldBe(new DetectedTitle("Kalın  Başlık", TitleSource.FirstBoldParagraph));
    }

    [Fact]
    public void Bold_paragraph_after_the_first_five_is_not_used()
    {
        var paragraphs = Enumerable.Range(1, 5).Select(i => Paragraph($"Paragraf {i}")).ToList();
        paragraphs.Add(Paragraph("Geç gelen", alignment: ParagraphAlignment.Center, bold: true));

        TitleDetector.Detect(Document([.. paragraphs]), "03_Dijital_Arsiv.docx")
            .ShouldBe(new DetectedTitle("Dijital Arsiv", TitleSource.FileName));
    }

    [Theory]
    [InlineData("01_Akilli_Sulama.docx", "Akilli Sulama")]
    [InlineData("12-Yeni_Bildiri.docx", "Yeni Bildiri")]
    [InlineData("Bildiri.docx", "Bildiri")]
    [InlineData("2024.docx", "2024")]
    public void Falls_back_to_file_name(string fileName, string expected)
    {
        TitleDetector.Detect(Document(), fileName).ShouldBe(new DetectedTitle(expected, TitleSource.FileName));
    }

    [Fact]
    public void Line_breaks_inside_the_title_become_spaces_and_casing_is_kept()
    {
        var document = Document(Paragraph("Kentsel Tarımda\nAkıllı Sulama") with { Style = "Title" });

        TitleDetector.Detect(document, "x.docx").Text.ShouldBe("Kentsel Tarımda Akıllı Sulama");
    }

    private static SourceDocument Document(params Paragraph[] paragraphs) => new(paragraphs);

    private static Paragraph Paragraph(string text, ParagraphAlignment alignment = ParagraphAlignment.Left, bool bold = false) =>
        new() { Alignment = alignment, Runs = [new Run { Text = text, Bold = bold }] };
}
