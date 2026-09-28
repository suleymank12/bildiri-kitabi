using System.Globalization;
using System.Text;

namespace BildiriKitabi.Core.Books;

/// <summary>
/// File names for the downloaded PDF: an ASCII fallback for old clients plus the full UTF-8 name (RFC 6266 / 5987).
/// </summary>
public static class PdfFileName
{
    private const int MaxSlugLength = 80;
    private const string AsciiPrefix = "bildiri-kitabi";

    /// <summary>For example <c>bildiri-kitabi-ornek-bilim-kongresi-2026.pdf</c>.</summary>
    public static string Ascii(string bookName)
    {
        var slug = Slug(bookName);
        return slug.Length == 0 ? $"{AsciiPrefix}.pdf" : $"{AsciiPrefix}-{slug}.pdf";
    }

    /// <summary>The book name as typed, minus characters that are not allowed in file names.</summary>
    public static string Unicode(string bookName)
    {
        ArgumentNullException.ThrowIfNull(bookName);
        var builder = new StringBuilder(bookName.Length);
        foreach (var c in bookName)
        {
            builder.Append(char.IsControl(c) || "\\/:*?\"<>|".Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        var name = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (name.Length > Book.NameMaxLength)
        {
            name = name[..Book.NameMaxLength].TrimEnd(' ', '.');
        }

        return name.Length == 0 ? Ascii(bookName) : $"{name}.pdf";
    }

    public static string ContentDisposition(string bookName, bool download) =>
        $"{(download ? "attachment" : "inline")}; filename=\"{Ascii(bookName)}\"; filename*=UTF-8''{Uri.EscapeDataString(Unicode(bookName))}";

    private static string Slug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var transliterated = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            transliterated.Append(c switch
            {
                'ı' => 'i',
                'İ' => 'I',
                'ğ' => 'g',
                'Ğ' => 'G',
                'ş' => 's',
                'Ş' => 'S',
                _ => c,
            });
        }

        var slug = new StringBuilder(transliterated.Length);
        var pendingDash = false;
        foreach (var c in transliterated.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingDash && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(lower);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        var result = slug.ToString();
        return result.Length <= MaxSlugLength ? result : result[..MaxSlugLength].TrimEnd('-');
    }
}
