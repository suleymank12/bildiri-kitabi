using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Tests.Shared;

namespace BildiriKitabi.IntegrationTests.Books;

/// <summary>
/// Ten sample papers → read → sanitize → PDF, verified by reading the PDF back with the test's own simple rules.
/// </summary>
public sealed partial class BookEndToEndTests(SampleBookFixture fixture) : IClassFixture<SampleBookFixture>
{
    private const string TurkishLetters = "İıĞğŞşÇçÖöÜü’";
    private const string LongestSampleTitle = "TOPLU TAŞIMA UYGULAMALARINDA ERİŞİLEBİLİR ROTA BİLGİSİNİN KULLANICI DENEYİMİNE KATKISI";

    private readonly BookPdf _pdf = new(fixture.Book.Pdf);
    private readonly List<SourcePaper> _sources = fixture.PaperFiles.Select(SourcePaper.Load).ToList();

    [Fact]
    public void Cover_and_table_of_contents_open_the_book()
    {
        _pdf.Pages.Count.ShouldBeGreaterThan(2 + 10);
        _pdf.Pages.Count.ShouldBe(fixture.Book.PageCount);

        var cover = _pdf.Page(1);
        cover.Text.ShouldContain(SampleBookFixture.BookName);
        cover.Text.ShouldContain("Bildiri Kitabı");
        cover.Text.ShouldContain("10 bildiri");
        cover.FooterNumber.ShouldBeNull();

        var contents = _pdf.Page(2);
        contents.BodyWords[0].ShouldBe("İçindekiler");
        contents.FooterNumber.ShouldBe(2);
    }

    [Fact]
    public void Every_table_of_contents_entry_points_to_the_page_where_the_paper_starts()
    {
        var entries = ReadTableOfContents();

        entries.Count.ShouldBe(10);
        foreach (var (source, pageNumber) in entries)
        {
            var page = _pdf.Page(pageNumber);
            page.BodyText.ShouldStartWith(Normalize(source.Title), customMessage: $"{source.FileName} → sayfa {pageNumber}");
            page.FooterNumber.ShouldBe(pageNumber);
        }

        entries.Select(e => e.Page).ShouldBe(fixture.Book.Papers.Select(p => p.StartPage));
    }

    [Fact]
    public void Every_page_after_the_cover_shows_its_physical_page_number()
    {
        foreach (var page in _pdf.Pages.Skip(1))
        {
            page.FooterNumber.ShouldBe(page.Number);
        }
    }

    [Fact]
    public void Running_heads_show_the_book_name_on_left_pages_and_the_paper_title_on_right_pages()
    {
        var entries = ReadTableOfContents();
        entries.Select(e => e.Source.Title).ShouldContain(t => Normalize(t) == LongestSampleTitle);

        for (var i = 0; i < entries.Count; i++)
        {
            var (source, start) = entries[i];
            var end = i + 1 < entries.Count ? entries[i + 1].Page - 1 : _pdf.Pages.Count;
            foreach (var page in Enumerable.Range(start, end - start + 1).Select(_pdf.Page))
            {
                var expected = page.Number % 2 == 0 ? SampleBookFixture.BookName : Normalize(source.Title);
                page.HeaderText.ShouldBe(expected, customMessage: $"sayfa {page.Number}");
                page.HeaderText.ShouldNotContain("…");
            }
        }
    }

