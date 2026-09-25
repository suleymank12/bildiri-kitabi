using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Sanitization;
using BildiriKitabi.Infrastructure.Docx;
using BildiriKitabi.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace BildiriKitabi.UnitTests.Sanitization;

public sealed class ContactInfoSanitizerTests
{
    /// <summary>Expected result for every contact line of the sample papers, in document order; null means removed.</summary>
    public static TheoryData<string, string?[]> SampleContactLines => new()
    {
        { "01_Akilli_Sulama.docx", ["ORCID: 0000-0001-1000-0001"] },
        { "02_Erisilebilir_Ulasim.docx", ["ORCID: 0000-0001-1000-0002", null] },
        { "03_Dijital_Arsiv.docx", ["ORCID 0000-0001-1000-0003"] },
        { "04_Kampus_Enerji.docx", [null, null] },
        { "05_Uzaktan_Egitim.docx", [null] },
        { "06_Afet_Lojistigi.docx", [null] },
        { "07_Kobi_Siber_Risk.docx", [null, null] },
        { "08_Uzaktan_Saglik.docx", [null] },
        { "09_Atik_Toplama.docx", [null] },
        { "10_Muze_Deneyimi.docx", [null] },
    };

    [Theory]
    [MemberData(nameof(SampleContactLines))]
    public void Sample_contact_lines_are_cleaned_exactly(string fileName, string?[] expected)
    {
        var paragraphs = ReadSample(fileName).Blocks.OfType<Paragraph>().ToList();

        var results = paragraphs
            .Select(ContactInfoSanitizer.Sanitize)
            .Where(r => r.RemovedEmailCount + r.RemovedPhoneCount > 0)
            .ToList();

        results.Select(r => r.Paragraph?.Text).ShouldBe(expected);
        results.ShouldAllBe(r => r.RemovedEmailCount == 1 && r.RemovedPhoneCount == 1);
    }

    [Theory]
    [MemberData(nameof(SampleContactLines))]
    public void Sample_paragraphs_without_contact_information_are_unchanged(string fileName, string?[] expected)
    {
        _ = expected;
        var paragraphs = ReadSample(fileName).Blocks.OfType<Paragraph>().ToList();

        foreach (var paragraph in paragraphs.Where(p => ContactInfoDetector.Find(p.Text).Count == 0))
        {
            var result = ContactInfoSanitizer.Sanitize(paragraph).Paragraph.ShouldNotBeNull();
            result.Runs.ShouldBe(paragraph.Runs);
            result.Style.ShouldBe(paragraph.Style);
        }
    }

    [Fact]
    public void All_sample_papers_lose_thirteen_emails_and_thirteen_phones_and_keep_three_orcids()
    {
        var results = TestPaths.PaperFiles
            .Select(path => ContactInfoSanitizer.Sanitize(ReadSample(Path.GetFileName(path))))
            .ToList();

        results.Sum(r => r.RemovedEmailCount).ShouldBe(13);
        results.Sum(r => r.RemovedPhoneCount).ShouldBe(13);

        var text = string.Join('\n', results.SelectMany(r => r.Document.Paragraphs).Select(p => p.Text));
        text.ShouldContain("0000-0001-1000-0001");
        text.ShouldContain("0000-0001-1000-0002");
        text.ShouldContain("0000-0001-1000-0003");
        text.ShouldNotContain("@");
    }

