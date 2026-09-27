using QuestPDF;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace BildiriKitabi.Infrastructure.Pdf;

/// <summary>
/// The single place where QuestPDF is configured: license, fonts and font discovery.
/// </summary>
public static class QuestPdfSetup
{
    public const string SerifFamily = "Liberation Serif";
    public const string SansFamily = "Liberation Sans";
    public const string SerifFallbackFamily = "DejaVu Serif";
    public const string SansFallbackFamily = "DejaVu Sans";

    /// <summary>
    /// Font chains handed to QuestPDF: Liberation (metric-compatible with Times New Roman / Arial) first, DejaVu for
    /// the symbols Liberation lacks (math operators, arrows, …). Text that Liberation covers is laid out exactly as before.
    /// </summary>
    internal static readonly string[] SerifFonts = [SerifFamily, SerifFallbackFamily];

    internal static readonly string[] SansFonts = [SansFamily, SansFallbackFamily];

    private const string FontResourcePrefix = "BildiriKitabi.Fonts.";

    private static readonly Lazy<bool> Initialization = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Initialization.Value;

    /// <summary>The bundled font files: file name and a way to open it.</summary>
    internal static IEnumerable<(string FileName, Func<Stream> Open)> BundledFonts()
    {
        var assembly = typeof(QuestPdfSetup).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(FontResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(n => (n[FontResourcePrefix.Length..], (Func<Stream>)(() => assembly.GetManifestResourceStream(n)
                ?? throw new InvalidOperationException($"Font resource '{n}' could not be opened."))));
    }

    private static bool Initialize()
    {
        // Community license: free for organizations below USD 1M annual revenue (see docs/kutuphaneler.md).
        Settings.License = LicenseType.Community;

        // Only the bundled fonts are used so the output does not depend on the machine it runs on.
        Settings.UseSystemFonts = false;
        Settings.FontDiscoveryPath = null;

        // A character missing from the whole chain must stop generation, never print as an empty box.
        Settings.ThrowOnMissingTextGlyphs = true;

        var fonts = BundledFonts().ToList();
        if (fonts.Count == 0)
        {
            throw new InvalidOperationException("Bundled fonts were not found in the assembly resources.");
        }

        foreach (var (_, open) in fonts)
        {
            using var stream = open();
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }
}
