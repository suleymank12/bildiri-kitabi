using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Configuration;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BildiriKitabi.Core.Application;

/// <summary>
/// Enqueues again the books that have been <c>Queued</c> for longer than <c>Generation:RequeueStaleAfterSeconds</c>:
/// their message may never have reached the broker (it was down when "generate" was called) or may have been lost.
/// The database row is the record of intent, like a simple outbox; a duplicate message is harmless because only
/// one handler can claim a queued book.
/// </summary>
public sealed partial class QueueSweepService(
    IAppDbContext db,
    IBookGenerationQueue queue,
    IOptions<GenerationOptions> options,
    TimeProvider timeProvider,
    ILogger<QueueSweepService> logger)
{
    private const int BatchSize = 100;

    /// <summary>True for a queued book that has waited at least <paramref name="staleAfter"/> (or has no queue time).</summary>
    public static bool IsStale(BookStatus status, DateTime? queuedAt, DateTime now, TimeSpan staleAfter) =>
        status == BookStatus.Queued && (queuedAt is null || now - queuedAt.Value >= staleAfter);

    /// <returns>The number of books enqueued again.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var threshold = now - TimeSpan.FromSeconds(options.Value.RequeueStaleAfterSeconds);
        var stale = await db.Books.AsNoTracking()
            .Where(b => b.Status == BookStatus.Queued && (b.QueuedAt == null || b.QueuedAt <= threshold))
            .OrderBy(b => b.QueuedAt)
            .Select(b => b.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var requeued = 0;
        foreach (var bookId in stale)
        {
            try
            {
                await queue.EnqueueAsync(bookId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The queue is still unavailable; the next sweep tries again.
                LogEnqueueFailed(logger, ex, stale.Count - requeued);
                break;
            }

            // Restart the waiting time so the book is not enqueued again on every sweep while it waits its turn.
            await db.Books
                .Where(b => b.Id == bookId && b.Status == BookStatus.Queued)
                .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.QueuedAt, now), cancellationToken)
                .ConfigureAwait(false);
            requeued++;
        }

        if (requeued > 0)
        {
            LogRequeued(logger, requeued);
        }

        return requeued;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued-book sweep enqueued {Count} waiting book(s) again")]
    private static partial void LogRequeued(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Queued-book sweep could not enqueue; {Remaining} book(s) wait for the next sweep")]
    private static partial void LogEnqueueFailed(ILogger logger, Exception exception, int remaining);
}
