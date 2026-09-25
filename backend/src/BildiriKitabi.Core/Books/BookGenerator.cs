using BildiriKitabi.Core.Documents;
using BildiriKitabi.Core.Sanitization;
using BildiriKitabi.Core.Titles;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Books;

public sealed record PaperSource(string FileName, Func<Stream> OpenRead);

public sealed record GeneratedPaper(
    string FileName,
    string Title,
    TitleSource TitleSource,
    int StartPage,
    int EndPage,
    int RemovedEmailCount,
    int RemovedPhoneCount);

public sealed record GeneratedBook(byte[] Pdf, int PageCount, IReadOnlyList<GeneratedPaper> Papers);

/// <summary>
/// Runs the whole pipeline for one book: read → sanitize → detect titles → render → verify.
/// Reported percentages follow completed work; nothing is simulated.
/// </summary>
public sealed partial class BookGenerator(
    IDocxReader reader,
    IBookRenderer renderer,
    IPdfLeakScanner leakScanner,
    TimeProvider timeProvider,
    ILogger<BookGenerator> logger)
{
    public GeneratedBook Generate(
        string bookName,
        IReadOnlyList<PaperSource> papers,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookName);
        ArgumentNullException.ThrowIfNull(papers);
        if (papers.Count == 0)
        {
            throw new ArgumentException("At least one paper is required.", nameof(papers));
        }

        var documents = new List<SourceDocument>(papers.Count);
        for (var i = 0; i < papers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new GenerationProgress(GenerationStage.Reading, Scale(i, papers.Count, 0, 30)));
            documents.Add(Read(papers[i], i + 1));
        }

        var sanitized = new List<DocumentSanitizationResult>(papers.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new GenerationProgress(GenerationStage.Sanitizing, Scale(i, papers.Count, 30, 40)));
            sanitized.Add(ContactInfoSanitizer.Sanitize(documents[i]));
        }

        progress?.Report(new GenerationProgress(GenerationStage.Composing, 40));
        var titles = sanitized.Select((s, i) => TitleDetector.Detect(s.Document, papers[i].FileName)).ToList();
        var content = new BookContent(
            bookName,
            timeProvider.GetLocalNow(),
            sanitized.Select((s, i) => new BookPaper(titles[i].Text, s.Document)).ToList());

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new GenerationProgress(GenerationStage.Rendering, 45));
        var rendered = renderer.Render(content);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new GenerationProgress(GenerationStage.Verifying, 85));
        var scan = leakScanner.Scan(rendered.Pdf);
        if (scan.HasLeak)
        {
            LogLeakDetected(logger, scan.EmailCount, scan.PhoneCount);
            throw new BookGenerationException(
                BookErrorCodes.ContactLeakDetected,
                "Oluşturulan PDF'te temizlenemeyen iletişim bilgisi tespit edildi; PDF yayımlanmadı.");
        }

        if (rendered.PaperPages.Count != papers.Count)
        {
            throw new BookGenerationException(
                BookErrorCodes.RenderFailed,
                "Oluşturulan PDF'teki bildiri sayısı yüklenen dosya sayısıyla eşleşmiyor.");
        }

        var generated = papers
            .Select((paper, i) => new GeneratedPaper(
                paper.FileName,
                titles[i].Text,
                titles[i].Source,
                rendered.PaperPages[i].Start,
                rendered.PaperPages[i].End,
                sanitized[i].RemovedEmailCount,
                sanitized[i].RemovedPhoneCount))
            .ToList();

        var removedEmails = generated.Sum(p => p.RemovedEmailCount);
        var removedPhones = generated.Sum(p => p.RemovedPhoneCount);
        LogGenerated(logger, papers.Count, rendered.PageCount, removedEmails, removedPhones);
        progress?.Report(new GenerationProgress(GenerationStage.Verifying, 95));
        return new GeneratedBook(rendered.Pdf, rendered.PageCount, generated);
    }

    private SourceDocument Read(PaperSource paper, int order)
    {
        try
        {
            using var stream = paper.OpenRead();
            return reader.Read(stream);
        }
        catch (InvalidDocumentException ex)
        {
            throw new BookGenerationException(
                BookErrorCodes.InvalidDocument,
                $"{order}. sıradaki bildiri okunamadı: {ex.Message}",
                ex);
        }
    }

    private static int Scale(int completed, int total, int from, int to) =>
        from + ((to - from) * completed / total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contact leak detected in rendered PDF: {EmailCount} e-mail(s), {PhoneCount} phone number(s)")]
    private static partial void LogLeakDetected(ILogger logger, int emailCount, int phoneCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book generated: {PaperCount} papers, {PageCount} pages, removed {EmailCount} e-mail(s) and {PhoneCount} phone number(s)")]
    private static partial void LogGenerated(ILogger logger, int paperCount, int pageCount, int emailCount, int phoneCount);
}
