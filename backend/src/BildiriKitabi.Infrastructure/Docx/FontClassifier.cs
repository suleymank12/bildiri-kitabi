using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Infrastructure.Docx;

/// <summary>
/// Maps a source font to the book's two families. Unknown fonts are treated as serif, the book's body face.
/// </summary>
internal static class FontClassifier
{
    private static readonly string[] SansSerifFamilies =
    [
        "Arial", "Calibri", "Helvetica", "Verdana", "Tahoma", "Segoe", "Trebuchet", "Candara", "Corbel", "Aptos",
        "Roboto", "Lato", "Montserrat", "Gill Sans", "Futura", "Franklin Gothic", "Century Gothic", "Lucida Sans",
        "Myriad", "Frutiger", "Univers", "Avenir", "Inter", "Source Sans", "Noto Sans", "DejaVu Sans", "Open Sans",
    ];

    public static FontFamilyKind Classify(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return FontFamilyKind.Serif;
        }

        if (fontName.Contains("Sans", StringComparison.OrdinalIgnoreCase)
            || SansSerifFamilies.Any(f => fontName.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
        {
            return FontFamilyKind.Sans;
        }

        return FontFamilyKind.Serif;
    }
}
