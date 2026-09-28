using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using BildiriKitabi.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BildiriKitabi.Core.Application;

/// <summary>
/// Recovers books left <c>Processing</c> by a run that will never finish: the process stopped, or the run failed and
/// its failure could not be written. Shared by the startup recovery and the sweeper. Each attempt gets one more run:
/// the first recovery returns the book to the queue (<see cref="Book.ReturnToQueue"/> marks it as interrupted), a book
/// found stuck again is marked <c>Failed</c> with <c>GENERATION_INTERRUPTED</c>. No extra column is needed because a
/// run in progress has no <see cref="Book.ProcessingFinishedAt"/> unless an earlier run was interrupted.
/// </summary>
public sealed partial class InterruptedGenerationRecovery(
    IAppDbContext db,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<InterruptedGenerationRecovery> logger)
{
    public const string InterruptedMessage = "Kitap oluşturma yarıda kaldı. Lütfen tekrar deneyin.";

    private const int BatchSize = 100;

    /// <summary>True for a processing book whose run started at least <paramref name="stuckAfter"/> ago (or has no start time).</summary>
    public static bool IsStuck(BookStatus status, DateTime? startedAt, DateTime now, TimeSpan stuckAfter) =>
        status == BookStatus.Processing && (startedAt is null || now - startedAt.Value >= stuckAfter);

    /// <summary>What recovery does with a book whose run is known to be dead.</summary>
    public static RecoveryAction Decide(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.Status != BookStatus.Processing)
        {
            return RecoveryAction.None;
        }

        return book.WasInterrupted ? RecoveryAction.Fail : RecoveryAction.Requeue;
    }

    /// <summary>
    /// Recovers the processing books whose run started at or before <paramref name="startedBefore"/>; every processing
    /// book when it is null (at startup no run can be alive). A row changed meanwhile, for example by a run that is still
    /// writing progress, fails the row version check and is left alone.
    /// </summary>
    /// <returns>The uids of the books returned to the queue, oldest run first; the caller enqueues them.</returns>
    public async Task<IReadOnlyList<Guid>> RecoverAsync(DateTime? startedBefore, CancellationToken cancellationToken)
    {
        var query = db.Books.Where(b => b.Status == BookStatus.Processing);
        if (startedBefore is { } threshold)
        {
            query = query.Where(b => b.ProcessingStartedAt == null || b.ProcessingStartedAt <= threshold);
        }

        var stuck = await query.OrderBy(b => b.ProcessingStartedAt).Take(BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var requeued = new List<Guid>();
        var failed = 0;
        foreach (var book in stuck)
        {
            var action = Decide(book);
            if (action == RecoveryAction.None)
            {
                continue;
            }

            if (action == RecoveryAction.Requeue)
            {
                book.ReturnToQueue(now);
            }
            else
            {
                book.MarkFailed(BookErrorCodes.GenerationInterrupted, InterruptedMessage, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                LogChangedMeanwhile(logger, book.Uid);
                foreach (var entry in db.ChangeTracker.Entries<Book>().Where(e => e.Entity == book))
                {
                    entry.State = EntityState.Detached;
                }

                continue;
            }

            if (action == RecoveryAction.Requeue)
            {
                requeued.Add(book.Uid);
            }
            else
            {
                failed++;
                await DeleteOutputAsync(book.Uid).ConfigureAwait(false);
            }
        }

        if (stuck.Count > 0)
        {
            LogRecovered(logger, requeued.Count, failed);
        }

        return requeued;
    }

    private async Task DeleteOutputAsync(Guid bookUid)
    {
        try
        {
            await storage.DeleteAsync(StorageKeys.Output(bookUid), CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            // A leftover PDF of a failed book is never served; deleting the book removes it.
            LogOutputDeleteFailed(logger, ex, bookUid);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Interrupted generation: {RequeuedCount} book(s) returned to the queue, {FailedCount} book(s) interrupted again marked failed")]
    private static partial void LogRecovered(ILogger logger, int requeuedCount, int failedCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Book {BookUid} changed while it was being recovered; left alone")]
    private static partial void LogChangedMeanwhile(ILogger logger, Guid bookUid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Output of failed book {BookUid} could not be deleted")]
    private static partial void LogOutputDeleteFailed(ILogger logger, Exception exception, Guid bookUid);
}

public enum RecoveryAction
{
    None,
    Requeue,
    Fail,
}
