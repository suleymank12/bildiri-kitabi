using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Application;

public enum StartGenerationOutcome
{
    Started,
    NotFound,
    AlreadyInProgress,
    AlreadyCompleted,
}

public enum DeleteBookOutcome
{
    Deleted,
    NotFound,
    InProgress,
}

public enum RestoreBookOutcome
{
    Restored,
    NotFound,
}

/// <summary>
/// State-changing commands on an existing book. Each one is a single conditional SQL statement, so two concurrent
/// requests can never both succeed. Editing the name and the paper order is in <see cref="BookEditService"/>.
/// </summary>
public sealed partial class BookCommandService(
    IAppDbContext db,
    IBookGenerationQueue queue,
    TimeProvider timeProvider,
    ILogger<BookCommandService> logger)
{
    /// <summary>
    /// Queues generation with one conditional update (<c>Uploaded</c>/<c>Failed</c> → <c>Queued</c>), then enqueues
    /// the id. If enqueueing fails the book stays <c>Queued</c> and the queued-book sweeper enqueues it again later.
    /// </summary>
    public async Task<StartGenerationOutcome> StartGenerationAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var queued = await db.Books
            .Where(b => b.Uid == bookUid && (b.Status == BookStatus.Uploaded || b.Status == BookStatus.Failed))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.Status, BookStatus.Queued)
                    .SetProperty(b => b.QueuedAt, now)
                    .SetProperty(b => b.Stage, (GenerationStage?)null)
                    .SetProperty(b => b.ProgressPercent, (byte)0)
                    .SetProperty(b => b.ErrorCode, (string?)null)
                    .SetProperty(b => b.ErrorMessage, (string?)null)
                    .SetProperty(b => b.ProcessingStartedAt, (DateTime?)null)
                    .SetProperty(b => b.ProcessingFinishedAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);

        if (queued == 0)
        {
            var status = await db.Books.AsNoTracking()
                .Where(b => b.Uid == bookUid)
                .Select(b => (BookStatus?)b.Status)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return status switch
            {
                null => StartGenerationOutcome.NotFound,
                BookStatus.Completed => StartGenerationOutcome.AlreadyCompleted,
                _ => StartGenerationOutcome.AlreadyInProgress,
            };
        }

        try
        {
            await queue.EnqueueAsync(bookUid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEnqueueFailed(logger, ex, bookUid);
        }

        LogQueued(logger, bookUid);
        return StartGenerationOutcome.Started;
    }

    /// <summary>
    /// Deletes the book softly: <c>AktifMi = 0</c> and <c>SilinmeZamani</c> are set; the row, its papers and every stored
    /// file stay, and the global query filter hides the book from then on, so a second delete finds nothing.
    /// </summary>
    public async Task<DeleteBookOutcome> DeleteAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var deleted = await db.Books
            .Where(b => b.Uid == bookUid && b.Status != BookStatus.Queued && b.Status != BookStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.IsActive, false)
                    .SetProperty(b => b.DeletedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 0)
        {
            var exists = await db.Books.AnyAsync(b => b.Uid == bookUid, cancellationToken).ConfigureAwait(false);
            return exists ? DeleteBookOutcome.InProgress : DeleteBookOutcome.NotFound;
        }

        LogDeleted(logger, bookUid);
        return DeleteBookOutcome.Deleted;
    }

    /// <summary>Brings a deleted book back as it was when it was deleted; an active or unknown book is not found.</summary>
    public async Task<RestoreBookOutcome> RestoreAsync(Guid bookUid, CancellationToken cancellationToken)
    {
        var restored = await db.Books.Deleted()
            .Where(b => b.Uid == bookUid)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(b => b.IsActive, true)
                    .SetProperty(b => b.DeletedAt, (DateTime?)null),
                cancellationToken)
            .ConfigureAwait(false);
        if (restored == 0)
        {
            return RestoreBookOutcome.NotFound;
        }

        LogRestored(logger, bookUid);
        return RestoreBookOutcome.Restored;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} queued for generation")]
    private static partial void LogQueued(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Book {BookUid} could not be enqueued; it stays queued and the queued-book sweeper sends it later")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} deleted (kept as inactive)")]
    private static partial void LogDeleted(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Information, Message = "Book {BookUid} restored")]
    private static partial void LogRestored(ILogger logger, Guid bookUid);
}
