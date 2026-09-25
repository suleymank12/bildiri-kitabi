using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BildiriKitabi.IntegrationTests.Books;

/// <summary>
/// The test's own, deliberately simple view of a source .docx, independent of the production reader and sanitizer.
/// </summary>
public sealed partial class SourcePaper
{
    private SourcePaper(string fileName, IReadOnlyList<string> paragraphs, IReadOnlyList<string> metadata)
    {
        FileName = fileName;
        Paragraphs = paragraphs;
        CoreMetadata = metadata;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Paragraphs { get; }

    /// <summary>dc:title, dc:creator and cp:lastModifiedBy of docProps/core.xml, as found at run time.</summary>
    public IReadOnlyList<string> CoreMetadata { get; }

    public string Title => Paragraphs.First(p => !string.IsNullOrWhiteSpace(p)).Trim();

    /// <summary>
    /// Word sequence expected in the book: contact lines (the only paragraphs with an '@') keep nothing but their ORCID.
    /// </summary>
    public IReadOnlyList<string> ExpectedWords => Paragraphs
        .Select(p => p.Contains('@', StringComparison.Ordinal) ? string.Join(' ', OrcidRegex().Matches(p).Select(m => m.Value)) : p)
        .SelectMany(p => p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .ToList();

    public static SourcePaper Load(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<W.Paragraph>().Select(ParagraphText).ToList();
        var properties = document.PackageProperties;
        var metadata = new[] { properties.Title, properties.Creator, properties.LastModifiedBy }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        return new SourcePaper(Path.GetFileName(path), paragraphs, metadata);
    }

    private static string ParagraphText(W.Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case W.Text t:
                    text.Append(t.Text);
                    break;
                case W.TabChar:
                case W.Break:
                    text.Append(' ');
                    break;
            }
        }

        return text.ToString();
    }

    [GeneratedRegex(@"ORCID:?\s*\d{4}-\d{4}-\d{4}-\d{3}[\dX]")]
    private static partial Regex OrcidRegex();
}
