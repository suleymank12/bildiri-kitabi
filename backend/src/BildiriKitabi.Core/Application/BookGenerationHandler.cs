using System.Threading.Channels;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Core.Application;

/// <summary>
/// Generates the PDF of one queued book. Idempotent, so a message delivered twice is harmless: only a book that is
/// still <c>Queued</c> is claimed, and the claim is a conditional update that only one handler can win.
/// Runs in its own DI scope; its <see cref="IAppDbContext"/> is only ever used by one flow at a time.
/// </summary>
public sealed partial class BookGenerationHandler(
    IAppDbContext db,
    IFileStorage storage,
    BookGenerator generator,
    IOptions<GenerationOptions> options,
    TimeProvider timeProvider,
    ILogger<BookGenerationHandler> logger)
{
    public const string UnexpectedErrorMessage = "Kitap oluşturulurken beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.";

    private const int ProgressStep = 5;

    public async Task HandleAsync(Guid bookId, CancellationToken stoppingToken)
    {
        if (!await TryClaimAsync(bookId, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        var timeoutSeconds = options.Value.TimeoutSeconds;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await GenerateAsync(bookId, timeout.Token, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application shutdown: the book stays Processing and the startup recovery queues it again.
            LogInterrupted(logger, bookId);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            LogTimedOut(logger, bookId, timeoutSeconds);
            await FailAsync(
                bookId,
                BookErrorCodes.GenerationTimeout,
                $"Kitap oluşturma {timeoutSeconds} saniyelik süre sınırını aştı. Lütfen tekrar deneyin.").ConfigureAwait(false);
        }
        catch (BookGenerationException ex)
        {
            LogGenerationFailed(logger, bookId, ex.Code);
            await FailAsync(bookId, ex.Code, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUnexpectedError(logger, ex, bookId);
            await FailAsync(bookId, BookErrorCodes.InternalError, UnexpectedErrorMessage).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryClaimAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var status = await db.Books.AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => (BookStatus?)b.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (status != BookStatus.Queued)
        {
            LogSkipped(logger, bookId, status);
            return false;
        }

        var startedAt = UtcNow();
        var claimed = await db.Books
            .Where(b => b.Id == bookId && b.Status == BookStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.Status, BookStatus.Processing)
                    .SetProperty(b => b.ProcessingStartedAt, startedAt)
                    .SetProperty(b => b.Stage, (GenerationStage?)null)
                    .SetProperty(b => b.ProgressPercent, (byte)0),
                cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            LogAlreadyClaimed(logger, bookId);
            return false;
        }

        return true;
    }

    private async Task GenerateAsync(Guid bookId, CancellationToken timeoutToken, CancellationToken stoppingToken)
    {
        var book = await db.Books.Include(b => b.Papers).FirstAsync(b => b.Id == bookId, stoppingToken).ConfigureAwait(false);
        var papers = book.Papers.OrderBy(p => p.Order).ToList();

        var streams = new List<Stream>(papers.Count);
        try
        {
            foreach (var paper in papers)
            {
                streams.Add(await storage.OpenReadAsync(paper.StorageKey, timeoutToken).ConfigureAwait(false));
            }

            var sources = papers.Select((paper, i) => new PaperSource(paper.OriginalFileName, () => streams[i])).ToList();
            var progress = Channel.CreateUnbounded<GenerationProgress>(new UnboundedChannelOptions { SingleReader = true });
            var progressWriter = WriteProgressAsync(book, progress.Reader, stoppingToken);

            GeneratedBook result;
            try
            {
                // The generator is synchronous and CPU-bound, so it runs on a pool thread; the worker only awaits it.
                var work = Task.Run(
                    () => generator.Generate(book.Name, sources, new ChannelProgress(progress.Writer), timeoutToken),
                    CancellationToken.None);
                _ = work.ContinueWith(t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                result = await work.WaitAsync(timeoutToken).ConfigureAwait(false);
            }
            finally
            {
                progress.Writer.TryComplete();
                await progressWriter.ConfigureAwait(false);
            }

            book.ReportProgress(GenerationStage.Saving, 97);
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            var key = StorageKeys.Output(book.Id);
            using (var pdf = new MemoryStream(result.Pdf, writable: false))
            {
                await storage.SaveAsync(key, pdf, stoppingToken).ConfigureAwait(false);
            }

            for (var i = 0; i < papers.Count; i++)
            {
                var generated = result.Papers[i];
                papers[i].RecordGeneration(generated.StartPage, generated.EndPage, generated.RemovedEmailCount, generated.RemovedPhoneCount);
            }

            book.MarkCompleted(key, result.Pdf.LongLength, result.PageCount, UtcNow());
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            LogCompleted(logger, bookId, result.PageCount);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Writes progress when the stage changes or the percentage grew by at least five points.</summary>
    private async Task WriteProgressAsync(Book book, ChannelReader<GenerationProgress> reader, CancellationToken cancellationToken)
    {
        GenerationStage? lastStage = null;
        var lastPercent = int.MinValue;
        var writing = true;
        await foreach (var progress in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (!writing || (progress.Stage == lastStage && progress.Percent < lastPercent + ProgressStep))
            {
                continue;
            }

            try
            {
                book.ReportProgress(progress.Stage, progress.Percent);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                lastStage = progress.Stage;
                lastPercent = progress.Percent;
            }
            catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException)
            {
                // Progress is informative only; the outcome is written by the caller.
                LogProgressWriteFailed(logger, ex, book.Id);
                writing = false;
            }
        }
    }

    /// <summary>
    /// Writes the failure. Never throws: when the database cannot be reached the book stays <c>Processing</c> and the
    /// sweeper recovers it once the time limit has passed.
    /// </summary>
    internal async Task FailAsync(Guid bookId, string code, string message)
    {
        try
        {
            db.ChangeTracker.Clear();
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, CancellationToken.None).ConfigureAwait(false);
            if (book is not { Status: BookStatus.Processing })
            {
                return;
            }

            book.MarkFailed(code, message, UtcNow());
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await storage.DeleteAsync(StorageKeys.Output(bookId), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailWriteFailed(logger, ex, bookId);
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Reports synchronously into the channel; <see cref="Progress{T}"/> would post to the thread pool.</summary>
    private sealed class ChannelProgress(ChannelWriter<GenerationProgress> writer) : IProgress<GenerationProgress>
    {
        public void Report(GenerationProgress value) => writer.TryWrite(value);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Book {BookId} skipped; status is {Status} (empty when the book no longer exists)")]
    private static partial void LogSkipped(ILogger logger, Guid bookId, BookStatus? status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Book {BookId} skipped; another handler claimed it")]
    private static partial void LogAlreadyClaimed(ILogger logger, Guid bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId} completed with {PageCount} pages")]
    private static partial void LogCompleted(ILogger logger, Guid bookId, int pageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookId} interrupted by shutdown; it will be queued again on startup")]
    private static partial void LogInterrupted(ILogger logger, Guid bookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book {BookId} timed out after {TimeoutSeconds} s")]
    private static partial void LogTimedOut(ILogger logger, Guid bookId, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book {BookId} failed with {ErrorCode}")]
    private static partial void LogGenerationFailed(ILogger logger, Guid bookId, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Book {BookId} failed with an unexpected error")]
    private static partial void LogUnexpectedError(ILogger logger, Exception exception, Guid bookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Progress of book {BookId} could not be written")]
    private static partial void LogProgressWriteFailed(ILogger logger, Exception exception, Guid bookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failure state of book {BookId} could not be written")]
    private static partial void LogFailWriteFailed(ILogger logger, Exception exception, Guid bookId);
}
