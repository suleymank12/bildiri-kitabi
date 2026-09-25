using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Sanitization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace BildiriKitabi.Infrastructure.Pdf;

/// <summary>
/// Defense in depth: extracts the text and metadata of the finished PDF and looks for anything that still
/// looks like an e-mail address or phone number. Contact values the user typed into the book name are permitted
/// (see <see cref="PermittedContactValues"/>). Only counts are reported, never the matched values.
/// </summary>
public sealed class PdfPigLeakScanner : IPdfLeakScanner
{
    public PdfLeakScanResult Scan(byte[] pdf, string bookName)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var permitted = PermittedContactValues.FromText(bookName);
        using var document = PdfDocument.Open(pdf);
        var emails = 0;
        var phones = 0;

        void Count(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (var match in ContactInfoDetector.Find(text))
            {
                if (permitted.Permits(match.Kind, text.Substring(match.Start, match.Length)))
                {
                    continue;
                }

                if (match.Kind == ContactKind.Email)
                {
                    emails++;
                }
                else
                {
                    phones++;
                }
            }
        }

        foreach (var page in document.GetPages())
        {
            Count(ContentOrderTextExtractor.GetText(page));
        }

        var info = document.Information;
        Count(info.Title);
        Count(info.Author);
        Count(info.Subject);
        Count(info.Keywords);
        Count(info.Creator);
        Count(info.Producer);
        if (document.TryGetXmpMetadata(out var xmp))
        {
            Count(xmp.GetXDocument().ToString());
        }

        return new PdfLeakScanResult(emails, phones);
    }
}
