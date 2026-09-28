using System.Text.RegularExpressions;
using BildiriKitabi.Core.Fonts;
using BildiriKitabi.Core.Sanitization;

namespace BildiriKitabi.Core.Books;

public sealed record PaperTitleValidation(string Title, string? ErrorCode, string? ErrorMessage)
{
    public bool IsValid => ErrorCode is null;
}

/// <summary>
/// Checks a paper title the user typed. The title is printed in the table of contents, the running head and on the
/// first page of the paper, so it must not be empty, must fit the column and must not bring back contact details.
/// </summary>
public static partial class PaperTitleValidator
{
    public const string InvalidCode = "PAPER_TITLE_INVALID";
    public const string ContactInfoCode = "PAPER_TITLE_CONTACT_INFO";
    public const string UnsupportedCharacterCode = "PAPER_TITLE_UNSUPPORTED_CHARACTER";

    /// <summary>Line breaks become spaces and the result is trimmed before any rule is checked.</summary>
    public static PaperTitleValidation Validate(string? title, IGlyphCoverage glyphCoverage)
    {
        ArgumentNullException.ThrowIfNull(glyphCoverage);

        var normalized = LineBreakRegex().Replace(title ?? string.Empty, " ").Trim();
        if (normalized.Length == 0)
        {
            return Invalid(normalized, InvalidCode, "Bildiri başlığı boş olamaz.");
        }

        if (normalized.Length > Paper.TitleMaxLength)
        {
            return Invalid(normalized, InvalidCode, $"Bildiri başlığı en fazla {Paper.TitleMaxLength} karakter olabilir.");
        }

        if (normalized.Any(char.IsControl))
        {
            return Invalid(normalized, InvalidCode, "Bildiri başlığı kontrol karakteri içeremez.");
        }

        if (ContactInfoDetector.Find(normalized).Count > 0)
        {
            return Invalid(normalized, ContactInfoCode, "Başlıkta e-posta adresi veya telefon numarası bulunamaz.");
        }

        var unsupported = UnsupportedCharacters.InPaperTitle(glyphCoverage, normalized);
        if (unsupported.Count > 0)
        {
            return Invalid(
                normalized,
                UnsupportedCharacterCode,
                unsupported.Count == 1
                    ? $"Başlıktaki {UnsupportedCharacters.Describe(unsupported)} karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın."
                    : $"Başlıktaki {UnsupportedCharacters.Describe(unsupported)} karakterleri PDF yazı tipinde bulunmuyor; lütfen kaldırın.");
        }

        return new PaperTitleValidation(normalized, null, null);
    }

    private static PaperTitleValidation Invalid(string title, string code, string message) => new(title, code, message);

    [GeneratedRegex(@"\r\n|[\r\n  \u0085]")]
    private static partial Regex LineBreakRegex();
}
