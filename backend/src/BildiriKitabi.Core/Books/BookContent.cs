using BildiriKitabi.Core.Documents;

namespace BildiriKitabi.Core.Books;

/// <summary>Everything the renderer needs to lay out a book; papers are already sanitized and ordered.</summary>
public sealed record BookContent(string Title, DateTimeOffset CreatedAt, IReadOnlyList<BookPaper> Papers);

public sealed record BookPaper(string Title, SourceDocument Document);

public readonly record struct PageRange(int Start, int End);

public sealed record RenderedBook(byte[] Pdf, int PageCount, IReadOnlyList<PageRange> PaperPages);

public interface IBookRenderer
{
    RenderedBook Render(BookContent book);
}

public sealed record PdfLeakScanResult(int EmailCount, int PhoneCount)
{
    public bool HasLeak => EmailCount > 0 || PhoneCount > 0;
}

public interface IPdfLeakScanner
{
    PdfLeakScanResult Scan(byte[] pdf);
}