    [Fact]
    public void Table_of_contents_page_numbers_are_centred_on_the_entry_title_lines()
    {
        var words = _pdf.Page(2).PositionedWords.Where(w => !w.Sans && w.Text != "İçindekiler").ToList();
        var right = words.Max(w => w.Right);

        // The page numbers are the right-aligned column; the titles end well before it. The sequence numbers ("1.")
        // sit on each entry's first line, so an entry's title lines lie from its marker down to the next marker.
        var numbers = words.Where(w => right - w.Right < 0.5 && IsInteger(w.Text)).ToList();
        numbers.Count.ShouldBe(10);
        var markers = words.Where(w => w.Text.EndsWith('.') && IsInteger(w.Text[..^1])).OrderByDescending(w => w.Baseline).ToList();
        markers.Count.ShouldBe(10);
        var titleWords = words.Except(numbers).Except(markers).ToList();

        for (var i = 0; i < markers.Count; i++)
        {
            var top = markers[i].Baseline + 0.5;
            var bottom = i + 1 < markers.Count ? markers[i + 1].Baseline + 0.5 : double.NegativeInfinity;
            var lines = titleWords.Where(w => w.Baseline <= top && w.Baseline > bottom).Select(w => w.Baseline).ToList();
            var number = numbers.Single(w => w.Baseline <= top && w.Baseline > bottom);

            // Same font and size, so the vertical centres differ exactly as the baselines do.
            var titleCentre = (lines.Max() + lines.Min()) / 2;
            Math.Abs(titleCentre - number.Baseline).ShouldBeLessThanOrEqualTo(1, $"{markers[i].Text} başlık merkezi {titleCentre:F2}, numara {number.Baseline:F2}");
        }
    }

