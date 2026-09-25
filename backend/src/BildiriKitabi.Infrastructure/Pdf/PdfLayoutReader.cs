using BildiriKitabi.Core.Books;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;

namespace BildiriKitabi.Infrastructure.Pdf;

internal sealed record PdfLayout(int PageCount, IReadOnlyList<PageRange> PaperPages);

/// <summary>
/// Recovers each paper's page range from the finished PDF: the table-of-contents entries are the only internal
/// links in the book, and each one points at the first page of its paper.
/// </summary>
internal static class PdfLayoutReader
{
    public static PdfLayout Read(byte[] pdf, int paperCount)
    {
        using var document = PdfDocument.Open(pdf);
        var starts = new List<int>();
        foreach (var page in document.GetPages())
        {
            var targets = page.GetAnnotations()
                .Where(a => a.Type == AnnotationType.Link && a.Action is GoToAction)
                .OrderByDescending(a => a.Rectangle.Top)
                .Select(a => ((GoToAction)a.Action!).Destination.PageNumber);
            starts.AddRange(targets);
        }

        if (starts.Count != paperCount || starts.Zip(starts.Skip(1)).Any(pair => pair.First >= pair.Second))
        {
            throw new BookGenerationException(
                BookErrorCodes.RenderFailed,
                "İçindekiler bağlantıları bildirilerle eşleşmiyor; PDF yayımlanmadı.");
        }

        var pageCount = document.NumberOfPages;
        var ranges = starts
            .Select((start, i) => new PageRange(start, i + 1 < starts.Count ? starts[i + 1] - 1 : pageCount))
            .ToList();
        return new PdfLayout(pageCount, ranges);
    }
}
