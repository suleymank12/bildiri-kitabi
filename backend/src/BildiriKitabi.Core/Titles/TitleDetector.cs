using System.Text.RegularExpressions;
using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Core.Titles;

public enum TitleSource
{
    TitleStyle,
    FirstBoldParagraph,
    FileName,

    /// <summary>Typed by the user after the upload; replaces the detected title everywhere in the book.</summary>
    Manual,
}

public sealed record DetectedTitle(string Text, TitleSource Source);

public static partial class TitleDetector
{
    private const int BoldParagraphSearchLimit = 5;

    private static readonly string[] TitleStyleNames = ["Title", "heading 1"];

    public static DetectedTitle Detect(SourceDocument document, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileName);

        return FindTitleParagraph(document) is var (paragraph, source) && paragraph is not null
            ? new DetectedTitle(ToTitleText(paragraph), source)
            : new DetectedTitle(FromFileName(fileName), TitleSource.FileName);
    }

    /// <summary>
    /// The paragraph the title was taken from (rules 1 and 2), or null when the title came from the file name.
    /// Generation uses it to print a title the user typed in place of the detected one.
    /// </summary>
    public static (Paragraph? Paragraph, TitleSource Source) FindTitleParagraph(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = document.Blocks.OfType<Paragraph>().Where(p => !p.IsBlank).ToList();

        var styled = paragraphs.FirstOrDefault(p =>
            p.Style is { } style && TitleStyleNames.Any(name => string.Equals(style, name, StringComparison.OrdinalIgnoreCase)));
        if (styled is not null)
        {
            return (styled, TitleSource.TitleStyle);
        }

        var bold = paragraphs
            .Take(BoldParagraphSearchLimit)
            .FirstOrDefault(p => p.Alignment == ParagraphAlignment.Center
                && p.Runs.Where(r => !string.IsNullOrWhiteSpace(r.Text)).All(r => r.Bold));
        return bold is not null ? (bold, TitleSource.FirstBoldParagraph) : (null, TitleSource.FileName);
    }

    /// <summary>
    /// The document with the text of its title paragraph replaced by <paramref name="title"/>, printed with the
    /// paragraph's own layout and the formatting of its first run. Unchanged when the document has no title paragraph.
    /// </summary>
    public static SourceDocument WithTitle(SourceDocument document, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (FindTitleParagraph(document).Paragraph is not { } paragraph)
        {
            return document;
        }

        var format = paragraph.Runs.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Text)) ?? paragraph.Runs[0];
        var replaced = paragraph with { Runs = [format with { Text = title, Hyperlink = null }] };
        var blocks = document.Blocks.Select(b => ReferenceEquals(b, paragraph) ? replaced : b).ToList();
        return document with { Blocks = blocks };
    }

    private static string ToTitleText(Paragraph paragraph) =>
        LineBreakRegex().Replace(paragraph.Text, " ").Trim();

    private static string FromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var withoutPrefix = OrderPrefixRegex().Replace(name, string.Empty);
        var title = withoutPrefix.Replace('_', ' ').Trim();
        return title.Length > 0 ? title : name;
    }

    [GeneratedRegex(@"[\r\n\t]+")]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"^\d+[_\-.\s]+")]
    private static partial Regex OrderPrefixRegex();
}