    [Fact]
    public void Page_break_and_empty_source_paragraphs_are_kept()
    {
        var document = new SourceDocument(
        [
            new Paragraph(),
            Paragraph("Tel: 0500 000 00 01"),
            new PageBreak(),
            Paragraph("ABSTRACT"),
        ]);

        var result = ContactInfoSanitizer.Sanitize(document);

        result.Document.Blocks.Select(b => b.GetType().Name).ShouldBe(["Paragraph", "PageBreak", "Paragraph"]);
        result.Document.Blocks[0].ShouldBeOfType<Paragraph>().Runs.ShouldBeEmpty();
        result.RemovedPhoneCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("0312 555 12 34")]
    [InlineData("+90 312 555 12 34")]
    [InlineData("0850 000 00 00")]
    [InlineData("05000000001")]
    [InlineData("+905000000001")]
    [InlineData("0500.000.00.08")]
    [InlineData("0 (500) 000 00 12")]
    [InlineData("(0500) 000 00 05")]
    [InlineData("+90-500-000-00-06")]
    [InlineData("0090 500 000 00 01")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("+1 (202) 555-0147")]
    public void Phone_numbers_are_removed(string phone)
    {
        var result = Sanitize($"Bilgi için {phone} numarasını arayınız.");

        result.Paragraph.ShouldNotBeNull().Text.ShouldBe("Bilgi için numarasını arayınız.");
        result.RemovedPhoneCount.ShouldBe(1);
        result.RemovedEmailCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("ad.soyad+kongre@mail.example.org")]
    [InlineData("AD@EXAMPLE.COM")]
    [InlineData("şule@örnek.example.com")]
    [InlineData("ad [at] example.org")]
    [InlineData("ad(at)example.org")]
    [InlineData("ad [at] example [dot] org")]
    [InlineData("mailto:ad@example.org")]
    public void Email_addresses_are_removed(string email)
    {
        var result = Sanitize($"Sorularınız için {email} adresine yazınız.");

        result.Paragraph.ShouldNotBeNull().Text.ShouldBe("Sorularınız için adresine yazınız.");
        result.RemovedEmailCount.ShouldBe(1);
        result.RemovedPhoneCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("ORCID: 0000-0002-1825-009X")]
    [InlineData("ISBN 978-605-4220-65-3")]
    [InlineData("DOI: 10.1016/j.jclepro.2020.123456")]
    [InlineData("2019-2023 yılları arasında")]
    [InlineData("25.09.2026 tarihinde")]
    [InlineData("oran %24 arttı")]
    [InlineData("bütçe 1.250.000 TL oldu")]
    [InlineData("anlamlı fark (p<0.05)")]
    [InlineData("örneklem n=40")]
    [InlineData("Tablo 3.2 incelendiğinde")]
    [InlineData("IBAN: TR12 0006 1005 1978 6457 8413 26")]
    [InlineData("ORCID 0000-0001-1000-0003")]
    [InlineData("Kodlar 1234 5678 9012 3456 biçimindedir")]
    [InlineData("toplam 3.500.000.000 kayıt")]
    public void Non_contact_numbers_are_kept_verbatim(string text)
    {
        var result = Sanitize(text);

        result.Paragraph.ShouldNotBeNull().Text.ShouldBe(text);
        result.RemovedEmailCount.ShouldBe(0);
        result.RemovedPhoneCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("Tel: 0312 555 12 34")]
    [InlineData("Tel. No: 0500 000 00 01")]
    [InlineData("Cep telefonu: +90 500 000 00 10")]
    [InlineData("CEP TELEFONU: +90 500 000 00 10")]
    [InlineData("İrtibat: 0500.000.00.08")]
    [InlineData("İLETİŞİM: ad@example.org")]
    [InlineData("EMAIL: ad@example.org")]
    [InlineData("Phone: +90 312 555 12 34 / Fax: +90 312 555 12 35")]
    [InlineData("Faks: 0312 555 12 35")]
    [InlineData("Contact: ad@example.com")]
    [InlineData("  E-posta: ad@example.org ;  GSM: 0500 000 00 01  ")]
    public void Paragraph_that_only_holds_labelled_contact_values_is_removed(string text)
    {
        Sanitize(text).IsRemoved.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Ali VELİ | ali@example.org | Ankara", "Ali VELİ | Ankara")]
    [InlineData("Ali VELİ (ali@example.org)", "Ali VELİ")]
    [InlineData("Ali VELİ, ali@example.org", "Ali VELİ")]
    [InlineData("ali@example.org - Ankara Örnek Üniversitesi", "Ankara Örnek Üniversitesi")]
    [InlineData("Yazışma yazarı: Ali VELİ, E-posta: ali@example.org, Tel: 0312 555 12 34", "Yazışma yazarı: Ali VELİ")]
    [InlineData("Veri seti DOI: 10.1016/j.jclepro.2020.123456 / iletişim ali@example.org", "Veri seti DOI: 10.1016/j.jclepro.2020.123456")]
    public void Only_contact_values_and_their_labels_and_separators_are_removed(string text, string expected)
    {
        Sanitize(text).Paragraph.ShouldNotBeNull().Text.ShouldBe(expected);
    }

    [Fact]
    public void Email_split_across_runs_is_removed_and_neighbouring_formatting_is_kept()
    {
        var paragraph = new Paragraph
        {
            Runs =
            [
                new Run { Text = "Yazar ", Bold = true },
                new Run { Text = "ali.veli@exa", Italic = true },
                new Run { Text = "mple.org", Underline = true },
                new Run { Text = " ile görüşüldü.", Bold = true },
            ],
        };

        var result = ContactInfoSanitizer.Sanitize(paragraph);

        var runs = result.Paragraph.ShouldNotBeNull().Runs;
        result.RemovedEmailCount.ShouldBe(1);
        runs.Select(r => r.Text).ShouldBe(["Yazar ", "ile görüşüldü."]);
        runs.ShouldAllBe(r => r.Bold && !r.Italic && !r.Underline);
    }

    [Fact]
    public void Contact_hyperlink_targets_are_dropped_but_other_links_are_kept()
    {
        var paragraph = new Paragraph
        {
            Runs =
            [
                new Run { Text = "yazara ulaşın", Hyperlink = "mailto:ali@example.org" },
                new Run { Text = " veya " },
                new Run { Text = "arayın", Hyperlink = "tel:+905000000001" },
                new Run { Text = " ya da " },
                new Run { Text = "siteyi ziyaret edin", Hyperlink = "https://example.org/kongre" },
            ],
        };

        var runs = ContactInfoSanitizer.Sanitize(paragraph).Paragraph.ShouldNotBeNull().Runs;

        runs.Select(r => r.Hyperlink).ShouldBe([null, null, null, null, "https://example.org/kongre"]);
        runs.Select(r => r.Text).ShouldBe(["yazara ulaşın", " veya ", "arayın", " ya da ", "siteyi ziyaret edin"]);
    }

    [Fact]
    public void Tables_and_footnotes_are_sanitized()
    {
        var document = new SourceDocument(
            [new Table([100f], [new TableRow([new TableCell(1, [Paragraph("Tel: 0500 000 00 01"), Paragraph("Hücre")])])])],
            [new Footnote(1, [Paragraph("Yazışma: ali@example.org")]), new Footnote(2, [Paragraph("E-posta: veli@example.org")])]);

        var result = ContactInfoSanitizer.Sanitize(document);

        var cell = result.Document.Blocks.Single().ShouldBeOfType<Table>().Rows.Single().Cells.Single();
        cell.Blocks.OfType<Paragraph>().Select(p => p.Text).ShouldBe(["Hücre"]);
        result.Document.Footnotes.Single().Paragraphs.Single().Text.ShouldBe("Yazışma:");
        result.RemovedEmailCount.ShouldBe(2);
        result.RemovedPhoneCount.ShouldBe(1);
    }

    [Fact]
    public void Typographic_apostrophe_and_turkish_letters_are_preserved()
    {
        const string text = "Türkiye’nin ığdır şehrinde ölçüm yapıldı (İ, ı, ğ, ş, ç, ö, ü).";

        Sanitize(text).Paragraph.ShouldNotBeNull().Text.ShouldBe(text);
    }

    private static ParagraphSanitizationResult Sanitize(string text) => ContactInfoSanitizer.Sanitize(Paragraph(text));

    private static Paragraph Paragraph(string text) => new() { Runs = [new Run { Text = text }] };

    private static SourceDocument ReadSample(string fileName)
    {
        using var stream = File.OpenRead(TestPaths.PaperPath(fileName));
        return new OpenXmlDocxReader(NullLogger<OpenXmlDocxReader>.Instance).Read(stream);
    }
}
