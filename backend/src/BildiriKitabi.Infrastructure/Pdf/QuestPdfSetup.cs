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

    private const string FontResourcePrefix = "BildiriKitabi.Fonts.";

    private static readonly Lazy<bool> Initialization = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureInitialized() => _ = Initialization.Value;

    private static bool Initialize()
    {
        // Community license: free for organizations below USD 1M annual revenue (see README).
        Settings.License = LicenseType.Community;

        // Only the bundled fonts are used so the output does not depend on the machine it runs on.
        Settings.UseSystemFonts = false;
        Settings.FontDiscoveryPath = null;

        var assembly = typeof(QuestPdfSetup).Assembly;
        var fonts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(FontResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (fonts.Count == 0)
        {
            throw new InvalidOperationException("Bundled fonts were not found in the assembly resources.");
        }

        foreach (var font in fonts)
        {
            using var stream = assembly.GetManifestResourceStream(font)
                ?? throw new InvalidOperationException($"Font resource '{font}' could not be opened.");
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }
}
