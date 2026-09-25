using System.Text;
using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Core.Fonts;

/// <summary>Answers whether the PDF fonts can print a character in a given family and style.</summary>
public interface IGlyphCoverage
{
    bool IsSupported(Rune character, FontFamilyKind family, bool bold, bool italic);
}

/// <summary>
/// Finds characters that the book's font chains cannot print, with the same family and style the renderer uses
/// for each piece of text. Control characters (line breaks, tabs) are laid out by the renderer, not drawn.
/// </summary>
public static class UnsupportedCharacters
{
    /// <summary>The book name is printed bold in serif on the cover and regular in sans in every running head.</summary>
    public static IReadOnlyList<string> InBookName(IGlyphCoverage coverage, string bookName)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(bookName);

        var found = new List<string>();
        Collect(coverage, bookName, FontFamilyKind.Serif, bold: true, italic: false, found);
        Collect(coverage, bookName, FontFamilyKind.Sans, bold: false, italic: false, found);
        return found;
    }

    /// <summary>
    /// Every run of the paper in its own family and style, plus the title, which the table of contents (serif) and
    /// the running head (sans) print again.
    /// </summary>
    public static IReadOnlyList<string> InPaper(IGlyphCoverage coverage, SourceDocument document, string title)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(title);

        var found = new List<string>();
        var paragraphs = document.Paragraphs.Concat(document.Footnotes.SelectMany(f => f.Paragraphs));
        foreach (var run in paragraphs.SelectMany(p => p.Runs))
        {
            Collect(coverage, run.Text, run.FontFamilyKind, run.Bold, run.Italic, found);
        }

        Collect(coverage, title, FontFamilyKind.Serif, bold: false, italic: false, found);
        Collect(coverage, title, FontFamilyKind.Sans, bold: false, italic: false, found);
        return found;
    }

    /// <summary>"'𝒜'" or "'𝒜', '😀'" — at most five characters, for user-facing messages.</summary>
    public static string Describe(IReadOnlyList<string> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        var shown = string.Join(", ", characters.Take(5).Select(c => $"'{c}'"));
        return characters.Count > 5 ? $"{shown} ve {characters.Count - 5} karakter daha" : shown;
    }

    private static void Collect(IGlyphCoverage coverage, string text, FontFamilyKind family, bool bold, bool italic, List<string> found)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || coverage.IsSupported(rune, family, bold, italic))
            {
                continue;
            }

            var value = rune.ToString();
            if (!found.Contains(value))
            {
                found.Add(value);
            }
        }
    }
}
