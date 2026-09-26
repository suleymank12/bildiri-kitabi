using System.Globalization;
using System.Text;
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
        ContentOrderBodyWords = ContentOrderWords(page.Letters.Where(l => !IsSans(l.FontName)));

        // The page number is a bottom line that consists of a single integer.
        var runningHead = words.Where(IsSans).ToList();
        var bottomLine = runningHead.Count == 0
            ? []
            : runningHead.Where(w => w.BoundingBox.Bottom <= runningHead.Min(x => x.BoundingBox.Bottom) + 1).ToList();
        FooterNumber = bottomLine.Count == 1 && int.TryParse(bottomLine[0].Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

        HeaderText = string.Join(' ', runningHead.Except(bottomLine).Select(w => w.Text));
        PositionedWords = words
            .Select(w => new PositionedWord(w.Text, w.Letters[0].StartBaseLine.Y, w.BoundingBox.Left, w.BoundingBox.Right, w.Letters[0].PointSize, IsSans(w)))
            .ToList();
    }

    public int Number { get; }

    public IReadOnlyList<string> Words { get; }

    public string Text { get; }

    public IReadOnlyList<string> BodyWords { get; }

    public string BodyText { get; }

    /// <summary>
    /// Body words in the order they are drawn, which is the order copy and search follow in content-order readers
    /// (pdf.js, pdftotext -raw). <see cref="BodyWords"/> is sorted by position instead.
    /// </summary>
    public IReadOnlyList<string> ContentOrderBodyWords { get; }

    public int? FooterNumber { get; }

    /// <summary>The running head at the top of a paper page: its sans words above the page number line.</summary>
    public string HeaderText { get; }

    public IReadOnlyList<PositionedWord> PositionedWords { get; }

    private static bool IsSans(Word word) => IsSans(word.FontName);

    private static bool IsSans(string? fontName) => fontName?.Contains("LiberationSans", StringComparison.Ordinal) == true;

    /// <summary>A word ends at a space, a baseline change or a horizontal gap wider than a fifth of the font size.</summary>
    private static List<string> ContentOrderWords(IEnumerable<Letter> letters)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        Letter? previous = null;
        foreach (var letter in letters)
        {
            var separated = previous is not null
                && (Math.Abs(letter.StartBaseLine.Y - previous.StartBaseLine.Y) > 0.5
                    || Math.Abs(letter.StartBaseLine.X - previous.EndBaseLine.X) > letter.PointSize / 5);
            if ((string.IsNullOrWhiteSpace(letter.Value) || separated) && current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }

            if (!string.IsNullOrWhiteSpace(letter.Value))
            {
                current.Append(letter.Value);
            }

            previous = letter;
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}

/// <param name="Baseline">Baseline of the word's first letter, in points from the bottom of the page.</param>
public sealed record PositionedWord(string Text, double Baseline, double Left, double Right, double Size, bool Sans);
