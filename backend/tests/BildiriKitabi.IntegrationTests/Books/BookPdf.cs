using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BildiriKitabi.IntegrationTests.Books;

/// <summary>
/// Reads a generated PDF back with PdfPig. Running heads and page numbers are set in the sans family while the
/// sample papers are set in serif, so words are split into body and running-head words by font.
/// </summary>
public sealed class BookPdf
{
    public BookPdf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        Pages = document.GetPages().Select(page => new BookPdfPage(page)).ToList();
        Information = new Dictionary<string, string?>
        {
            ["Title"] = document.Information.Title,
            ["Author"] = document.Information.Author,
            ["Subject"] = document.Information.Subject,
            ["Keywords"] = document.Information.Keywords,
            ["Creator"] = document.Information.Creator,
            ["Producer"] = document.Information.Producer,
        };
        Xmp = document.TryGetXmpMetadata(out var xmp) ? xmp.GetXDocument().ToString() : null;
    }

    public IReadOnlyList<BookPdfPage> Pages { get; }

    public IReadOnlyDictionary<string, string?> Information { get; }

    public string? Xmp { get; }

    public string AllText => string.Join('\n', Pages.Select(p => p.Text));

    /// <param name="number">1-based page number.</param>
    public BookPdfPage Page(int number) => Pages[number - 1];
}

public sealed class BookPdfPage
{
    public BookPdfPage(Page page)
    {
        Number = page.Number;
        var words = page.GetWords().ToList();
        Words = words.Select(w => w.Text).ToList();
        Text = string.Join(' ', Words);

        var body = words.Where(w => !IsSans(w)).ToList();
        BodyWords = body.Select(w => w.Text).ToList();
        BodyText = string.Join(' ', BodyWords);

        // The page number is a bottom line that consists of a single integer.
        var runningHead = words.Where(IsSans).ToList();
        var bottomLine = runningHead.Count == 0
            ? []
            : runningHead.Where(w => w.BoundingBox.Bottom <= runningHead.Min(x => x.BoundingBox.Bottom) + 1).ToList();
        FooterNumber = bottomLine.Count == 1 && int.TryParse(bottomLine[0].Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    public int Number { get; }

    public IReadOnlyList<string> Words { get; }

    public string Text { get; }

    public IReadOnlyList<string> BodyWords { get; }

    public string BodyText { get; }

    public int? FooterNumber { get; }

    private static bool IsSans(Word word) => word.FontName?.Contains("LiberationSans", StringComparison.Ordinal) == true;
}