    private static bool IsInteger(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    [Fact]
    public void Book_contains_no_email_addresses_or_turkish_phone_numbers()
    {
        var text = _pdf.AllText;

        EmailRegex().Matches(text).ShouldBeEmpty();
        TurkishPhoneRegex().Matches(text).ShouldBeEmpty();
        TurkishPhoneRegex().Matches(text.Replace(" ", string.Empty, StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Orcid_identifiers_are_kept()
    {
        var text = _pdf.AllText;

        text.ShouldContain("ORCID: 0000-0001-1000-0001");
        text.ShouldContain("ORCID: 0000-0001-1000-0002");
        text.ShouldContain("ORCID 0000-0001-1000-0003");
    }

    [Fact]
    public void Every_paper_is_reproduced_word_for_word_without_contact_values()
    {
        var entries = ReadTableOfContents();
        for (var i = 0; i < entries.Count; i++)
        {
            var (source, start) = entries[i];
            var end = i + 1 < entries.Count ? entries[i + 1].Page - 1 : _pdf.Pages.Count;
            var actual = Enumerable.Range(start, end - start + 1).SelectMany(n => _pdf.Page(n).BodyWords).ToList();
            var expected = source.ExpectedWords;

            actual.ShouldBe(expected, customMessage: source.FileName);

            var actualText = string.Concat(actual);
            var expectedText = string.Concat(expected);
            foreach (var letter in TurkishLetters)
            {
                actualText.Count(c => c == letter).ShouldBe(expectedText.Count(c => c == letter), customMessage: $"{source.FileName} '{letter}'");
            }
        }

        string.Concat(_sources.SelectMany(s => s.ExpectedWords)).ShouldContain("’");
    }

    [Fact]
    public void English_abstract_starts_on_its_own_page_after_the_turkish_summary()
    {
        var entries = ReadTableOfContents();
        for (var i = 0; i < entries.Count; i++)
        {
            var (source, start) = entries[i];
            var end = i + 1 < entries.Count ? entries[i + 1].Page - 1 : _pdf.Pages.Count;
            var pages = Enumerable.Range(start, end - start + 1).Select(_pdf.Page).ToList();

            var abstractPage = pages.Single(p => p.BodyWords.Contains("ABSTRACT"));
            abstractPage.BodyWords[0].ShouldBe("ABSTRACT", customMessage: source.FileName);
            abstractPage.Number.ShouldBeGreaterThan(start);

            var keywordsPage = pages.Single(p => p.BodyText.Contains("Anahtar Kelimeler:", StringComparison.Ordinal));
            keywordsPage.Number.ShouldBe(abstractPage.Number - 1, customMessage: source.FileName);
        }
    }

    [Fact]
    public void Source_core_metadata_of_the_sample_papers_does_not_reach_the_pdf()
    {
        var values = _sources.SelectMany(s => s.CoreMetadata).Distinct(StringComparer.Ordinal).ToList();
        values.ShouldNotBeEmpty();

        AssertNotInPdf(_pdf, fixture.Book.Pdf, values);
        _pdf.Information["Title"].ShouldBe(SampleBookFixture.BookName);
        _pdf.Information["Creator"].ShouldBe("Bildiri Kitabı Oluşturucu");
        _pdf.Information["Producer"].ShouldBe("Bildiri Kitabı Oluşturucu");
    }

    [Fact]
    public void Fake_phone_number_in_last_modified_by_does_not_reach_the_pdf()
    {
        const string fakeNumber = "905001112233";
        const string fakeTitle = "gizli kaynak başlığı";
        const string fakeCreator = "Uydurma Yazar";
        var body = TestDocx.Paragraph("DENEME BİLDİRİSİ", "<w:b/>", "<w:jc w:val=\"center\"/>") + TestDocx.Paragraph("Kısa bir özet metni.");
        using var source = TestDocx.Create(body, configurePackage: package =>
        {
            package.PackageProperties.LastModifiedBy = fakeNumber;
            package.PackageProperties.Title = fakeTitle;
            package.PackageProperties.Creator = fakeCreator;
        });
        var bytes = source.ToArray();

        var book = SampleBookFixture.CreateGenerator().Generate(
            "Metadata Denemesi",
            [new PaperSource("01_Deneme.docx", () => new MemoryStream(bytes))],
            cancellationToken: TestContext.Current.CancellationToken);
        var pdf = new BookPdf(book.Pdf);

        pdf.AllText.ShouldContain("DENEME BİLDİRİSİ");
        AssertNotInPdf(pdf, book.Pdf, [fakeNumber, fakeTitle, fakeCreator]);
    }

    private static void AssertNotInPdf(BookPdf pdf, byte[] raw, IEnumerable<string> values)
    {
        var text = pdf.AllText;
        var compactText = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        var rawLatin1 = Encoding.Latin1.GetString(raw);
        foreach (var value in values)
        {
            text.ShouldNotContain(value, Case.Sensitive);
            compactText.ShouldNotContain(value.Replace(" ", string.Empty, StringComparison.Ordinal), Case.Sensitive);
            foreach (var (key, field) in pdf.Information)
            {
                (field ?? string.Empty).ShouldNotContain(value, Case.Sensitive, customMessage: key);
            }

            (pdf.Xmp ?? string.Empty).ShouldNotContain(value, Case.Sensitive);
            rawLatin1.ShouldNotContain(value, Case.Sensitive);
            rawLatin1.ShouldNotContain(Encoding.Latin1.GetString(Encoding.BigEndianUnicode.GetBytes(value)), Case.Sensitive);
        }
    }

    /// <summary>
    /// Reads "n. title … page" entries from the table of contents text, not from the renderer, in the order the text
    /// is drawn: copying or searching an entry must give its whole title before its page number.
    /// </summary>
    private List<(SourcePaper Source, int Page)> ReadTableOfContents()
    {
        var words = _pdf.Page(2).ContentOrderBodyWords;
        var entries = new List<(SourcePaper, int)>();
        var position = 0;
        for (var i = 0; i < _sources.Count; i++)
        {
            var marker = $"{i + 1}.";
            position = IndexOf(words, [marker], position) + 1;
            position.ShouldBeGreaterThan(0, $"İçindekiler'de {marker} bulunamadı");

            var titleWords = Normalize(_sources[i].Title).Split(' ');
            IndexOf(words, titleWords, position).ShouldBe(position, $"İçindekiler'de {marker} başlığı eşleşmedi");
            position += titleWords.Length;

            entries.Add((_sources[i], int.Parse(words[position], NumberStyles.None, CultureInfo.InvariantCulture)));
            position++;
        }

        return entries;
    }

    private static int IndexOf(IReadOnlyList<string> words, string[] sequence, int from)
    {
        for (var i = from; i + sequence.Length <= words.Count; i++)
        {
            if (sequence.Select((w, k) => words[i + k] == w).All(match => match))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(string text) => WhitespaceRegex().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\S+@\S+\.\S+")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+?90[\s\-.]*)?(?:\(?0\)?[\s\-.]*)?\(?[2-58]\d{2}\)?[\s\-.]*\d{3}[\s\-.]*\d{2}[\s\-.]*\d{2}(?!\d)")]
    private static partial Regex TurkishPhoneRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
