using System.Text;
using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Fonts;

namespace BildiriKitabi.Infrastructure.Pdf;

/// <summary>
/// Glyph coverage of the fonts the renderer really uses, read from the cmap tables of the bundled font files.
/// A character is supported when the Liberation face of the requested style has it, or the DejaVu fallback face
/// QuestPDF would pick for that style (DejaVu is bundled in Regular and Bold; italic falls back to those).
/// </summary>
public sealed class FontGlyphCoverage : IGlyphCoverage
{
    private static readonly Lazy<FontGlyphCoverage> Bundled = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, HashSet<int>> _faces;

    private FontGlyphCoverage(Dictionary<string, HashSet<int>> faces)
    {
        _faces = faces;
    }

    public static FontGlyphCoverage Instance => Bundled.Value;

    public bool IsSupported(Rune character, FontFamilyKind family, bool bold, bool italic)
    {
        var sans = family == FontFamilyKind.Sans;
        var primary = (sans ? "LiberationSans-" : "LiberationSerif-") + (bold, italic) switch
        {
            (true, true) => "BoldItalic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular",
        };
        var fallback = (sans ? "DejaVuSans" : "DejaVuSerif") + (bold ? "-Bold" : string.Empty);
        return Face(primary).Contains(character.Value) || Face(fallback).Contains(character.Value);
    }

    private HashSet<int> Face(string name) =>
        _faces.TryGetValue(name, out var codePoints)
            ? codePoints
            : throw new InvalidOperationException($"Bundled font '{name}.ttf' is missing.");

    private static FontGlyphCoverage Load()
    {
        var faces = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var (fileName, open) in QuestPdfSetup.BundledFonts())
        {
            using var stream = open();
            faces[Path.GetFileNameWithoutExtension(fileName)] = TrueTypeCmap.ReadCodePoints(stream);
        }

        return new FontGlyphCoverage(faces);
    }
}
