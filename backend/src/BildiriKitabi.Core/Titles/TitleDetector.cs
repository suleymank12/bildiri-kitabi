using System.Text.RegularExpressions;
using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Core.Titles;

public enum TitleSource
{
    TitleStyle,
    FirstBoldParagraph,
    FileName,
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

        var paragraphs = document.Blocks.OfType<Paragraph>().Where(p => !p.IsBlank).ToList();

        var styled = paragraphs.FirstOrDefault(p =>
            p.Style is { } style && TitleStyleNames.Any(name => string.Equals(style, name, StringComparison.OrdinalIgnoreCase)));
        if (styled is not null)
        {
            return new DetectedTitle(ToTitleText(styled), TitleSource.TitleStyle);
        }

        var bold = paragraphs
            .Take(BoldParagraphSearchLimit)
            .FirstOrDefault(p => p.Alignment == ParagraphAlignment.Center
                && p.Runs.Where(r => !string.IsNullOrWhiteSpace(r.Text)).All(r => r.Bold));
        if (bold is not null)
        {
            return new DetectedTitle(ToTitleText(bold), TitleSource.FirstBoldParagraph);
        }

        return new DetectedTitle(FromFileName(fileName), TitleSource.FileName);
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
